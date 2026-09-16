using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Loggers.Inventory;

/// <summary>
/// Ports <c>Shared/MyWorldObject.cs</c>'s serialised shape (see the port
/// design's §2.7): a snapshot of one owned item's identity plus its raw
/// property dictionaries and spell lists, immune to whatever the live object
/// does next.
/// </summary>
public sealed class MyWorldObjectRecord
{
    public bool HasIdData { get; init; }
    public uint Id { get; init; }
    public int LastIdTime { get; init; }
    public int ObjectClass { get; init; }
    public IReadOnlyDictionary<int, bool> BoolValues { get; init; } = new Dictionary<int, bool>();
    public IReadOnlyDictionary<int, double> DoubleValues { get; init; } = new Dictionary<int, double>();
    public IReadOnlyDictionary<int, int> IntValues { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, string> StringValues { get; init; } = new Dictionary<int, string>();
    public IReadOnlyList<int> ActiveSpells { get; init; } = [];
    public IReadOnlyList<int> Spells { get; init; } = [];

    /// <summary><c>MyWorldObjectCreator.Create</c>.</summary>
    public static MyWorldObjectRecord Create(PluginWorldObject wo, PluginItemProperties properties)
        => new()
        {
            HasIdData = wo.HasAppraisalData,
            Id = wo.ObjectId,
            LastIdTime = wo.LastIdTime,
            ObjectClass = (int)wo.ObjectClass,
            BoolValues = new Dictionary<int, bool>(properties.Bools.Select(kvp => new KeyValuePair<int, bool>((int)kvp.Key, kvp.Value))),
            DoubleValues = new Dictionary<int, double>(properties.Floats.Select(kvp => new KeyValuePair<int, double>((int)kvp.Key, kvp.Value))),
            IntValues = new Dictionary<int, int>(properties.Ints.Select(kvp => new KeyValuePair<int, int>((int)kvp.Key, kvp.Value))),
            StringValues = new Dictionary<int, string>(properties.Strings.Select(kvp => new KeyValuePair<int, string>((int)kvp.Key, kvp.Value))),
            ActiveSpells = wo.ActiveSpellIds.Select(id => (int)id).ToList(),
            Spells = wo.SpellIds.Select(id => (int)id).ToList(),
        };

    /// <summary>
    /// <c>MyWorldObjectCreator.Combine</c>, ported bug-for-bug:
    /// <c>older.AddTo(newer's dictionaries)</c> unconditionally overwrites
    /// <c>older</c>'s dictionaries with every key <c>newer</c> has (its
    /// "already contains the key" guard is tautological — it always checks
    /// the dict it's iterating, not the target), keeping any key ONLY
    /// present in <c>older</c>. Identity fields (Id/LastIdTime/ObjectClass)
    /// and the spell lists stay <c>older</c>'s own — <c>AddTo</c> never
    /// touches them, so a combined record keeps stale carried/active spell
    /// ids from whenever it was first identified.
    /// </summary>
    public static MyWorldObjectRecord Combine(MyWorldObjectRecord older, MyWorldObjectRecord newer)
    {
        if (!older.HasIdData || newer.HasIdData)
            return newer;

        var bools = new Dictionary<int, bool>(older.BoolValues);
        foreach (KeyValuePair<int, bool> kvp in newer.BoolValues)
            bools[kvp.Key] = kvp.Value;

        var doubles = new Dictionary<int, double>(older.DoubleValues);
        foreach (KeyValuePair<int, double> kvp in newer.DoubleValues)
            doubles[kvp.Key] = kvp.Value;

        var ints = new Dictionary<int, int>(older.IntValues);
        foreach (KeyValuePair<int, int> kvp in newer.IntValues)
            ints[kvp.Key] = kvp.Value;

        var strings = new Dictionary<int, string>(older.StringValues);
        foreach (KeyValuePair<int, string> kvp in newer.StringValues)
            strings[kvp.Key] = kvp.Value;

        return new MyWorldObjectRecord
        {
            HasIdData = older.HasIdData,
            Id = older.Id,
            LastIdTime = older.LastIdTime,
            ObjectClass = older.ObjectClass,
            BoolValues = bools,
            DoubleValues = doubles,
            IntValues = ints,
            StringValues = strings,
            ActiveSpells = older.ActiveSpells,
            Spells = older.Spells,
        };
    }
}
