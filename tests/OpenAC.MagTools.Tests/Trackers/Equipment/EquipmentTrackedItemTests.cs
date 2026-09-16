using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Equipment;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.Equipment;

public sealed class EquipmentTrackedItemTests
{
    private static PluginInventoryItem MakeItem(
        uint objectId = 1u,
        string name = "Test Item",
        int currentMana = 100,
        int maximumMana = 100,
        uint spellId = 0u)
        => new(
            objectId, 0u, name, 0u, 0u, 0u, 0u, 1u, 0u, 0u, 0u,
            1, 0, 0, spellId, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ItemCurrentMana = currentMana,
            ItemMaximumMana = maximumMana,
        };

    [Fact]
    public void SecondsPerBurn_matches_the_18s_to_20s_example()
    {
        // -1/18 per second is retail's "1 every 18 seconds" burn rate; the
        // 5-second grid rounds that up to 20, per the inventory doc's example.
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(), default, DateTime.UtcNow);
        item.OnIdentReceived(
            Properties(floats: new Dictionary<uint, double> { [5u] = -1d / 18d }),
            DateTime.UtcNow);

        Assert.Equal(20, item.SecondsPerBurn);
    }

    [Fact]
    public void SecondsPerBurn_is_zero_when_ManaRateOfChange_is_absent_or_nonnegative()
    {
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(), default, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        Assert.Equal(0, item.SecondsPerBurn);
    }

    [Fact]
    public void CalculatedCurrentMana_decays_on_the_burn_grid_and_clamps_at_zero()
    {
        var clock = new FakeTimeProvider();
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 3, maximumMana: 10), default, clock.GetUtcNow().UtcDateTime);
        item.OnIdentReceived(
            Properties(floats: new Dictionary<uint, double> { [5u] = -1d / 18d }), // SecondsPerBurn = 20
            clock.GetUtcNow().UtcDateTime);

        Assert.Equal(3, item.CalculatedCurrentMana(clock.GetUtcNow().UtcDateTime, EquipmentTrackedItemState.Active));

        clock.Advance(TimeSpan.FromSeconds(25)); // one burn tick elapsed
        Assert.Equal(2, item.CalculatedCurrentMana(clock.GetUtcNow().UtcDateTime, EquipmentTrackedItemState.Active));

        clock.Advance(TimeSpan.FromSeconds(1000)); // far more than the item holds
        Assert.Equal(0, item.CalculatedCurrentMana(clock.GetUtcNow().UtcDateTime, EquipmentTrackedItemState.Active));
    }

    [Fact]
    public void CalculatedCurrentMana_returns_raw_value_when_not_active()
    {
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 7), default, DateTime.UtcNow);
        item.OnIdentReceived(
            Properties(floats: new Dictionary<uint, double> { [5u] = -1d / 18d }),
            DateTime.UtcNow);

        Assert.Equal(7, item.CalculatedCurrentMana(DateTime.UtcNow, EquipmentTrackedItemState.NotActive));
    }

    [Fact]
    public void ManaTimeRemaining_uses_the_sentinel_when_active_with_no_burn_rate()
    {
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(), default, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        TimeSpan remaining = item.ManaTimeRemaining(DateTime.UtcNow, EquipmentTrackedItemState.Active);
        Assert.Equal(new TimeSpan(99, 99, 0), remaining);
    }

    [Fact]
    public void ManaTimeRemaining_is_zero_when_not_active()
    {
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(), default, DateTime.UtcNow);
        Assert.Equal(TimeSpan.Zero, item.ManaTimeRemaining(DateTime.UtcNow, EquipmentTrackedItemState.NotActive));
    }

    [Fact]
    public void ManaNeededToRefill_is_the_gap_to_maximum()
    {
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 40, maximumMana: 100), default, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        Assert.Equal(60, item.ManaNeededToRefill(DateTime.UtcNow, EquipmentTrackedItemState.NotActive));
    }

    [Fact]
    public void State_is_Unknown_without_id_data()
    {
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(), default, DateTime.UtcNow);

        Assert.Equal(
            EquipmentTrackedItemState.Unknown,
            item.GetState(new FakeSpellCatalog(), []));
    }

    [Fact]
    public void State_is_NotActivatable_with_no_max_mana()
    {
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(maximumMana: 0), default, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        Assert.Equal(
            EquipmentTrackedItemState.NotActivatable,
            item.GetState(new FakeSpellCatalog(), []));
    }

    [Fact]
    public void State_is_NotActive_with_zero_current_mana()
    {
        const uint associatedSpellId = 42u;
        var world = new PluginWorldObject(1u, 0u, "Wand", PluginObjectClass.WandStaffOrb, 0u, 0u, 0u)
        {
            SpellIds = [associatedSpellId],
        };

        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(
            MakeItem(currentMana: 0, maximumMana: 100, spellId: associatedSpellId), world, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        Assert.Equal(
            EquipmentTrackedItemState.NotActive,
            item.GetState(new FakeSpellCatalog(), []));
    }

    [Fact]
    public void State_is_Active_when_the_only_carried_spell_is_the_items_own_associated_spell()
    {
        const uint associatedSpellId = 42u;
        var world = new PluginWorldObject(1u, 0u, "Wand", PluginObjectClass.WandStaffOrb, 0u, 0u, 0u)
        {
            SpellIds = [associatedSpellId],
        };

        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(
            MakeItem(currentMana: 50, maximumMana: 100, spellId: associatedSpellId), world, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        // The item's own associated activation spell is excluded from the
        // gating loop, so with nothing else to gate on the item is Active.
        Assert.Equal(
            EquipmentTrackedItemState.Active,
            item.GetState(new FakeSpellCatalog(), []));
    }

    [Fact]
    public void State_is_NotActive_when_a_gated_beneficial_spell_is_not_running()
    {
        const uint spellId = 55u;
        var world = new PluginWorldObject(1u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 0u)
        {
            SpellIds = [spellId],
            ActiveSpellIds = [], // not currently active on the item
        };
        var spells = new FakeSpellCatalog();
        spells.Register(spellId, "Strength Other", family: 3u, difficulty: 5);

        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 50, maximumMana: 100), world, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        Assert.Equal(
            EquipmentTrackedItemState.NotActive,
            item.GetState(spells, []));
    }

    [Fact]
    public void State_is_Active_when_the_item_itself_carries_a_matching_active_spell()
    {
        const uint spellId = 55u;
        var world = new PluginWorldObject(1u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 0u)
        {
            SpellIds = [spellId],
            ActiveSpellIds = [spellId],
        };
        var spells = new FakeSpellCatalog();
        spells.Register(spellId, "Strength Other", family: 3u, difficulty: 5);

        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 50, maximumMana: 100), world, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        Assert.Equal(
            EquipmentTrackedItemState.Active,
            item.GetState(spells, []));
    }

    [Fact]
    public void State_is_Active_when_the_player_carries_a_matching_item_cast_enchantment()
    {
        const uint spellId = 55u;
        var world = new PluginWorldObject(1u, 0u, "Ring", PluginObjectClass.Jewelry, 0u, 0u, 0u)
        {
            SpellIds = [spellId],
            ActiveSpellIds = [],
        };
        var spells = new FakeSpellCatalog();
        spells.Register(spellId, "Strength Other", family: 3u, difficulty: 5);

        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 50, maximumMana: 100), world, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        // TimeRemaining <= 0 marks an item-cast enchantment, per the original.
        var enchantments = new[] { new PluginActiveEnchantment(spellId, 3u, 5, 0d) };

        Assert.Equal(
            EquipmentTrackedItemState.Active,
            item.GetState(spells, enchantments));
    }

    [Fact]
    public void State_skips_offensive_and_debuff_spells_when_evaluating_activation()
    {
        const uint spellId = 55u;
        var world = new PluginWorldObject(1u, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, 0u, 0u)
        {
            SpellIds = [spellId],
            ActiveSpellIds = [],
        };
        var spells = new FakeSpellCatalog();
        spells.Register(spellId, "Blood Drinker", family: 9u, difficulty: 5, isOffensive: true);

        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 50, maximumMana: 100), world, DateTime.UtcNow);
        item.OnIdentReceived(Properties(), DateTime.UtcNow);

        Assert.Equal(
            EquipmentTrackedItemState.Active,
            item.GetState(spells, []));
    }

    [Fact]
    public void UpdateSnapshot_treats_a_mana_change_as_a_fresh_ident_timestamp()
    {
        var clock = new FakeTimeProvider();
        var item = new EquipmentTrackedItem(1u);
        item.UpdateSnapshot(MakeItem(currentMana: 50), default, clock.GetUtcNow().UtcDateTime);
        item.OnIdentReceived(
            Properties(floats: new Dictionary<uint, double> { [5u] = -1d / 18d }),
            clock.GetUtcNow().UtcDateTime);

        clock.Advance(TimeSpan.FromMinutes(30));
        // A ManaChange updates the timestamp WITHOUT a fresh ident — the
        // original's documented reason LastIdTime alone is wrong.
        item.UpdateSnapshot(MakeItem(currentMana: 45), default, clock.GetUtcNow().UtcDateTime);

        Assert.Equal(clock.GetUtcNow().UtcDateTime, item.LastManaIdentUtc);
    }

    private static PluginItemProperties Properties(
        IReadOnlyDictionary<uint, double>? floats = null,
        IReadOnlyDictionary<uint, bool>? bools = null)
        => new(
            new Dictionary<uint, int>(),
            new Dictionary<uint, long>(),
            bools ?? new Dictionary<uint, bool>(),
            floats ?? new Dictionary<uint, double>(),
            new Dictionary<uint, string>(),
            new Dictionary<uint, uint>(),
            new Dictionary<uint, uint>());
}

/// <summary>A minimal <see cref="ISpellCatalog"/> for activation-state tests.</summary>
internal sealed class FakeSpellCatalog : ISpellCatalog
{
    private readonly Dictionary<uint, PluginSpellInfo> _spells = [];

    public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; } = [];

    public void Register(
        uint spellId, string name, uint family, int difficulty, bool isOffensive = false, bool isDebuff = false)
        => _spells[spellId] = new PluginSpellInfo(
            spellId, name, family, 1, difficulty, 0, 0f, 0u, string.Empty, true, true)
        {
            IsOffensive = isOffensive,
            IsDebuff = isDebuff,
        };

    public bool TryGet(uint spellId, out PluginSpellInfo info) => _spells.TryGetValue(spellId, out info);
}
