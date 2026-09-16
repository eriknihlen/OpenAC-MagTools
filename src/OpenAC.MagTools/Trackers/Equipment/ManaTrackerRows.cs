using System.Globalization;
using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.Equipment;

/// <summary>
/// Builds the Mana list's rows from an <see cref="EquipmentTracker"/>, per
/// §4.1/§4.3 of the port design: excludes Aetheria and cloaks from display
/// (they stay tracked, just not listed), assigns the four state icons, and
/// bubble-sorts ascending by remaining time — matching the original's
/// <c>ManaTrackerGUI</c>.
/// </summary>
public static class ManaTrackerRows
{
    /// <summary>Decal icon ids, verbatim from the original.</summary>
    public const uint UnknownIcon = 0x60020B5u;
    public const uint ActiveIcon = 0x60011F9u; // green
    public const uint NotActiveIcon = 0x60011F8u; // red
    public const uint NoneIcon = 0x600287Au;

    public readonly record struct Row(
        uint ObjectId,
        uint ItemIcon,
        string Name,
        uint StateIcon,
        string ManaText,
        string TimeText,
        double SortSeconds);

    public static IReadOnlyList<Row> Build(
        EquipmentTracker tracker,
        ISpellCatalog spells,
        IReadOnlyList<PluginActiveEnchantment> playerEnchantments,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(playerEnchantments);
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = new List<Row>();

        foreach (EquipmentTrackedItem item in tracker.Items)
        {
            if (EquipmentTracker.IsHiddenFromList(item))
                continue;

            EquipmentTrackedItemState state = item.GetState(spells, playerEnchantments);
            uint stateIcon = state switch
            {
                EquipmentTrackedItemState.Unknown => UnknownIcon,
                EquipmentTrackedItemState.NotActivatable => NoneIcon,
                EquipmentTrackedItemState.Active => ActiveIcon,
                EquipmentTrackedItemState.NotActive => NotActiveIcon,
                _ => NoneIcon,
            };

            int calculatedMana = item.CalculatedCurrentMana(now, state);
            string manaText = calculatedMana.ToString(CultureInfo.InvariantCulture)
                + " / " + item.Item.ItemMaximumMana.ToString(CultureInfo.InvariantCulture);

            TimeSpan remaining = item.ManaTimeRemaining(now, state);
            string timeText = FormatTime(remaining);

            // The original's hidden sort cell: ManaTimeRemaining.TotalSeconds
            // for Active/NotActive items, int.MaxValue otherwise (Unknown/
            // NotActivatable sink to the bottom).
            double sortSeconds = state is EquipmentTrackedItemState.Active
                or EquipmentTrackedItemState.NotActive
                ? remaining.TotalSeconds
                : int.MaxValue;

            rows.Add(new Row(
                item.ObjectId, item.Item.IconId, item.Item.Name, stateIcon,
                manaText, timeText, sortSeconds));
        }

        rows.Sort(static (a, b) => a.SortSeconds.CompareTo(b.SortSeconds));
        return rows;
    }

    private static string FormatTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero || remaining == TimeSpan.MaxValue)
            return string.Empty;

        int totalHours = (int)remaining.TotalHours;
        int minutes = remaining.Minutes;
        return totalHours.ToString(CultureInfo.InvariantCulture) + "h"
            + minutes.ToString("D2", CultureInfo.InvariantCulture) + "m";
    }
}
