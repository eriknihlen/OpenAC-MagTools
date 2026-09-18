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
/// <para>
/// Owner review (2026-09-18): every 2 s think tick walked
/// <see cref="IItemAutomation.CaptureOwnedItems"/> unconditionally, even when
/// none of the five toggles could possibly act (a live measurement found 30
/// of 32 owned-item captures in a 60-tick headless run came from here, with
/// <c>InventoryManagementSettings.AetheriaRevealer</c> defaulting to true so
/// the "all toggles off" early return never fires). The owned-item-derived
/// half of <see cref="BuildSnapshot"/> is now event-driven: <see cref="Start"/>
/// subscribes to <see cref="IEvents.ObjectChanged"/> and marks
/// <see cref="_itemSnapshotDirty"/> whenever a change is reported for an
/// object the local player OWNS (the same <see cref="PluginWorldObject.IsOwned"/>
/// filter <c>InventoryTrackerHost.IsInventoryRelevant</c> and
/// <c>InventoryLogger</c>'s ownership check already use — <c>ObjectChanged</c>
/// fires for every world entity, not just owned ones). <see cref="Think"/>
/// only re-walks <see cref="IItemAutomation.CaptureOwnedItems"/> when that
/// flag is set (or on the first think after <see cref="Start"/>/a
/// reconnect), then clears it; the walk's results are cached in
/// <see cref="_cachedItems"/> in between. The 2 s cadence itself is
/// unchanged — an action still fires within 2 s of the change that enabled
/// it, same as the original's timer-plus-wake-trigger design. The
/// chest-proximity scan and the lockpick-training check are NOT part of this
/// cache (they are cheap property/position reads, not an inventory walk, and
/// have no owning <c>ObjectChanged</c> signal of their own — e.g. a skill
/// change) so they still run fresh every think, same as before. See
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
    private Action<PluginObjectChange>? _onObjectChanged;
    private TickScheduler? _scheduler;
    private double _lastActionElapsedSeconds = double.NegativeInfinity;
    private bool _confirmationArmed;
    private bool _running;

    /// <summary>
    /// Set by <see cref="Start"/> (first think always rebuilds, including
    /// after a reconnect) and by <see cref="OnObjectChanged"/> when a
    /// relevant owned-item change arrives; cleared once
    /// <see cref="BuildSnapshot"/> re-walks <see cref="IItemAutomation.CaptureOwnedItems"/>
    /// and refreshes <see cref="_cachedItems"/>. See the class remarks.
    /// </summary>
    private bool _itemSnapshotDirty = true;

    /// <summary>The owned-item-derived half of the last <see cref="IdleActionSnapshot"/> built, reused while <see cref="_itemSnapshotDirty"/> is clear.</summary>
    private CachedItemIds _cachedItems;

    /// <summary>
    /// Keyring object ids an id has already been requested for, this run --
    /// P6 re-review: the original's Create/Change wake triggers really did
    /// re-request every 2-second tick until IdentReceived, but that was a
    /// side effect of Decal's own per-event dispatch, not a deliberate
    /// "spam the server" design; a plugin identify can also be legitimately
    /// <c>Refused</c>/<c>Busy</c> while a user appraisal is in flight (see
    /// docs/plugin-api.md's Identify section), so repeating the SAME request
    /// every tick wastes a slot without a matching benefit. Requested once
    /// per object id until <c>HasAppraisalData</c> flips true.
    /// </summary>
    private readonly HashSet<uint> _keyringIdRequested = [];

    /// <summary>The owned-item ids <see cref="BuildSnapshot"/> derives from a <see cref="IItemAutomation.CaptureOwnedItems"/> walk, cached between walks.</summary>
    private readonly record struct CachedItemIds(
        uint AetheriaManaStoneId,
        uint CoalescedAetheriaId,
        uint IntricateCarvingToolId,
        uint HeartItemId,
        uint ShatteredKeyItemId,
        uint BestKeyringForRingingId,
        uint KeyringWithKeysId,
        uint AgedLegendaryKeyId);

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
        // A fresh Start (including a reconnect after Stop) must re-walk the
        // owned pack on its first think rather than trust whatever was
        // cached from a previous session.
        _itemSnapshotDirty = true;
        _cachedItems = default;

        _tickRegistration = scheduler.Every(ThinkInterval, Think);

        _onConfirmation = OnConfirmationRequested;
        _host.Events.ConfirmationRequested += _onConfirmation;

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;
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

        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;

        _confirmationArmed = false;
        _keyringIdRequested.Clear();
    }

    /// <summary>
    /// Marks the cached owned-item snapshot dirty for a change to an object
    /// the local player OWNS. Mirrors
    /// <c>InventoryTrackerHost.IsInventoryRelevant</c>/<c>InventoryLogger</c>'s
    /// ownership check: <see cref="IEvents.ObjectChanged"/> fires for every
    /// world entity the client is tracking (a monster spawning, another
    /// player crossing a cell boundary), not just owned ones, so an
    /// unfiltered subscription would defeat the whole point of this cache. A
    /// <see cref="PluginObjectChangeKind.Released"/> object can no longer be
    /// resolved (it already left the object table by the time this handler
    /// runs), so it counts as relevant unconditionally rather than risk
    /// missing a real drop/consume/give-away. <see cref="PluginObjectChangeKind.IdentReceived"/>
    /// is covered the same way -- an owned Burning Sands Keyring's
    /// UsesRemaining/KeysHeld only become knowable once its appraisal data
    /// arrives, which is exactly what this event reports.
    /// </summary>
    private void OnObjectChanged(PluginObjectChange change)
    {
        if (change.Kind == PluginObjectChangeKind.Released)
        {
            _itemSnapshotDirty = true;
            return;
        }

        if (_host.Automation.Objects.TryGet(change.ObjectId, out PluginWorldObject world) && world.IsOwned)
            _itemSnapshotDirty = true;
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

        // The original's timerWithChestOpen (2 s cadence while a container is
        // open) only ever evaluates KeyDeringer -- Aetheria/heart/shattered/
        // ringing all defer to the main (container-closed) timer (M3).
        if (automation.Objects.OpenContainerObjectId != 0u)
        {
            options = options with
            {
                AetheriaRevealer = false,
                HeartCarver = false,
                ShatteredKeyFixer = false,
                KeyRinger = false,
            };
        }

        IdleActionSnapshot snapshot = BuildSnapshot(automation, options);
        IdleActionPlan? plan = IdleActionPlanner.Plan(options, snapshot);
        if (plan is not { } chosen)
            return;

        _host.Selection.Select(chosen.TargetObjectId);
        automation.Items.Apply(chosen.ToolObjectId, chosen.TargetObjectId);

        // Only heart carving / shattered-key fixing can raise the "chance to
        // succeed" confirmation (H4) -- Aetheria reveal and key ring/dering
        // never do, so arming the window for them was a false positive that
        // could swallow an unrelated confirmation dialog within 5 seconds of
        // an idle action.
        if (_scheduler is not null
            && chosen.FeatureName is "HeartCarver" or "ShatteredKeyFixer")
        {
            _lastActionElapsedSeconds = _scheduler.ElapsedSeconds;
            _confirmationArmed = true;
        }
    }

    private IdleActionSnapshot BuildSnapshot(IAutomationSurface automation, IdleActionOptions options)
    {
        if (_itemSnapshotDirty)
        {
            _cachedItems = CaptureItemIds(automation, options);
            _itemSnapshotDirty = false;
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
            _cachedItems.AetheriaManaStoneId,
            _cachedItems.CoalescedAetheriaId,
            _cachedItems.IntricateCarvingToolId,
            _cachedItems.HeartItemId,
            _cachedItems.ShatteredKeyItemId,
            _cachedItems.BestKeyringForRingingId,
            _cachedItems.KeyringWithKeysId,
            _cachedItems.AgedLegendaryKeyId,
            chestNearby,
            automation.Objects.OpenContainerObjectId != 0u,
            lockpickTrained);
    }

    /// <summary>
    /// The owned-item walk itself (<see cref="IItemAutomation.CaptureOwnedItems"/>
    /// plus the per-item classification loop) -- only called from
    /// <see cref="BuildSnapshot"/> while <see cref="_itemSnapshotDirty"/> is
    /// set. Unchanged from the original per-tick version except for being
    /// split out of <see cref="BuildSnapshot"/> so it can be skipped.
    /// </summary>
    private CachedItemIds CaptureItemIds(IAutomationSurface automation, IdleActionOptions options)
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
                    {
                        // The original's wake triggers kept requesting id
                        // data for an unidentified keyring on every Create/
                        // Change until IdentReceived (H3) -- without SOME
                        // request, a keyring that never happened to get
                        // appraised by some other path could never become
                        // ring/dering reachable. Deduped to once per object
                        // id (P6 re-review) rather than re-issued every
                        // 2-second tick: see the remark on
                        // _keyringIdRequested.
                        if ((options.KeyRinger || options.KeyDeringer)
                            && _keyringIdRequested.Add(item.ObjectId))
                            automation.Objects.Identify(item.ObjectId);
                        break;
                    }

                    // Now appraised -- if it comes back unidentified again in
                    // some future session (a fresh object id reuse, in
                    // practice), it must be eligible for a fresh request.
                    _keyringIdRequested.Remove(item.ObjectId);
                    automation.Objects.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties);
                    int usesRemaining = properties.Ints.TryGetValue((uint)ItemModel.UsesRemainingKey, out int uses) ? uses : 0;
                    int keysHeld = properties.Ints.TryGetValue((uint)ItemModel.KeysHeldKey, out int keys) ? keys : 0;

                    // First match wins for the deringing target (the original
                    // `break`s on the first KeysHeld > 0 ring it finds; M4).
                    if (keysHeld > 0 && keyringWithKeys == 0u)
                        keyringWithKeys = item.ObjectId;

                    // Ported verbatim from IdleActionManager's ring-selection
                    // loop: `bestKeyRing == null || best.KeysHeld <
                    // wo.KeysHeld` picks the ring with strictly MORE keys;
                    // only when tied at exactly ZERO keys does it switch to
                    // whichever has FEWER uses remaining (M4) -- a nonzero
                    // tie keeps whichever ring was found first.
                    if (usesRemaining > 0 && keysHeld < 24)
                    {
                        if (bestRingingId == 0u || keysHeld > bestRingingKeysHeld)
                        {
                            bestRingingId = item.ObjectId;
                            bestRingingKeysHeld = keysHeld;
                            bestRingingUses = usesRemaining;
                        }
                        else if (bestRingingKeysHeld == 0 && bestRingingUses > usesRemaining)
                        {
                            bestRingingId = item.ObjectId;
                            bestRingingKeysHeld = keysHeld;
                            bestRingingUses = usesRemaining;
                        }
                    }
                    break;
                case PluginObjectClass.Key when item.Name == "Aged Legendary Key":
                    agedLegendaryKey = item.ObjectId;
                    break;
            }
        }

        return new CachedItemIds(
            aetheriaManaStone,
            coalescedAetheria,
            carvingTool,
            heartItem,
            shatteredKey,
            bestRingingId,
            keyringWithKeys,
            agedLegendaryKey);
    }

    /// <summary>
    /// Answers a type-5 confirmation within <see cref="ConfirmationWindow"/>
    /// of an executed HEART-CARVING or SHATTERED-KEY-FIXING idle action --
    /// the only two idle actions that can raise one (H4) -- unconditionally,
    /// with no percent to check (unlike <see cref="TinkeringAutoConfirm"/>).
    /// One-shot: answering disarms the window immediately rather than
    /// staying armed to catch a second, unrelated confirmation for the rest
    /// of the 5 seconds.
    /// </summary>
    private void OnConfirmationRequested(PluginConfirmation confirmation)
    {
        if (confirmation.Type != TinkeringAutoConfirm.CraftingPercentType)
            return;
        if (!_confirmationArmed || _scheduler is null)
            return;
        if (_scheduler.ElapsedSeconds - _lastActionElapsedSeconds > ConfirmationWindow.TotalSeconds)
        {
            _confirmationArmed = false;
            return;
        }

        _confirmationArmed = false;
        _host.Automation.Dialogs.Answer(confirmation.ContextId, true);
    }
}
