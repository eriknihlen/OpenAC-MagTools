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

        bool waitingForIdData = false;
        IWorldObjectAutomation objects = _host.Automation.Objects;

        foreach (PluginInventoryItem item in selected)
        {
            if (!NeedsIdent(item.ObjectClass))
                continue;
            if (objects.TryGet(item.ObjectId, out PluginWorldObject worldObject) && worldObject.HasAppraisalData)
                continue;

            if (!_idsRequested)
                objects.Identify(item.ObjectId);
            else
                waitingForIdData = true;
        }

        if (!_idsRequested)
        {
            _idsRequested = true;
            return;
        }

        if (!waitingForIdData)
        {
            bool clipboardSet = ExportObjects(selected);
            Stop(clipboardSet);
        }
    }

    private static bool NeedsIdent(PluginObjectClass objectClass)
        => Array.IndexOf(IdentWorthyClasses, objectClass) >= 0;

    /// <summary>
    /// <c>ItemIsEquippedByMe</c>, verbatim: <c>EquippedSlots &gt; 0</c>, and for
    /// weapons (<c>Slot == -1</c>) additionally requires the item's container
    /// to be the player.
    /// </summary>
    private bool IsEquippedByMe(PluginInventoryItem item)
    {
        if (!item.IsEquipped)
            return false;

        if (item.ContainerSlot == -1)
            return item.ContainerObjectId == _host.Automation.Character.ObjectId;

        return true;
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
