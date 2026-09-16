using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.12 <c>ManaRecharger</c> singleton think loop.
/// Owned per-session by <see cref="AutoRecharge"/> instead of being a true
/// process-wide singleton (the original ran inside one Decal process per
/// account; this plugin instance already is that scope) — see docs/deviations.md.
/// </summary>
/// <remarks>
/// Selection rules, verbatim from the inventory doc: prefer an identified
/// (since this recharge run started) "Mana Stone" with current mana &gt; 0;
/// otherwise request an id for the first stale/unidentified "Mana Stone";
/// otherwise fall back to the "Mana Charge" with the smallest current mana.
/// Switching to a different stone than the last one applied waits 5 seconds
/// since the last attempt. Stops on a chat line containing
/// "The Mana Stone gives", or on logoff.
/// </remarks>
public sealed class ManaRecharger
{
    private static readonly TimeSpan SwitchCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ThinkInterval = TimeSpan.FromMilliseconds(100);

    private readonly IPluginHost _host;
    private readonly TimeProvider _timeProvider;

    private IDisposable? _thinkRegistration;
    private Action<PluginChatMessage>? _onChatReceived;
    private Action? _onLogoff;
    private DateTime _startUtc;
    private DateTime _lastAttemptUtc;
    private uint _lastAppliedObjectId;
    private bool _running;

    public ManaRecharger(IPluginHost host, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsRunning => _running;

    public void Start(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;

        _running = true;
        _startUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _lastAttemptUtc = DateTime.MinValue;
        _lastAppliedObjectId = 0u;

        _onLogoff = Stop;
        _host.Events.Logoff += _onLogoff;

        _onChatReceived = OnChatReceived;
        _host.Automation.Chat.Received += _onChatReceived;

        _thinkRegistration = scheduler.Every(ThinkInterval, Think);
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

        _thinkRegistration?.Dispose();
        _thinkRegistration = null;

        if (_onChatReceived is not null)
            _host.Automation.Chat.Received -= _onChatReceived;
        _onChatReceived = null;

        if (_onLogoff is not null)
            _host.Events.Logoff -= _onLogoff;
        _onLogoff = null;
    }

    private void OnChatReceived(PluginChatMessage message)
    {
        if (message.Text.Contains("The Mana Stone gives", StringComparison.Ordinal))
            Stop();
    }

    private void Think()
    {
        if (!_running)
            return;

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        List<(PluginInventoryItem Item, PluginWorldObject World)> stones = [];
        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (item.ObjectClass != PluginObjectClass.ManaStone)
                continue;
            if (!item.Name.Contains("Mana Stone", StringComparison.Ordinal)
                && !item.Name.Contains("Mana Charge", StringComparison.Ordinal))
                continue;

            _host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject world);
            stones.Add((item, world));
        }

        if (stones.Count == 0)
            return;

        bool switchAllowed = now - _lastAttemptUtc >= SwitchCooldown;

        // 1. A "Mana Stone" identified since this recharge run started, with
        // mana left, wins outright.
        foreach ((PluginInventoryItem item, PluginWorldObject world) in stones)
        {
            if (!item.Name.Contains("Mana Stone", StringComparison.Ordinal))
                continue;
            if (!world.HasAppraisalData || IdentPredatesStart(world))
                continue;
            if (item.ItemCurrentMana <= 0)
                continue;

            TryApply(item.ObjectId, now, switchAllowed);
            return;
        }

        // 2. Otherwise request an id for the first stale/unidentified "Mana Stone".
        foreach ((PluginInventoryItem item, PluginWorldObject world) in stones)
        {
            if (!item.Name.Contains("Mana Stone", StringComparison.Ordinal))
                continue;
            if (world.HasAppraisalData && !IdentPredatesStart(world))
                continue;

            _host.Automation.Objects.Identify(item.ObjectId);
            return;
        }

        // 3. Fall back to the "Mana Charge" with the smallest current mana.
        (PluginInventoryItem Item, PluginWorldObject World)? smallest = null;
        foreach ((PluginInventoryItem item, PluginWorldObject world) in stones)
        {
            if (!item.Name.Contains("Mana Charge", StringComparison.Ordinal))
                continue;
            if (smallest is null || item.ItemCurrentMana < smallest.Value.Item.ItemCurrentMana)
                smallest = (item, world);
        }

        if (smallest is { } chosen)
            TryApply(chosen.Item.ObjectId, now, switchAllowed);
    }

    private bool IdentPredatesStart(PluginWorldObject world)
        => DateTimeOffset.FromUnixTimeSeconds(world.LastIdTime).UtcDateTime < _startUtc;

    private void TryApply(uint objectId, DateTime now, bool switchAllowed)
    {
        if (objectId != _lastAppliedObjectId && !switchAllowed)
            return;

        _host.Automation.Items.Apply(objectId, _host.Automation.Character.ObjectId);
        _lastAppliedObjectId = objectId;
        _lastAttemptUtc = now;
    }
}
