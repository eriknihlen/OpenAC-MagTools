using OpenAC.MagTools.Loggers.Inventory;

namespace OpenAC.MagTools.Tests.Loggers.Inventory;

public sealed class MyWorldObjectRecordTests
{
    private static MyWorldObjectRecord Make(
        bool hasIdData,
        Dictionary<int, int>? ints = null,
        IReadOnlyList<int>? spells = null)
        => new()
        {
            HasIdData = hasIdData,
            Id = 1u,
            LastIdTime = 1000,
            ObjectClass = 1,
            IntValues = ints ?? new Dictionary<int, int>(),
            Spells = spells ?? [],
        };

    [Fact]
    public void NewerAlreadyIdentifiedReplacesOlderOutright()
    {
        MyWorldObjectRecord older = Make(hasIdData: true, ints: new Dictionary<int, int> { { 1, 111 } });
        MyWorldObjectRecord newer = Make(hasIdData: true, ints: new Dictionary<int, int> { { 2, 222 } });

        MyWorldObjectRecord combined = MyWorldObjectRecord.Combine(older, newer);

        Assert.Same(newer, combined);
    }

    [Fact]
    public void OlderWithoutIdDataIsReplacedByNewer()
    {
        MyWorldObjectRecord older = Make(hasIdData: false);
        MyWorldObjectRecord newer = Make(hasIdData: false, ints: new Dictionary<int, int> { { 2, 222 } });

        MyWorldObjectRecord combined = MyWorldObjectRecord.Combine(older, newer);

        Assert.Same(newer, combined);
    }

    [Fact]
    public void OlderIdentifiedAndNewerNotKeepsOlderIdentityAndMergesFields()
    {
        MyWorldObjectRecord older = Make(hasIdData: true, ints: new Dictionary<int, int> { { 1, 100 }, { 3, 300 } }, spells: [55]);
        MyWorldObjectRecord newer = Make(hasIdData: false, ints: new Dictionary<int, int> { { 1, 999 } }, spells: [77]);

        MyWorldObjectRecord combined = MyWorldObjectRecord.Combine(older, newer);

        Assert.True(combined.HasIdData);
        // newer's value for a shared key wins...
        Assert.Equal(999, combined.IntValues[1]);
        // ...but a key only older had survives.
        Assert.Equal(300, combined.IntValues[3]);
        // Spell lists are never touched by the merge -- stale, from older.
        Assert.Equal([55], combined.Spells);
    }
}
