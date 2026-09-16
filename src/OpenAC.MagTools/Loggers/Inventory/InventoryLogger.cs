using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Loggers.Inventory;

/// <summary>
/// Ports <c>Inventory/InventoryLogger.cs</c>: on login, request id data for
/// every ident-worthy owned item that lacks it (only when the file doesn't
/// exist yet), otherwise dump immediately; keep requesting ids for newly
/// arrived items thereafter; dump again on logoff. The dump merges with the
/// previous file via <see cref="MyWorldObjectRecord.Combine"/>.
/// </summary>
public sealed class InventoryLogger
{
    /// <summary>
    /// How often the post-startup snapshot poll runs (HIGH-3, P10 review):
    /// once per second through the plugin's one <see cref="TickScheduler"/>
    /// clock, instead of a raw <see cref="IEvents.Tick"/> subscription
    /// driving a full <see cref="IItemAutomation.CaptureOwnedItems"/> walk
    /// (object-table walk + per-item projection/allocation + sort) every
    /// single frame.
    /// </summary>
    private static readonly TimeSpan SnapshotPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many 1-second polls to wait for the owned pack to stream in
    /// before giving up on the startup capture entirely (HIGH-3) -- about a
    /// minute. If the pack never populates, nothing was ever captured and
    /// <see cref="Stop"/> will not write anything either (HIGH-2).
    /// </summary>
    private const int StartupGiveUpPolls = 60;

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly InventoryManagementSettings _settings;
    private readonly HashSet<uint> _requestedIds = [];
    private Action<PluginObjectChange>? _onObjectChanged;
    private string _storageKey = string.Empty;
    private bool _waitingForIdData;
    private bool _running;
    private IDisposable? _snapshotPoll;
    private bool _startupCaptureDone;
    private int _startupPollCount;

    /// <summary>
    /// The last non-empty owned-item snapshot this instance has actually
    /// captured. <see cref="Stop"/> dumps from this rather than a fresh
    /// <see cref="IItemAutomation.CaptureOwnedItems"/> call, because by the
    /// time logoff teardown runs the host has typically already released
    /// the owned objects -- a fresh capture there is reliably empty even
    /// though real items were logged earlier in the session (defect 8).
    /// Refreshed whenever <see cref="Dump"/> runs with real items AND on
    /// every post-startup <see cref="OnSnapshotPoll"/> tick (HIGH-1, P10
    /// review) -- not just at startup -- so it reflects anything looted,
    /// bought, or tinkered mid-session, not only the login-time inventory.
    /// </summary>
    private IReadOnlyList<PluginInventoryItem> _lastOwnedSnapshot = [];

    public InventoryLogger(IPluginHost host, ChatOutput chat, InventoryManagementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _chat = chat;
        _settings = settings;
    }

    /// <summary><c>ObjectClassNeedsIdent</c>, verbatim rule set.</summary>
    public static bool ObjectClassNeedsIdent(PluginObjectClass objectClass, string name)
    {
        if (objectClass is PluginObjectClass.Armor or PluginObjectClass.Clothing
            or PluginObjectClass.MeleeWeapon or PluginObjectClass.MissileWeapon
            or PluginObjectClass.WandStaffOrb or PluginObjectClass.Jewelry)
            return true;

        if (objectClass == PluginObjectClass.Gem && !string.IsNullOrEmpty(name)
            && name.Contains("Aetheria", StringComparison.Ordinal))
            return true;

        if (objectClass == PluginObjectClass.Misc && !string.IsNullOrEmpty(name)
            && name.Contains("Essence", StringComparison.Ordinal))
            return true;

        return false;
    }

    /// <summary>
    /// Starts the logger for a session. <paramref name="scheduler"/> is the
    /// plugin's one <see cref="TickScheduler"/> clock (HIGH-3, P10 review) --
    /// already constructed and running by the time <c>OnSessionReady</c>
    /// calls this, so every poll this class needs hangs off it rather than
    /// a raw <see cref="IEvents.Tick"/> subscription.
    /// </summary>
    public void Start(string server, string character, TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;
        _running = true;
        _requestedIds.Clear();
        _waitingForIdData = false;
        _storageKey = server + "/" + character + ".Inventory.xml";
        _lastOwnedSnapshot = [];
        _startupCaptureDone = false;
        _startupPollCount = 0;

        if (!_settings.InventoryLogger.Value)
        {
            _onObjectChanged = OnObjectChanged;
            _host.Events.ObjectChanged += _onObjectChanged;
            return;
        }

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;

        // At SessionReady time the owned pack has not necessarily streamed
        // in yet (defect 8); if it's already populated (the common case --
        // e.g. a reconnect where the character was already fully in-world)
        // this runs immediately, exactly like the original.
        IReadOnlyList<PluginInventoryItem> items = _host.Automation.Items.CaptureOwnedItems();
        if (items.Count > 0)
        {
            _startupCaptureDone = true;
            RunStartupCapture(items);
        }

        // HIGH-3: poll through the plugin's one TickScheduler clock at 1 Hz
        // instead of a raw Events.Tick subscription driving a full
        // CaptureOwnedItems() walk every frame. Before the startup capture
        // succeeds this also counts toward a bounded give-up
        // (StartupGiveUpPolls); once it has succeeded, the SAME poll keeps
        // _lastOwnedSnapshot fresh for the rest of the session (HIGH-1).
        _snapshotPoll = scheduler.Every(SnapshotPollInterval, OnSnapshotPoll);
    }

    private void OnSnapshotPoll()
    {
        IReadOnlyList<PluginInventoryItem> items = _host.Automation.Items.CaptureOwnedItems();

        if (items.Count == 0)
        {
            if (_startupCaptureDone)
                return;

            _startupPollCount++;
            if (_startupPollCount < StartupGiveUpPolls)
                return;

            // Bounded give-up: the pack never populated within the wait
            // window. Nothing was ever captured, so Stop() will not write
            // anything either (HIGH-2).
            _startupCaptureDone = true;
            _host.Log.Warn(
                "InventoryLogger: the owned pack never populated within the "
                + StartupGiveUpPolls + "-second startup wait window; nothing "
                + "was captured this session.");
            return;
        }

        if (!_startupCaptureDone)
        {
            _startupCaptureDone = true;
            RunStartupCapture(items);
            return;
        }

        // HIGH-1: keep the snapshot fresh for the rest of the session, so a
        // later Stop() -- which typically runs after the host has already
        // torn the owned objects down -- dumps what was actually
        // looted/bought/tinkered mid-session, not just the login-time
        // snapshot.
        _lastOwnedSnapshot = items.ToArray();
    }

    private void RunStartupCapture(IReadOnlyList<PluginInventoryItem> items)
    {
        _lastOwnedSnapshot = items.ToArray();

        if (_host.Storage.ReadText(_storageKey) is null)
        {
            _chat.Write("Requesting id information for all armor/weapon inventory. This will take a few minutes...");

            bool anyMissing = false;
            foreach (PluginInventoryItem item in items)
            {
                if (!HasIdData(item) && ObjectClassNeedsIdent(item.ObjectClass, item.Name))
                {
                    _host.Automation.Objects.Identify(item.ObjectId);
                    anyMissing = true;
                }
            }

            if (anyMissing)
            {
                _waitingForIdData = true;
            }
            else
            {
                // Nothing to identify (everything ident-worthy already has
                // id data) -- dump now rather than waiting forever for an
                // IdentReceived that will never come.
                Dump(requestIdsIfMissing: false, items);
            }
        }
        else
        {
            Dump(requestIdsIfMissing: true, items);
        }
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;
        _snapshotPoll?.Dispose();
        _snapshotPoll = null;

        // HIGH-2: skip the dump entirely when nothing was EVER captured
        // this session (an empty character, or Disable()/logoff before the
        // pack ever streamed in / the startup wait gave up) -- writing
        // Export([]) in that case would silently overwrite a perfectly
        // good pre-existing file with an empty document.
        if (_settings.InventoryLogger.Value && _lastOwnedSnapshot.Count > 0)
            Dump(requestIdsIfMissing: false, _lastOwnedSnapshot);

        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;
        _requestedIds.Clear();
        _waitingForIdData = false;
    }

    private void OnObjectChanged(PluginObjectChange change)
    {
        if (!_settings.InventoryLogger.Value)
            return;
        // The original hooks CreateObject and ChangeObject (any change, not
        // only ident). This narrows to Created/IdentReceived because those
        // are the only PluginObjectChangeKind values that can plausibly flip
        // "needs an id request" or "just got its id" for an owned item; an
        // ordinary property Updated never does. If a real gap shows up here
        // (an item whose id-worthiness only becomes knowable on a later
        // Updated), widen this back to include it.
        if (change.Kind != PluginObjectChangeKind.Created && change.Kind != PluginObjectChangeKind.IdentReceived)
            return;

        if (_waitingForIdData)
        {
            IReadOnlyList<PluginInventoryItem> currentItems = _host.Automation.Items.CaptureOwnedItems();
            bool allIdentified = currentItems
                .Where(item => ObjectClassNeedsIdent(item.ObjectClass, item.Name))
                .All(item => HasIdData(item));

            if (allIdentified)
            {
                _waitingForIdData = false;
                Dump(requestIdsIfMissing: false, currentItems);
                _chat.Write("Requesting id information for all armor/weapon inventory completed. Log file written.");
                return;
            }

            // LOW-9 (P10 review): fall through to the per-item identify path
            // below instead of returning here. An item that arrives DURING
            // the wait (e.g. looted mid-startup) was never in the original
            // request loop, and nothing else would ever request its id --
            // the wait would then hang forever, since the completeness check
            // above now also covers this new item. The per-item path below
            // (H7-ordered) requests it exactly like any other newly-seen
            // item.
        }

        // A TryGet miss means the object already left the table between the
        // event firing and this handler running (a Released can be queued
        // right behind a Created in the same delivery batch) -- there is
        // nothing to request an id for, so this silently skips rather than
        // restoring a synthetic default PluginWorldObject.
        if (!_host.Automation.Objects.TryGet(change.ObjectId, out PluginWorldObject wo))
            return;
        if (wo.HasAppraisalData || !ObjectClassNeedsIdent(wo.ObjectClass, wo.Name))
            return;
        // H7: the ownership check must run BEFORE marking the id as
        // requested. An object seen on the ground (not yet in my container)
        // must not get poisoned into _requestedIds -- otherwise once it's
        // picked up, this handler fires again for the same id but
        // _requestedIds.Add already returns false, and its id is never
        // actually requested.
        //
        // MEDIUM-5: an item equipped mid-session has ContainerObjectId ==
        // whatever it was equipped FROM (or 0), never the player -- the
        // wielding entity's id lives in WielderObjectId instead (the same
        // host-shape fact behind defect 9's IsEquippedByMe fix). Checking
        // ContainerObjectId alone silently dropped every mid-session equip.
        uint myId = _host.Automation.Character.ObjectId;
        if (wo.ContainerObjectId != myId && wo.WielderObjectId != myId)
            return;
        if (!_requestedIds.Add(change.ObjectId))
            return;

        _host.Automation.Objects.Identify(change.ObjectId);
    }

    /// <summary>
    /// The host's <see cref="PluginInventoryItem"/> is the ident-worthy owned
    /// snapshot; "has id data" for it is inferred the same way
    /// <see cref="ItemInfo.ItemModel"/> treats a captured properties bag: this
    /// logger asks <see cref="IWorldObjectAutomation.TryGet"/> for the
    /// matching world object's <c>HasAppraisalData</c> flag directly.
    /// </summary>
    private bool HasIdData(PluginInventoryItem item)
        => _host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject wo) && wo.HasAppraisalData;

    private void Dump(bool requestIdsIfMissing, IReadOnlyList<PluginInventoryItem> items)
    {
        // HIGH-1: refresh the last-known-good snapshot whenever this call
        // actually has real items, so a later Stop() dump (which typically
        // runs after the host has already torn the owned objects down)
        // reflects the most recently captured inventory, not whatever was
        // last captured at startup.
        if (items.Count > 0)
            _lastOwnedSnapshot = items as PluginInventoryItem[] ?? items.ToArray();

        List<MyWorldObjectRecord> previous = [];
        if (_host.Storage.ReadText(_storageKey) is { } content)
        {
            if (!InventoryLoggerXml.TryImport(content, out previous))
                _chat.Write("Inventory file is corrupt.");
        }

        var current = new List<MyWorldObjectRecord>();
        foreach (PluginInventoryItem item in items)
        {
            // An owned item the object table has no full PluginWorldObject
            // for yet (not yet resolved/appraised) is still owned and still
            // belongs in the dump -- the original always wrote every owned
            // item, with an empty id block for anything unresolved. See the
            // P9 fix round (docs/live-results.md) for the "empty document"
            // symptom this addresses. A previously-persisted record for this
            // id must survive a thin object-table dump unchanged: look it up
            // in `previous` and Combine rather than overwriting good id data
            // with an empty unresolved stub (HIGH-1).
            //
            // HIGH-A (P9 re-review): do NOT call Identify or record the id in
            // _requestedIds here. On the real host, TryGet's false branch
            // means there is no ClientObject for this id yet either, and
            // Identify requires one -- so an Identify call in THIS branch is
            // a guaranteed no-op. Worse, recording it in _requestedIds would
            // poison OnObjectChanged's `_requestedIds.Add` dedup guard (the
            // same H7 hazard documented at that method's container check
            // below): once poisoned, the item would never actually be
            // identified for the rest of the session, even after its
            // ClientObject shows up and OnObjectChanged fires for it. The
            // real request has to wait for that later, resolved-object path.
            if (!_host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject wo))
            {
                MyWorldObjectRecord unresolved = MyWorldObjectRecord.CreateUnresolved(item);
                MyWorldObjectRecord? previousMatch = previous.FirstOrDefault(
                    prev => prev.Id == unresolved.Id && prev.ObjectClass == unresolved.ObjectClass);

                current.Add(previousMatch is null
                    ? unresolved
                    : MyWorldObjectRecord.Combine(previousMatch, unresolved));
                continue;
            }

            if (!_host.Automation.Objects.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties))
                properties = ItemModel.EmptyProperties;

            MyWorldObjectRecord snapshot = MyWorldObjectRecord.Create(wo, properties);

            MyWorldObjectRecord? matched = previous.FirstOrDefault(
                prev => prev.Id == snapshot.Id && prev.ObjectClass == snapshot.ObjectClass);

            if (matched is null)
            {
                if (requestIdsIfMissing && !snapshot.HasIdData && ObjectClassNeedsIdent(wo.ObjectClass, wo.Name))
                    _host.Automation.Objects.Identify(snapshot.Id);
                current.Add(snapshot);
                continue;
            }

            if (requestIdsIfMissing && !matched.HasIdData && !snapshot.HasIdData
                && ObjectClassNeedsIdent(wo.ObjectClass, wo.Name))
            {
                _host.Automation.Objects.Identify(snapshot.Id);
                current.Add(snapshot);
            }
            else
            {
                current.Add(MyWorldObjectRecord.Combine(matched, snapshot));
            }
        }

        _host.Storage.WriteText(_storageKey, InventoryLoggerXml.Export(current));
    }
}
