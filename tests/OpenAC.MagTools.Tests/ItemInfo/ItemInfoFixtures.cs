using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;

namespace OpenAC.MagTools.Tests.ItemInfo;

/// <summary>Small builders so item-info tests don't hand-roll the wide host records.</summary>
internal static class ItemInfoFixtures
{
    public static PluginWorldObject Wo(
        uint id,
        string name,
        PluginObjectClass objectClass,
        IReadOnlyList<uint>? spellIds = null,
        IReadOnlyList<uint>? activeSpellIds = null,
        uint iconId = 0u)
        => new(id, 0u, name, objectClass, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
            SpellIds = spellIds ?? [],
            ActiveSpellIds = activeSpellIds ?? [],
            IconId = iconId,
        };

    public static PluginInventoryItem Inv(
        uint id,
        string name,
        PluginObjectClass objectClass,
        int damage = 0,
        double variance = 0d,
        int weaponSkill = 0,
        float workmanship = 0f,
        uint containerId = 0u,
        uint equippedLocation = 0u,
        int containerSlot = -1)
        => new(
            id, 0u, name, 0u, containerId, 0u, 0u, equippedLocation, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, weaponSkill, 0, damage, variance, 0, 0, 0)
        {
            ObjectClass = objectClass,
            Workmanship = workmanship,
            ContainerSlot = containerSlot,
        };

    public static PluginItemProperties Props(
        Dictionary<uint, int>? ints = null,
        Dictionary<uint, double>? floats = null,
        Dictionary<uint, bool>? bools = null,
        PluginWeaponProfile? weaponProfile = null,
        PluginArmorProfile? armorProfile = null)
        => new(
            ints ?? [],
            new Dictionary<uint, long>(),
            bools ?? [],
            floats ?? [],
            new Dictionary<uint, string>(),
            new Dictionary<uint, uint>(),
            new Dictionary<uint, uint>())
        {
            WeaponProfile = weaponProfile,
            ArmorProfile = armorProfile,
        };

    public static ItemModel Model(
        PluginWorldObject wo,
        Dictionary<uint, int>? ints = null,
        Dictionary<uint, double>? floats = null,
        Dictionary<uint, bool>? bools = null,
        PluginInventoryItem? inv = null,
        PluginWeaponProfile? weaponProfile = null,
        PluginArmorProfile? armorProfile = null)
        => new(wo, Props(ints, floats, bools, weaponProfile, armorProfile), inv);
}

/// <summary>A settable <see cref="ISettings"/> for formatter tests.</summary>
internal sealed class FakeItemInfoSettings : ISettings
{
    public bool ShowBuffedValues { get; set; } = true;
    public bool ShowValueAndBurden { get; set; }
}

/// <summary>A settable <see cref="ISpellCatalog"/> keyed by spell id for formatter tests.</summary>
internal sealed class FakeFormatterSpellCatalog : ISpellCatalog
{
    private readonly Dictionary<uint, PluginSpellInfo> _spells = [];

    public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; } = [];

    public void Add(uint id, string name, uint family = 0u, int difficulty = 0)
        => _spells[id] = new PluginSpellInfo(
            id, name, family, 1, difficulty, 0, 0f, 0u, string.Empty, true, true);

    public bool TryGet(uint spellId, out PluginSpellInfo info) => _spells.TryGetValue(spellId, out info);
}
