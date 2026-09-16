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
    private string _storageKey = string.Empty;
    private bool _waitingForIdData;
    private bool _running;

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

        if (!_settings.InventoryLogger.Value)
        {
            _onObjectChanged = OnObjectChanged;
            _host.Events.ObjectChanged += _onObjectChanged;
            return;
        }

        if (_host.Storage.ReadText(_storageKey) is null)
        {
            _chat.Write("Requesting id information for all armor/weapon inventory. This will take a few minutes...");

            foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
            {
                if (!HasIdData(item) && ObjectClassNeedsIdent(item.ObjectClass, item.Name))
                    _host.Automation.Objects.Identify(item.ObjectId);
            }

            _waitingForIdData = true;
        }
        else
        {
            Dump(requestIdsIfMissing: true);
        }

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;

        if (_settings.InventoryLogger.Value)
            Dump(requestIdsIfMissing: false);

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
            bool allIdentified = _host.Automation.Items.CaptureOwnedItems()
                .Where(item => ObjectClassNeedsIdent(item.ObjectClass, item.Name))
                .All(item => HasIdData(item));

            if (!allIdentified)
                return;

            _waitingForIdData = false;
            Dump(requestIdsIfMissing: false);
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

    private void Dump(bool requestIdsIfMissing)
    {
        List<MyWorldObjectRecord> previous = [];
        if (_host.Storage.ReadText(_storageKey) is { } content)
        {
            if (!InventoryLoggerXml.TryImport(content, out previous))
                _chat.Write("Inventory file is corrupt.");
        }

        var current = new List<MyWorldObjectRecord>();
        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            // An owned item the object table has no full PluginWorldObject
            // for yet (not yet resolved/appraised) is still owned and still
            // belongs in the dump -- the original always wrote every owned
            // item, with an empty id block for anything unresolved. See
            // defect #3/#5 in docs/live-results.md: this `continue` used to
            // drop it outright, so a fresh dump before ident data arrived
            // came out as an effectively-empty document.
            if (!_host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject wo))
            {
                if (requestIdsIfMissing && ObjectClassNeedsIdent(item.ObjectClass, item.Name))
                    _host.Automation.Objects.Identify(item.ObjectId);
                current.Add(MyWorldObjectRecord.CreateUnresolved(item));
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
