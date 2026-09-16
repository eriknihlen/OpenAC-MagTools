using System.Globalization;
using System.Xml.Linq;

namespace OpenAC.MagTools.Loggers.Inventory;

/// <summary>
/// Ports the <c>XmlSerializer</c> dump of <c>List&lt;MyWorldObject&gt;</c>
/// (see the port design's §1.18/§2.7): root <c>ArrayOfMyWorldObject</c> (the
/// serializer's own default name for a bare <c>List&lt;T&gt;</c>), one
/// <c>MyWorldObject</c> element per item in field-declaration order, and each
/// <c>SerializableDictionary</c> as
/// <c>&lt;item&gt;&lt;key&gt;..&lt;/key&gt;&lt;value&gt;..&lt;/value&gt;&lt;/item&gt;</c>
/// children.
/// </summary>
public static class InventoryLoggerXml
{
    public static string Export(IEnumerable<MyWorldObjectRecord> items)
    {
        var root = new XElement("ArrayOfMyWorldObject");
        foreach (MyWorldObjectRecord item in items)
        {
            root.Add(new XElement(
                "MyWorldObject",
                new XElement("HasIdData", item.HasIdData),
                new XElement("Id", item.Id.ToString(CultureInfo.InvariantCulture)),
                new XElement("LastIdTime", item.LastIdTime.ToString(CultureInfo.InvariantCulture)),
                new XElement("ObjectClass", item.ObjectClass.ToString(CultureInfo.InvariantCulture)),
                Dict("BoolValues", item.BoolValues, static v => v.ToString()),
                Dict("DoubleValues", item.DoubleValues, static v => v.ToString(CultureInfo.InvariantCulture)),
                Dict("IntValues", item.IntValues, static v => v.ToString(CultureInfo.InvariantCulture)),
                Dict("StringValues", item.StringValues, static v => v),
                new XElement("ActiveSpells", item.ActiveSpells.Select(id => new XElement("int", id))),
                new XElement("Spells", item.Spells.Select(id => new XElement("int", id)))));
        }

        return new XDocument(root).ToString();
    }

    private static XElement Dict<TValue>(string elementName, IReadOnlyDictionary<int, TValue> values, Func<TValue, string> format)
        => new(
            elementName,
            values.Select(kvp => new XElement(
                "item",
                new XElement("key", kvp.Key.ToString(CultureInfo.InvariantCulture)),
                new XElement("value", format(kvp.Value)))));

    /// <summary>Returns false for anything that doesn't parse, matching the original's "file is corrupt" path.</summary>
    public static bool TryImport(string content, out List<MyWorldObjectRecord> items)
    {
        items = [];
        if (string.IsNullOrWhiteSpace(content))
            return false;

        XElement? root;
        try
        {
            root = XDocument.Parse(content).Element("ArrayOfMyWorldObject");
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        if (root is null)
            return false;

        foreach (XElement node in root.Elements("MyWorldObject"))
        {
            bool hasIdData = (bool?)node.Element("HasIdData") ?? false;
            uint id = uint.TryParse((string?)node.Element("Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsedId) ? parsedId : 0u;
            int lastIdTime = int.TryParse((string?)node.Element("LastIdTime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLastId) ? parsedLastId : 0;
            int objectClass = int.TryParse((string?)node.Element("ObjectClass"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedClass) ? parsedClass : 0;

            items.Add(new MyWorldObjectRecord
            {
                HasIdData = hasIdData,
                Id = id,
                LastIdTime = lastIdTime,
                ObjectClass = objectClass,
                BoolValues = ReadDict(node, "BoolValues", static text => bool.TryParse(text, out bool v) && v),
                DoubleValues = ReadDict(node, "DoubleValues", static text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0d),
                IntValues = ReadDict(node, "IntValues", static text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0),
                StringValues = ReadDict(node, "StringValues", static text => text),
                ActiveSpells = node.Element("ActiveSpells")?.Elements("int").Select(e => (int?)e ?? 0).ToList() ?? [],
                Spells = node.Element("Spells")?.Elements("int").Select(e => (int?)e ?? 0).ToList() ?? [],
            });
        }

        return true;
    }

    private static Dictionary<int, TValue> ReadDict<TValue>(XElement parent, string elementName, Func<string, TValue> parse)
    {
        var result = new Dictionary<int, TValue>();
        XElement? dict = parent.Element(elementName);
        if (dict is null)
            return result;

        foreach (XElement item in dict.Elements("item"))
        {
            string? keyText = (string?)item.Element("key");
            if (keyText is null || !int.TryParse(keyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int key))
                continue;

            string valueText = (string?)item.Element("value") ?? string.Empty;
            result[key] = parse(valueText);
        }

        return result;
    }
}
