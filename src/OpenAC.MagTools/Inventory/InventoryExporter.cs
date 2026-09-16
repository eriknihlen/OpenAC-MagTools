using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Inventory;

/// <summary>
/// Ports <c>Inventory/InventoryExporter.cs</c>: the two Tools → Inventory
/// clipboard buttons. Requests ids for anything ident-worthy that lacks them,
/// waits, sorts with <see cref="WorldObjectSorter"/>, and writes one item-info
/// line per item to the clipboard.
/// </summary>
[Flags]
public enum ExportGroups
{
    WornEquipment = 0x1,
    Inventory = 0x2,
}

public sealed class InventoryExporter
{
    private static readonly TimeSpan ThinkInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How often an item still lacking id data after the initial request
    /// gets re-requested, and how many times that happens before giving up
    /// and exporting without it. Defect 10 (live-gate round 3): the original
    /// request-once-then-wait-forever loop hung "Clipboard Inventory Info"
    /// permanently on a single never-appraised item ("Copying..." for 130+
    /// seconds with no completion). No original source for this class'
    /// exact retry behaviour is available in this port's references, so
    /// this bounded retry-then-give-up cadence is this port's own addition
    /// -- see the deviations doc row for this class.
    /// </summary>
    private static readonly TimeSpan IdRetryInterval = TimeSpan.FromSeconds(5);

    private const int MaxIdRetries = 3;

    private static readonly PluginObjectClass[] IdentWorthyClasses =
    [
        PluginObjectClass.Armor,
        PluginObjectClass.Clothing,
        PluginObjectClass.MeleeWeapon,
        PluginObjectClass.MissileWeapon,
        PluginObjectClass.WandStaffOrb,
        PluginObjectClass.Jewelry,
    ];

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly ItemInfoOnIdentSettings _settings;
    private readonly ISpellCatalog _spells;
    private readonly TickScheduler _scheduler;

    private IDisposable? _thinkLoop;
    private ExportGroups _exportGroups;
    private bool _idsRequested;
    private double _idsRequestedAtSeconds;
    private int _idRetryCount;

    public InventoryExporter(
        IPluginHost host,
        ChatOutput chat,
        ItemInfoOnIdentSettings settings,
        ISpellCatalog spells,
        TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(scheduler);
        _host = host;
        _chat = chat;
        _settings = settings;
        _spells = spells;
        _scheduler = scheduler;
    }

    public bool IsRunning => _thinkLoop is not null;

    public void ExportToClipboard(ExportGroups groups)
    {
        if (IsRunning)
            return;

        _exportGroups = groups;
        _idsRequested = false;
        _idsRequestedAtSeconds = 0d;
        _idRetryCount = 0;

        _chat.Write("Copying all inventory item info to clipboard...");

        _thinkLoop = _scheduler.Every(ThinkInterval, Think);
    }

    /// <summary>
    /// Stops a running export without printing the completion message —
    /// for a caller tearing this instance down mid-export (e.g. the plugin
    /// disabling) rather than the export finishing on its own. A no-op when
    /// nothing is running.
    /// </summary>
    public void Cancel() => StopThinking(report: null);

    /// <summary>
    /// Ends a completed export and reports the clipboard write's actual
    /// result. <paramref name="clipboardSet"/> is <see langword="null"/> for
    /// <see cref="Cancel"/> (no report at all); otherwise it is
    /// <see cref="IPluginClipboard.TrySetText"/>'s return value, so a clipboard
    /// that refused the write (unavailable, another process holding it, …)
    /// prints a failure message this port ADDS rather than the original's
    /// unconditional success line -- the original had no failure path here
    /// at all (see the deviations-doc row for this class).
    /// </summary>
    private void Stop(bool clipboardSet) => StopThinking(clipboardSet);

    private void StopThinking(bool? report)
    {
        if (_thinkLoop is null)
            return;

        _thinkLoop.Dispose();
        _thinkLoop = null;

        switch (report)
        {
            case true:
                _chat.Write("All inventory item info has been copied to the clipboard.");
                break;
            case false:
                const string message = "Clipboard is unavailable; nothing was copied.";
                _chat.Write(message);
                _host.Log.Warn(message);
                break;
            case null:
                break;
        }
    }

    private void Think()
    {
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();
        List<PluginInventoryItem> selected = [];

        if ((_exportGroups & ExportGroups.Inventory) != 0)
        {
            foreach (PluginInventoryItem item in owned)
            {
                if (IsEquippedByMe(item))
                    continue;
                selected.Add(item);
            }
        }

        if ((_exportGroups & ExportGroups.WornEquipment) != 0)
        {
            foreach (PluginInventoryItem item in owned)
            {
                if (!IsEquippedByMe(item))
                    continue;
                selected.Add(item);
            }
        }

        IWorldObjectAutomation objects = _host.Automation.Objects;
        var pending = new List<PluginInventoryItem>();
        foreach (PluginInventoryItem item in selected)
        {
            if (!NeedsIdent(item.ObjectClass))
                continue;
            if (objects.TryGet(item.ObjectId, out PluginWorldObject worldObject) && worldObject.HasAppraisalData)
                continue;
            pending.Add(item);
        }

        if (!_idsRequested)
        {
            foreach (PluginInventoryItem item in pending)
                objects.Identify(item.ObjectId);

            _idsRequested = true;
            _idsRequestedAtSeconds = _scheduler.ElapsedSeconds;
            _idRetryCount = 0;
            return;
        }

        if (pending.Count == 0)
        {
            bool clipboardSet = ExportObjects(selected);
            Stop(clipboardSet);
            return;
        }

        // Defect 10: still waiting on id data for at least one item. Retry
        // on a bounded cadence rather than waiting forever.
        if (_scheduler.ElapsedSeconds - _idsRequestedAtSeconds < IdRetryInterval.TotalSeconds)
            return;

        _idRetryCount++;
        if (_idRetryCount > MaxIdRetries)
        {
            bool clipboardSet = ExportObjects(selected);
            // LOW-8: only report the missing-id count when something was
            // actually copied -- a failed clipboard write already reports
            // "Clipboard is unavailable; nothing was copied." via Stop(),
            // and pairing that with "N items were exported without it" is
            // a contradiction (nothing was exported at all).
            if (clipboardSet)
            {
                _chat.Write(
                    pending.Count + " item(s) never received identification data and were exported without it.");
            }

            Stop(clipboardSet);
            return;
        }

        foreach (PluginInventoryItem item in pending)
            objects.Identify(item.ObjectId);
        _idsRequestedAtSeconds = _scheduler.ElapsedSeconds;
    }

    private static bool NeedsIdent(PluginObjectClass objectClass)
        => Array.IndexOf(IdentWorthyClasses, objectClass) >= 0;

    /// <summary>
    /// Ports <c>ItemIsEquippedByMe</c> (<c>EquippedSlots &gt; 0</c>, and for
    /// weapons the original's Decal-era <c>Slot == -1</c> additionally
    /// required the item's container to be the player) against this host's
    /// actual field shape rather than the original's assumption -- see
    /// <c>PluginInventoryItem</c> in
    /// <c>AcDream.Plugin.Abstractions/ItemAutomation.cs</c> and how
    /// <c>AcDream.App/Plugins/AppAutomationSurface.cs</c> populates it for
    /// equipped items. On this host <see cref="PluginInventoryItem.ContainerSlot"/>
    /// is -1 for EVERY equipped item, not only weapons, and
    /// <see cref="PluginInventoryItem.ContainerObjectId"/> is left as whatever
    /// container the item was equipped FROM (or 0) -- never the player. The
    /// wielding entity's id lives in <see cref="PluginInventoryItem.WielderObjectId"/>
    /// instead. Defect 9: the original's container-based check rejected every
    /// equipped item on this host, so "Clipboard Worn Equipment" always wrote
    /// an empty string. See the deviations doc for this row.
    /// </summary>
    private bool IsEquippedByMe(PluginInventoryItem item)
    {
        if (!item.IsEquipped)
            return false;

        return item.WielderObjectId == _host.Automation.Character.ObjectId;
    }

    private bool ExportObjects(List<PluginInventoryItem> items)
    {
        items.Sort(new WorldObjectSorter(_host.Automation.Character.ObjectId, items));

        var output = new System.Text.StringBuilder();
        IWorldObjectAutomation objects = _host.Automation.Objects;

        foreach (PluginInventoryItem inventoryItem in items)
        {
            if (!objects.TryGet(inventoryItem.ObjectId, out PluginWorldObject worldObject))
                continue;

            if (!objects.TryCaptureProperties(inventoryItem.ObjectId, out PluginItemProperties properties))
                properties = ItemModel.EmptyProperties;

            var model = new ItemModel(worldObject, properties, inventoryItem);
            output.AppendLine(ItemInfoFormatter.Format(model, _settings, _spells));
        }

        return _host.Clipboard.TrySetText(output.ToString());
    }
}
