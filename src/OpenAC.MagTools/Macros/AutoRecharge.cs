using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.12 <c>AutoRecharge</c>: watches chat for a "low
/// on mana" warning and starts <see cref="ManaRecharger"/>. Gated by
/// <c>ManaManagement/AutoRecharge</c> (default true, also the Mana tab's
/// "Recharge" checkbox).
/// </summary>
public sealed class AutoRecharge
{
    private readonly IPluginHost _host;
    private readonly SettingsManager _settings;
    private readonly ManaRecharger _recharger;
    private Action<PluginChatMessage>? _onChatReceived;
    private TickScheduler? _scheduler;
    private bool _running;

    public AutoRecharge(IPluginHost host, SettingsManager settings, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
        _recharger = new ManaRecharger(host, timeProvider);
    }

    /// <summary>Exposed so the Mana page can show the recharger's live state.</summary>
    public ManaRecharger Recharger => _recharger;

    public void Start(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;

        _running = true;
        _scheduler = scheduler;

        _onChatReceived = OnChatReceived;
        _host.Automation.Chat.Received += _onChatReceived;
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

        if (_onChatReceived is not null)
            _host.Automation.Chat.Received -= _onChatReceived;
        _onChatReceived = null;

        _recharger.Stop();
        _scheduler = null;
    }

    private void OnChatReceived(PluginChatMessage message)
    {
        if (!_settings.ManaManagement.AutoRecharge.Value)
            return;

        string text = message.Text;
        if (string.IsNullOrEmpty(text))
            return;

        // The original's exact ignore rules: your own /say echo, and any
        // line another player said (which could quote the warning text).
        if (text.StartsWith("You say, ", StringComparison.Ordinal))
            return;
        if (text.Contains("says, \"", StringComparison.Ordinal))
            return;

        if (!text.Contains("Your", StringComparison.Ordinal)
            || !text.Contains(" is low on Mana.", StringComparison.Ordinal))
            return;

        if (_scheduler is not null && !_recharger.IsRunning)
            _recharger.Start(_scheduler);
    }
}
