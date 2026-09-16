using System.Xml.Linq;
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
    public void ExportMatchesSerializableDictionaryWriteXmlsExactShape()
    {
        // M2: the original's SerializableDictionary<TKey,TValue>.WriteXml
        // wraps each key/value in an XmlSerializer-typed inner element
        // (bool -> "boolean", int -> "int") and writes lowercase true/false
        // -- not a flat <key>131</key>. A file this logger writes must be
        // byte-shape compatible with one the original wrote.
        var item = new MyWorldObjectRecord
        {
            HasIdData = true,
            Id = 42u,
            BoolValues = new Dictionary<int, bool> { { 91, true } },
            IntValues = new Dictionary<int, int> { { 131, 61 } },
        };

        string xml = InventoryLoggerXml.Export([item]);
        XElement mwo = XDocument.Parse(xml).Root!.Element("MyWorldObject")!;

        Assert.Equal("true", (string?)mwo.Element("HasIdData"));

        XElement boolItem = mwo.Element("BoolValues")!.Element("item")!;
        Assert.Equal("91", (string?)boolItem.Element("key")!.Element("int"));
        Assert.Equal("true", (string?)boolItem.Element("value")!.Element("boolean"));

        XElement intItem = mwo.Element("IntValues")!.Element("item")!;
        Assert.Equal("131", (string?)intItem.Element("key")!.Element("int"));
        Assert.Equal("61", (string?)intItem.Element("value")!.Element("int"));
    }

    [Fact]
    public void HighBitObjectIdRoundTripsThroughTheSignedIntBitPattern()
    {
        // LOW: the original's Id field is a signed 32-bit int over the same
        // wire guid bits; a guid at or above 0x80000000 must still round-trip
        // exactly via the unchecked int<->uint reinterpretation.
        const uint highBitId = 0x80000042u;
        var item = new MyWorldObjectRecord { Id = highBitId };

        string xml = InventoryLoggerXml.Export([item]);
        bool ok = InventoryLoggerXml.TryImport(xml, out List<MyWorldObjectRecord> imported);

        Assert.True(ok);
        Assert.Equal(highBitId, Assert.Single(imported).Id);
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
