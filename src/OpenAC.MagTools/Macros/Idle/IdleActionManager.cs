using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros.Idle;

/// <summary>
/// Ports <c>Macros/IdleActionManager.cs</c>: while idle in Peace mode, use the
/// right tool on the right item (Aetheria reveal, heart carving, shattered-key
/// fixing, key ringing/deringing), per the port design's §1.10.
/// </summary>
/// <remarks>
/// <para>
/// The original ran a 4000 ms main timer and a 2000 ms
/// <c>timerWithChestOpen</c> that took over only while a container was open,
/// with a scattered set of <c>WorldFilter.CreateObject</c>/<c>ChangeObject</c>/
/// <c>ReleaseObject</c> "wake-up" triggers that restarted whichever timer was
/// due. This port collapses that to ONE uniform tick: every 2 seconds while
/// any of the five toggles is on, gated the same way (Peace mode, not busy);
/// the container-open case simply always runs at the faster 2 s cadence. The
/// observable behavior — react within a few seconds of the right conditions
/// existing — is the same; only the exact scheduling mechanics differ. See
/// docs/deviations.md.
/// </para>
/// </remarks>
public sealed class IdleActionManager
{
    /// <summary>ACE's Lockpick skill id (confirmed via this port's own <c>Dictionaries.SkillInfo</c> table, id 0x17).</summary>
    public const uint LockpickSkillId = 0x17u;

    private static readonly TimeSpan ThinkInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(5);

    private readonly IPluginHost _host;
    private readonly InventoryManagementSettings _settings;
    private IDisposable? _tickRegistration;
    private Action<PluginConfirmation>? _onConfirmation;
    private TickScheduler? _scheduler;
    private double _lastActionElapsedSeconds = double.NegativeInfinity;
    private bool _running;

    public IdleActionManager(IPluginHost host, InventoryManagementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
    }

    public void Start(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;
        _running = true;
        _scheduler = scheduler;

        _tickRegistration = scheduler.Every(ThinkInterval, Think);

        _onConfirmation = OnConfirmationRequested;
        _host.Events.ConfirmationRequested += _onConfirmation;
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;

        _tickRegistration?.Dispose();
        _tickRegistration = null;
        _scheduler = null;

        if (_onConfirmation is not null)
            _host.Events.ConfirmationRequested -= _onConfirmation;
        _onConfirmation = null;
    }

    private void Think()
    {
        IdleActionOptions options = new(
            AetheriaRevealer: _settings.AetheriaRevealer.Value,
            HeartCarver: _settings.HeartCarver.Value,
            ShatteredKeyFixer: _settings.ShatteredKeyFixer.Value,
            KeyRinger: _settings.KeyRinger.Value,
            KeyDeringer: _settings.KeyDeringer.Value);

        if (!options.AetheriaRevealer && !options.HeartCarver && !options.ShatteredKeyFixer
            && !options.KeyRinger && !options.KeyDeringer)
            return;

        IAutomationSurface automation = _host.Automation;
        if (!automation.IsAvailable)
            return;
        if (automation.Combat.Snapshot.Mode != PluginCombatMode.Peace)
            return;
        if (automation.Items.IsBusy)
            return;

        IdleActionSnapshot snapshot = BuildSnapshot(automation);
        IdleActionPlan? plan = IdleActionPlanner.Plan(options, snapshot);
        if (plan is not { } chosen)
            return;

        _host.Selection.Select(chosen.TargetObjectId);
        automation.Items.Apply(chosen.ToolObjectId, chosen.TargetObjectId);

        if (_scheduler is not null)
            _lastActionElapsedSeconds = _scheduler.ElapsedSeconds;
    }

    private IdleActionSnapshot BuildSnapshot(IAutomationSurface automation)
    {
        uint aetheriaManaStone = 0u, coalescedAetheria = 0u, carvingTool = 0u, heartItem = 0u, shatteredKey = 0u;
        uint agedLegendaryKey = 0u;

        // Best key-ringing candidate: has id data, UsesRemaining > 0,
        // KeysHeld < 24, preferring the ring with the MOST keys (tie-broken
        // by most uses).
        uint bestRingingId = 0u;
        int bestRingingKeysHeld = -1;
        int bestRingingUses = -1;
        uint keyringWithKeys = 0u;

        foreach (PluginInventoryItem item in automation.Items.CaptureOwnedItems())
        {
            switch (item.ObjectClass)
            {
                case PluginObjectClass.Gem when item.Name == "Aetheria Mana Stone":
                    aetheriaManaStone = item.ObjectId;
                    break;
                case PluginObjectClass.Gem when item.Name == "Coalesced Aetheria":
                    coalescedAetheria = item.ObjectId;
                    break;
                case PluginObjectClass.Misc when item.Name == "Intricate Carving Tool":
                    carvingTool = item.ObjectId;
                    break;
                case PluginObjectClass.Misc when item.Name.EndsWith(" Heart", StringComparison.Ordinal):
                    heartItem = item.ObjectId;
                    break;
                case PluginObjectClass.Misc
                    when item.Name.StartsWith("Shattered ", StringComparison.Ordinal)
                        && item.Name.EndsWith(" Key", StringComparison.Ordinal):
                    shatteredKey = item.ObjectId;
                    break;
                case PluginObjectClass.Misc when item.Name == "Burning Sands Keyring":
                    if (!automation.Objects.TryGet(item.ObjectId, out PluginWorldObject wo) || !wo.HasAppraisalData)
                        break;
                    automation.Objects.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties);
                    int usesRemaining = properties.Ints.TryGetValue((uint)ItemModel.UsesRemainingKey, out int uses) ? uses : 0;
                    int keysHeld = properties.Ints.TryGetValue((uint)ItemModel.KeysHeldKey, out int keys) ? keys : 0;

                    if (keysHeld > 0)
                        keyringWithKeys = item.ObjectId;

                    if (usesRemaining > 0 && keysHeld < 24
                        && (keysHeld > bestRingingKeysHeld
                            || (keysHeld == bestRingingKeysHeld && usesRemaining > bestRingingUses)))
                    {
                        bestRingingId = item.ObjectId;
                        bestRingingKeysHeld = keysHeld;
                        bestRingingUses = usesRemaining;
                    }
                    break;
                case PluginObjectClass.Key when item.Name == "Aged Legendary Key":
                    agedLegendaryKey = item.ObjectId;
                    break;
            }
        }

        bool chestNearby = false;
        PluginNavigationSnapshot navSnapshot = automation.Navigation.Snapshot;
        if (navSnapshot.IsAvailable)
        {
            foreach (PluginNavigationObject candidate in automation.Navigation.CaptureObjects())
            {
                if (!candidate.Name.Contains(" Chest", StringComparison.Ordinal))
                    continue;
                if (navSnapshot.Position.HorizontalDistanceMeters(candidate.Position) <= 10d)
                {
                    chestNearby = true;
                    break;
                }
            }
        }

        bool lockpickTrained = automation.Character.TryGetSkill(LockpickSkillId, out PluginSkillInfo lockpick)
            && lockpick.Training >= PluginSkillTraining.Trained;

        return new IdleActionSnapshot(
            aetheriaManaStone,
            coalescedAetheria,
            carvingTool,
            heartItem,
            shatteredKey,
            bestRingingId,
            keyringWithKeys,
            agedLegendaryKey,
            chestNearby,
            automation.Objects.OpenContainerObjectId != 0u,
            lockpickTrained);
    }

    /// <summary>
    /// Answers any type-5 confirmation within <see cref="ConfirmationWindow"/>
    /// of the last executed idle action, unconditionally (unlike
    /// <see cref="TinkeringAutoConfirm"/>, there is no percent to check here —
    /// the original clicks yes on whatever dialog these actions raised).
    /// </summary>
    private void OnConfirmationRequested(PluginConfirmation confirmation)
    {
        if (confirmation.Type != TinkeringAutoConfirm.CraftingPercentType)
            return;
        if (_scheduler is null)
            return;
        if (_scheduler.ElapsedSeconds - _lastActionElapsedSeconds > ConfirmationWindow.TotalSeconds)
            return;

        _host.Automation.Dialogs.Answer(confirmation.ContextId, true);
    }
}
