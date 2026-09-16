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
    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly InventoryManagementSettings _settings;
    private readonly HashSet<uint> _requestedIds = [];
    private Action<PluginObjectChange>? _onObjectChanged;
    private Action<double>? _onStartupTick;
    private string _storageKey = string.Empty;
    private bool _waitingForIdData;
    private bool _running;
    private bool _startupCapturePending;

    /// <summary>
    /// The last non-empty owned-item snapshot this instance has actually
    /// dumped from. <see cref="Stop"/> dumps from this rather than a fresh
    /// <see cref="IItemAutomation.CaptureOwnedItems"/> call, because by the
    /// time logoff teardown runs the host has typically already released
    /// the owned objects -- a fresh capture there is reliably empty even
    /// though real items were logged earlier in the session. See defect 8
    /// in the live-gate results.
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

    public void Start(string server, string character)
    {
        if (_running)
            return;
        _running = true;
        _requestedIds.Clear();
        _waitingForIdData = false;
        _storageKey = server + "/" + character + ".Inventory.xml";
        _lastOwnedSnapshot = [];

        if (!_settings.InventoryLogger.Value)
        {
            _onObjectChanged = OnObjectChanged;
            _host.Events.ObjectChanged += _onObjectChanged;
            return;
        }

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;

        BeginStartupCapture();
    }

    /// <summary>
    /// <see cref="Start"/> used to decide the fresh-file-vs-existing-file
    /// branch and, in the existing-file branch, dump immediately using
    /// whatever <see cref="IItemAutomation.CaptureOwnedItems"/> returned at
    /// that instant. At <see cref="SessionContext.SessionReady"/> time the
    /// owned pack has not necessarily streamed in yet, so that read a real
    /// host's empty startup window and wrote (or immediately dumped)
    /// nothing -- defect 8. This instead waits for the first
    /// <see cref="IEvents.Tick"/> on which the owned-item count is non-zero
    /// before doing anything the original did synchronously; if the pack is
    /// already populated at <see cref="Start"/> time (the common case --
    /// e.g. a reconnect where the character was already fully in-world),
    /// it runs immediately, exactly like the original.
    /// </summary>
    private void BeginStartupCapture()
    {
        IReadOnlyList<PluginInventoryItem> items = _host.Automation.Items.CaptureOwnedItems();
        if (items.Count > 0)
        {
            RunStartupCapture(items);
            return;
        }

        _startupCapturePending = true;
        _onStartupTick = OnStartupTick;
        _host.Events.Tick += _onStartupTick;
    }

    private void OnStartupTick(double elapsedSeconds)
    {
        IReadOnlyList<PluginInventoryItem> items = _host.Automation.Items.CaptureOwnedItems();
        if (items.Count == 0)
            return;

        StopStartupCapture();
        RunStartupCapture(items);
    }

    private void StopStartupCapture()
    {
        if (!_startupCapturePending)
            return;

        if (_onStartupTick is not null)
            _host.Events.Tick -= _onStartupTick;
        _onStartupTick = null;
        _startupCapturePending = false;
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
        StopStartupCapture();

        // Dump from the last known-good (non-empty) snapshot rather than a
        // fresh CaptureOwnedItems() call: by the time logoff teardown runs
        // the host has typically already released the owned objects, so a
        // fresh capture here is reliably empty even though real items were
        // logged earlier in the session (defect 8).
        if (_settings.InventoryLogger.Value)
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

            if (!allIdentified)
                return;

            _waitingForIdData = false;
            if (currentItems.Count > 0)
                _lastOwnedSnapshot = currentItems.ToArray();
            Dump(requestIdsIfMissing: false, currentItems);
            _chat.Write("Requesting id information for all armor/weapon inventory completed. Log file written.");
            return;
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
        // H7: the container check must run BEFORE marking the id as
        // requested. An object seen on the ground (not yet in my container)
        // must not get poisoned into _requestedIds -- otherwise once it's
        // picked up, this handler fires again for the same id but
        // _requestedIds.Add already returns false, and its id is never
        // actually requested.
        if (wo.ContainerObjectId != _host.Automation.Character.ObjectId)
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
