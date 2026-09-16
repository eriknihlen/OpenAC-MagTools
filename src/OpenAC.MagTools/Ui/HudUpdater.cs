using System.Globalization;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Trackers.Combat;
using OpenAC.MagTools.Trackers.Equipment;
using OpenAC.MagTools.Trackers.Inventory;
using OpenAC.MagTools.Trackers.ProfitLoss;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// Fills <see cref="HudViewModel"/>'s 14 rows once a second, exactly as the
/// original's <c>HUD</c> class refreshed the VirindiHUDs status bar off its
/// own 1000 ms timer. See §4.3 of the port design for every row's formula.
/// </summary>
/// <remarks>
/// "ID Queue" has no host equivalent — the original read
/// <c>CoreManager.Current.IDQueue.ActionCount</c>, a Decal-internal counter
/// this contract does not expose. It is left blank, matching the original's
/// own "empty string means nothing to show" convention. See docs/deviations.md.
/// </remarks>
public sealed class HudUpdater
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IPluginHost _host;
    private readonly HudViewModel _hud;
    private readonly CombatTrackerHost? _combatTrackerHost;
    private readonly EquipmentTrackerHost? _equipmentTrackerHost;
    private readonly InventoryTrackerHost? _inventoryTrackerHost;
    private IDisposable? _registration;

    public HudUpdater(
        IPluginHost host,
        HudViewModel hud,
        CombatTrackerHost? combatTrackerHost,
        EquipmentTrackerHost? equipmentTrackerHost,
        InventoryTrackerHost? inventoryTrackerHost)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(hud);
        _host = host;
        _hud = hud;
        _combatTrackerHost = combatTrackerHost;
        _equipmentTrackerHost = equipmentTrackerHost;
        _inventoryTrackerHost = inventoryTrackerHost;
    }

    public void Start(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        Stop();
        Refresh();
        _registration = scheduler.Every(RefreshInterval, Refresh);
    }

    public void Stop()
    {
        _registration?.Dispose();
        _registration = null;
    }

    public void Refresh()
    {
        _hud.SetValue("Mana", ManaRow());
        _hud.SetValue("Comps Time 1h", CompsTimeRow());
        _hud.SetValue("Net Profit 5m", NetProfitRow(TimeSpan.FromMinutes(5)));
        _hud.SetValue("Net Profit 1h", NetProfitRow(TimeSpan.FromHours(1)));
        _hud.SetValue("DPS Out 1m", N0(_combatTrackerHost?.Current.DpsOut1m));
        _hud.SetValue("DPS Out 5m", N0(_combatTrackerHost?.Current.DpsOut5m));
        _hud.SetValue("DPS Out 1h", N0(_combatTrackerHost?.Current.DpsOut1h));
        _hud.SetValue("DPS In 1m", N0(_combatTrackerHost?.Current.DpsIn1m));
        _hud.SetValue("DPS In 5m", N0(_combatTrackerHost?.Current.DpsIn5m));
        _hud.SetValue("DPS In 1h", N0(_combatTrackerHost?.Current.DpsIn1h));

        // LOW: one CaptureObjects() call feeds both the Players and Monsters
        // rows, instead of two independent full landscape scans.
        (string players, string monsters) = PlayersAndMonstersRows();
        _hud.SetValue("Players", players);
        _hud.SetValue("Monsters", monsters);

        _hud.SetValue("Pack Slots", PackSlotsRow());
        // "ID Queue" — no host equivalent; see class remarks.
        _hud.SetValue("ID Queue", string.Empty);
    }

    private string ManaRow()
    {
        if (_equipmentTrackerHost is null)
            return string.Empty;

        ISpellCatalog spells = _host.Automation.Spells;
        IReadOnlyList<PluginActiveEnchantment> enchantments = _host.Automation.Character.ActiveEnchantments;

        TimeSpan remaining = _equipmentTrackerHost.Tracker.RemainingTimeBeforeNextEmptyItem(spells, enchantments);
        if (remaining == TimeSpan.MaxValue)
            return string.Empty;

        bool anyInactive = _equipmentTrackerHost.Tracker.NumberOfInactiveItems(spells, enchantments) > 0;
        int hours = (int)remaining.TotalHours;
        int minutes = remaining.Minutes;
        string text = hours.ToString(CultureInfo.InvariantCulture) + "h"
            + minutes.ToString("D2", CultureInfo.InvariantCulture) + "m";
        return anyInactive ? "*" + text : text;
    }

    private string CompsTimeRow()
    {
        TrackedConsumable? next = _inventoryTrackerHost?.Consumables.NextItemToBeDepleted(TimeSpan.FromHours(1));
        if (next is null)
            return string.Empty;

        TimeSpan depletion = next.History.GetTimeToDepletion(TimeSpan.FromHours(1));
        if (depletion <= TimeSpan.Zero || depletion == TimeSpan.MaxValue)
            return string.Empty;

        return depletion.TotalHours.ToString("N1", CultureInfo.InvariantCulture) + "h";
    }

    /// <summary>M4: blank, not "0.0/h", when the rate computes to exactly zero.</summary>
    private string NetProfitRow(TimeSpan historyPeriod)
    {
        if (_inventoryTrackerHost is null)
            return string.Empty;

        double perHour = ProfitLossTracker.MmdPerHour(_inventoryTrackerHost.ProfitLoss.NetProfit, historyPeriod);
        return perHour == 0d ? string.Empty : perHour.ToString("N1", CultureInfo.InvariantCulture) + "/h";
    }

    private static string N0(double? value)
        => value is { } v and not 0d ? v.ToString("N0", CultureInfo.InvariantCulture) : string.Empty;

    private (string Players, string Monsters) PlayersAndMonstersRows()
    {
        int playerCount = -1; // subtract the local player below regardless of whether any were found
        int monsterCount = 0;
        foreach (PluginWorldObject candidate in _host.Automation.Objects.CaptureObjects())
        {
            if (!candidate.IsLandscape)
                continue;

            if (candidate.ObjectClass == PluginObjectClass.Player)
                playerCount++;
            else if (candidate.ObjectClass == PluginObjectClass.Monster)
                monsterCount++;
        }

        string players = playerCount > 0 ? playerCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
        string monsters = monsterCount > 0 ? monsterCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return (players, monsters);
    }

    /// <remarks>
    /// LOW: the host's <see cref="ICharacterInfo.MainPackFreeSlots"/> hardcodes
    /// the main pack's capacity at 102 slots (<c>AppAutomationSurface.cs</c>'s
    /// implementation) rather than reading the character's actual, possibly
    /// augmented, live <c>ItemSlots</c> capacity the way the original's
    /// <c>Util.GetFreePackSlots</c> did. Not fixable from this plugin — the
    /// host contract exposes only the final free-slot count, not the pack's
    /// own capacity. See docs/deviations.md.
    /// </remarks>
    private string PackSlotsRow()
    {
        int slots = _host.Automation.Character.MainPackFreeSlots;
        // M4: blank, not "0", when the pack is completely full — matches the
        // original's own "(freePackSlots == 0) ? '' : ..." ternary.
        return slots == 0 ? string.Empty : slots.ToString(CultureInfo.InvariantCulture);
    }
}
