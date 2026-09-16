using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Trackers.Equipment;

/// <summary>Port of the original's <c>EquipmentTrackedItemState</c>.</summary>
public enum EquipmentTrackedItemState
{
    Unknown,
    NotActivatable,
    Active,
    NotActive,
}

/// <summary>
/// One equipped item's mana/burn bookkeeping, ported from the original's
/// <c>EquipmentTrackedItem</c>. Every derived number
/// (<see cref="CalculatedCurrentMana"/>, <see cref="ManaTimeRemaining"/>,
/// <see cref="State"/>, …) is computed on demand from the raw stored state
/// rather than cached and invalidated, which sidesteps a whole class of
/// "forgot to recompute" bugs the original's field-caching approach was
/// exposed to — see docs/deviations.md.
/// </summary>
public sealed class EquipmentTrackedItem
{
    public EquipmentTrackedItem(uint objectId)
    {
        ObjectId = objectId;
    }

    public uint ObjectId { get; }

    /// <summary>The most recent owned-item snapshot for this object (mana/value fields).</summary>
    public PluginInventoryItem Item { get; private set; }

    /// <summary>
    /// The matching <see cref="PluginWorldObject"/> snapshot, which is where
    /// the host puts <c>SpellIds</c>/<c>ActiveSpellIds</c> (a
    /// <see cref="PluginInventoryItem"/> only carries
    /// <see cref="PluginInventoryItem.AppraisedSpellIds"/>, the appraisal-panel
    /// list, not the split "carried vs currently active" pair the original's
    /// activation-state math needs) — see docs/deviations.md.
    /// </summary>
    public PluginWorldObject World { get; private set; }

    public bool HasIdData { get; private set; }

    /// <summary>Raw ACE <c>ManaRate</c> float (property id 5), usually negative.</summary>
    public double? ManaRateOfChange { get; private set; }

    /// <summary>ACE <c>Retained</c> bool (property id 91).</summary>
    public bool? Retained { get; private set; }

    /// <summary>
    /// The original's <c>timeOfLastManaIdent</c> — updated on BOTH an ident
    /// completing and a bare mana-value change (see <see cref="UpdateSnapshot"/>),
    /// because a mana change alone does not re-run appraisal.
    /// </summary>
    public DateTime? LastManaIdentUtc { get; private set; }

    /// <summary>
    /// Applies a fresh owned-item snapshot (from a <c>CreateObject</c>/
    /// <c>ChangeObject</c>-equivalent resync). If the item's current mana
    /// differs from what we last saw, this counts as a "ManaChange" for
    /// <see cref="LastManaIdentUtc"/> purposes, exactly like the original.
    /// </summary>
    public void UpdateSnapshot(PluginInventoryItem item, PluginWorldObject world, DateTime nowUtc)
    {
        bool firstSnapshot = !_hasSnapshot;
        if (!firstSnapshot && item.ItemCurrentMana != Item.ItemCurrentMana)
            LastManaIdentUtc = nowUtc;

        Item = item;
        World = world;
        _hasSnapshot = true;
    }

    private bool _hasSnapshot;

    /// <summary>Applies fresh appraisal data (an <c>IdentReceived</c> event).</summary>
    public void OnIdentReceived(PluginItemProperties properties, DateTime nowUtc)
    {
        HasIdData = true;
        ManaRateOfChange = properties.Floats.TryGetValue(
            (uint)ItemInfo.ItemModel.ManaRateOfChangeKey, out double rate)
            ? rate
            : null;
        Retained = properties.Bools.TryGetValue(
            (uint)ItemInfo.ItemModel.RetainedKey, out bool retained)
            ? retained
            : null;
        LastManaIdentUtc = nowUtc;
    }

    /// <summary>
    /// <c>ceil(-0.2 / ManaRateOfChange) * 5</c> — items burn mana on a
    /// 5-second grid. Zero when there is no (negative) burn rate. LOW
    /// deviation: a POSITIVE rate (a recharging item) also returns 0 here;
    /// the original ran the same formula unconditionally for any non-zero
    /// rate, which for a positive rate produces a negative, nonsensical
    /// "seconds per burn". See docs/deviations.md.
    /// </summary>
    public int SecondsPerBurn
    {
        get
        {
            if (ManaRateOfChange is not { } rate || rate >= 0d)
                return 0;
            return (int)Math.Ceiling(-0.2d / rate) * 5;
        }
    }

    private double SecondsSinceLastManaIdent(DateTime nowUtc)
        => LastManaIdentUtc is { } last ? Math.Max(0d, (nowUtc - last).TotalSeconds) : 0d;

    /// <summary>
    /// The activation state, computed per the original's rules: no id data →
    /// Unknown; no spells or no max mana → NotActivatable; zero current mana →
    /// NotActive; otherwise every non-offensive, non-debuff spell the item
    /// carries (other than its own associated activation spell) must be
    /// matched by an active-on-the-item or player-carried enchantment of the
    /// same spell family at an equal-or-higher difficulty, or the item counts
    /// as NotActive.
    /// </summary>
    public EquipmentTrackedItemState GetState(
        ISpellCatalog spells, IReadOnlyList<PluginActiveEnchantment> playerEnchantments)
    {
        if (!HasIdData)
            return EquipmentTrackedItemState.Unknown;

        // The original's "SpellCount == 0 || MaximumMana == 0" gate. The
        // host does not expose a separate "spell count" distinct from the
        // carried-spell id list used below, so an item with no carried
        // spells at all reads the same way a zero spell count would.
        // See docs/deviations.md.
        if (SpellIds.Count == 0 || Item.ItemMaximumMana == 0)
            return EquipmentTrackedItemState.NotActivatable;

        if (Item.ItemCurrentMana == 0)
            return EquipmentTrackedItemState.NotActive;

        foreach (uint spellId in SpellIds)
        {
            if (spellId == Item.SpellId)
                continue; // the item's own associated activation spell

            if (!spells.TryGet(spellId, out PluginSpellInfo info))
                continue; // can't evaluate an unknown spell; don't fail the item over it

            if (info.IsDebuff || info.IsOffensive)
                continue; // cast-on-strike spells don't gate the "is it running" state

            bool satisfied = false;
            foreach (uint activeSpellId in ActiveSpellIds)
            {
                if (spells.TryGet(activeSpellId, out PluginSpellInfo activeInfo)
                    && activeInfo.Family == info.Family
                    && activeInfo.Difficulty >= info.Difficulty)
                {
                    satisfied = true;
                    break;
                }
            }

            if (!satisfied)
            {
                // Player enchantments that are item-cast (duration-less/expired
                // the instant they land) rather than a timed buff — the
                // original's "TimeRemaining <= 0" filter, applied inline
                // instead of pre-building a filtered list per call (M6: keeps
                // GetState allocation-free — see docs/deviations.md).
                foreach (PluginActiveEnchantment enchantment in playerEnchantments)
                {
                    if (enchantment.SecondsRemaining > 0d)
                        continue;
                    if (enchantment.Family != info.Family)
                        continue;
                    if (spells.TryGet(enchantment.SpellId, out PluginSpellInfo enchantInfo)
                        && enchantInfo.Difficulty >= info.Difficulty)
                    {
                        satisfied = true;
                        break;
                    }
                }
            }

            if (!satisfied)
                return EquipmentTrackedItemState.NotActive;
        }

        return EquipmentTrackedItemState.Active;
    }

    /// <summary>
    /// The item's carried spells (the appraisal-panel "Item is imbued with"
    /// list). Guarded against null: a default <see cref="PluginWorldObject"/>
    /// (no matching world snapshot resolved yet) has null collection fields,
    /// since struct field initializers do not run for <c>default</c>.
    /// </summary>
    private IReadOnlyList<uint> SpellIds => World.SpellIds ?? [];

    private IReadOnlyList<uint> ActiveSpellIds => World.ActiveSpellIds ?? [];

    public int CalculatedCurrentMana(DateTime nowUtc, EquipmentTrackedItemState state)
    {
        // The original's own `ItemState == Unknown || ItemState == NotActivatable`
        // guard returns 0 for those two states — no id data (or nothing to
        // read from) means there is nothing to report, not the item's raw
        // (possibly stale/zero) current-mana field. NotActive falls through
        // to the raw-value branch below, matching the original (NotActive
        // always implies CurrentMana == 0 anyway, by definition of the state).
        if (state is EquipmentTrackedItemState.Unknown or EquipmentTrackedItemState.NotActivatable)
            return 0;

        int secondsPerBurn = SecondsPerBurn;
        if (state != EquipmentTrackedItemState.Active || secondsPerBurn <= 0)
            return Item.ItemCurrentMana;

        int burned = (int)Math.Floor(SecondsSinceLastManaIdent(nowUtc) / secondsPerBurn);
        int calculated = Item.ItemCurrentMana - burned;
        return Math.Clamp(calculated, 0, Item.ItemCurrentMana);
    }

    public int ManaNeededToRefill(DateTime nowUtc, EquipmentTrackedItemState state)
        => Math.Max(Item.ItemMaximumMana - CalculatedCurrentMana(nowUtc, state), 0);

    /// <summary>
    /// <c>TimeSpan(99, 99, 0)</c> when Active with no burn rate (the original's
    /// "effectively forever" sentinel — constructing it exactly the way the
    /// original did lets <see cref="TimeSpan"/> normalize the out-of-range
    /// minutes field itself), zero otherwise, and the calculated burn-down
    /// time when Active with a real burn rate.
    /// </summary>
    public TimeSpan ManaTimeRemaining(DateTime nowUtc, EquipmentTrackedItemState state)
    {
        if (state != EquipmentTrackedItemState.Active)
            return TimeSpan.Zero;

        int secondsPerBurn = SecondsPerBurn;
        if (secondsPerBurn <= 0)
            return new TimeSpan(99, 99, 0);

        return TimeSpan.FromSeconds(CalculatedCurrentMana(nowUtc, state) * (double)secondsPerBurn);
    }
}
