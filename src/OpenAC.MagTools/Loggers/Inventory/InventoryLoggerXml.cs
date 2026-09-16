using System.Globalization;
using System.Xml.Linq;

namespace OpenAC.MagTools.Loggers.Inventory;

/// <summary>
/// Ports the <c>XmlSerializer</c> dump of <c>List&lt;MyWorldObject&gt;</c>
/// (see the port design's §1.18/§2.7): root <c>ArrayOfMyWorldObject</c> (the
/// serializer's own default name for a bare <c>List&lt;T&gt;</c>), one
/// <c>MyWorldObject</c> element per item in field-declaration order, and each
/// <c>SerializableDictionary&lt;int, TValue&gt;</c> exactly as its own
/// <c>WriteXml</c>/<c>ReadXml</c> shape: <c>&lt;item&gt;&lt;key&gt;&lt;int&gt;
/// N&lt;/int&gt;&lt;/key&gt;&lt;value&gt;&lt;TYPE&gt;V&lt;/TYPE&gt;&lt;/value&gt;
/// &lt;/item&gt;</c>, where the inner element name is <c>XmlSerializer</c>'s own
/// XSD-primitive root name for the value's CLR type (<c>bool</c> -&gt;
/// <c>boolean</c>, <c>double</c> -&gt; <c>double</c>, <c>int</c> -&gt; <c>int</c>,
/// <c>string</c> -&gt; <c>string</c>), lowercase <c>true</c>/<c>false</c> for
/// booleans (M2). This makes a file this logger writes STRUCTURALLY
/// INTERCHANGEABLE with one the original wrote (and vice versa) -- same
/// element names, nesting, and value shapes, so either importer can read
/// either file -- not merely readable by our own importer. It is NOT
/// byte-shape compatible: this writer emits no <c>&lt;?xml ...?&gt;</c>
/// declaration and no <c>xmlns:xsi</c>/<c>xmlns:xsd</c> namespace attributes
/// on the root element the way <c>XmlSerializer</c>'s own default output
/// does, so a byte-for-byte diff against an original-written file will not
/// match even when both parse to the same structure.
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
                new XElement("HasIdData", Bool(item.HasIdData)),
                // The original's Id is a signed 32-bit int over the same wire
                // guid bits; reinterpreting our uint the same way keeps a
                // high-bit object id byte-identical to what the original
                // would have written (LOW: uint-vs-int ids).
                new XElement("Id", unchecked((int)item.Id).ToString(CultureInfo.InvariantCulture)),
                new XElement("LastIdTime", item.LastIdTime.ToString(CultureInfo.InvariantCulture)),
                new XElement("ObjectClass", item.ObjectClass.ToString(CultureInfo.InvariantCulture)),
                Dict("BoolValues", item.BoolValues, "boolean", static v => Bool(v)),
                Dict("DoubleValues", item.DoubleValues, "double", static v => v.ToString(CultureInfo.InvariantCulture)),
                Dict("IntValues", item.IntValues, "int", static v => v.ToString(CultureInfo.InvariantCulture)),
                Dict("StringValues", item.StringValues, "string", static v => v),
                new XElement("ActiveSpells", item.ActiveSpells.Select(id => new XElement("int", id))),
                new XElement("Spells", item.Spells.Select(id => new XElement("int", id)))));
        }

        return new XDocument(root).ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static XElement Dict<TValue>(
        string elementName,
        IReadOnlyDictionary<int, TValue> values,
        string valueTypeName,
        Func<TValue, string> format)
        => new(
            elementName,
            values.Select(kvp => new XElement(
                "item",
                new XElement("key", new XElement("int", kvp.Key.ToString(CultureInfo.InvariantCulture))),
                new XElement("value", new XElement(valueTypeName, format(kvp.Value))))));

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
            bool hasIdData = ParseBool((string?)node.Element("HasIdData"));
            uint id = int.TryParse((string?)node.Element("Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId)
                ? unchecked((uint)parsedId)
                : 0u;
            int lastIdTime = int.TryParse((string?)node.Element("LastIdTime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLastId) ? parsedLastId : 0;
            int objectClass = int.TryParse((string?)node.Element("ObjectClass"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedClass) ? parsedClass : 0;

            items.Add(new MyWorldObjectRecord
            {
                HasIdData = hasIdData,
                Id = id,
                LastIdTime = lastIdTime,
                ObjectClass = objectClass,
                BoolValues = ReadDict(node, "BoolValues", static text => ParseBool(text)),
                DoubleValues = ReadDict(node, "DoubleValues", static text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0d),
                IntValues = ReadDict(node, "IntValues", static text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0),
                StringValues = ReadDict(node, "StringValues", static text => text ?? string.Empty),
                ActiveSpells = node.Element("ActiveSpells")?.Elements("int").Select(e => (int?)e ?? 0).ToList() ?? [],
                Spells = node.Element("Spells")?.Elements("int").Select(e => (int?)e ?? 0).ToList() ?? [],
            });
        }

        return true;
    }

    private static bool ParseBool(string? text) => bool.TryParse(text, out bool value) && value;

    private static Dictionary<int, TValue> ReadDict<TValue>(XElement parent, string elementName, Func<string?, TValue> parse)
    {
        var result = new Dictionary<int, TValue>();
        XElement? dict = parent.Element(elementName);
        if (dict is null)
            return result;

        foreach (XElement item in dict.Elements("item"))
        {
            // <key><int>N</int></key> -- the inner element carries the value;
            // its own tag name doesn't matter for reading (only for
            // round-trip shape fidelity on write).
            string? keyText = (string?)item.Element("key")?.Elements().FirstOrDefault();
            if (keyText is null || !int.TryParse(keyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int key))
                continue;

            string? valueText = (string?)item.Element("value")?.Elements().FirstOrDefault();
            result[key] = parse(valueText);
        }

        return result;
    }
}
