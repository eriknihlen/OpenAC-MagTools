using OpenAC.MagTools.Loggers.Inventory;

namespace OpenAC.MagTools.Tests.Loggers.Inventory;

public sealed class InventoryLoggerXmlTests
{
    [Fact]
    public void ExportThenImportRoundTripsEveryField()
    {
        var item = new MyWorldObjectRecord
        {
            HasIdData = true,
            Id = 42u,
            LastIdTime = 12345,
            ObjectClass = 1,
            BoolValues = new Dictionary<int, bool> { { 91, true } },
            DoubleValues = new Dictionary<int, double> { { 22, 0.25 } },
            IntValues = new Dictionary<int, int> { { 44, 60 }, { 1, 1 } },
            StringValues = new Dictionary<int, string> { { 1, "War Mace" } },
            ActiveSpells = [10, 20],
            Spells = [30, 40],
        };

        string xml = InventoryLoggerXml.Export([item]);
        bool ok = InventoryLoggerXml.TryImport(xml, out List<MyWorldObjectRecord> imported);

        Assert.True(ok);
        MyWorldObjectRecord round = Assert.Single(imported);
        Assert.Equal(item.HasIdData, round.HasIdData);
        Assert.Equal(item.Id, round.Id);
        Assert.Equal(item.LastIdTime, round.LastIdTime);
        Assert.Equal(item.ObjectClass, round.ObjectClass);
        Assert.Equal(item.BoolValues, round.BoolValues);
        Assert.Equal(item.DoubleValues, round.DoubleValues);
        Assert.Equal(item.IntValues, round.IntValues);
        Assert.Equal(item.StringValues, round.StringValues);
        Assert.Equal(item.ActiveSpells, round.ActiveSpells);
        Assert.Equal(item.Spells, round.Spells);
    }

    [Fact]
    public void CorruptContentIsReportedAsNotImported()
    {
        bool ok = InventoryLoggerXml.TryImport("this is not xml <<<", out List<MyWorldObjectRecord> imported);

        Assert.False(ok);
        Assert.Empty(imported);
    }

    [Fact]
    public void EmptyContentIsReportedAsNotImported()
    {
        bool ok = InventoryLoggerXml.TryImport(string.Empty, out List<MyWorldObjectRecord> imported);

        Assert.False(ok);
        Assert.Empty(imported);
    }
}
