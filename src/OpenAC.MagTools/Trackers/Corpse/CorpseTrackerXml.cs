using System.Globalization;
using System.Xml.Linq;

namespace OpenAC.MagTools.Trackers.Corpse;

/// <summary>
/// Ports <c>CorpseTrackerExporter</c>/<c>CorpseTrackerImporter</c>'s
/// <c>&lt;Corpses&gt;&lt;Corpse .../&gt;&lt;/Corpses&gt;</c> shape verbatim
/// (same element/attribute names), through <c>IPluginStorage</c> text keys
/// instead of a real file (see docs/deviations.md's storage-key convention).
/// </summary>
public static class CorpseTrackerXml
{
    public static string Export(IEnumerable<TrackedCorpse> items)
    {
        var root = new XElement("Corpses");
        foreach (TrackedCorpse item in items)
        {
            root.Add(new XElement(
                "Corpse",
                new XAttribute("Id", item.Id.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("TimeStamp", item.TimeStamp.Ticks.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LandBlock", item.LandBlock.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LocationX", item.LocationX.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LocationY", item.LocationY.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LocationZ", item.LocationZ.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Description", item.Description),
                new XAttribute("Opened", item.Opened.ToString())));
        }

        return new XDocument(root).ToString();
    }

    /// <summary>Returns false (matching the original's "file is corrupt" path) for anything that doesn't parse.</summary>
    public static bool TryImport(string content, out List<TrackedCorpse> items)
    {
        items = [];
        if (string.IsNullOrWhiteSpace(content))
            return false;

        XElement? root;
        try
        {
            root = XDocument.Parse(content).Element("Corpses");
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        if (root is null)
            return false;

        foreach (XElement node in root.Elements("Corpse"))
        {
            string? idText = (string?)node.Attribute("Id");
            if (idText is null || !uint.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
                continue;

            var item = new TrackedCorpse(id);

            if (long.TryParse((string?)node.Attribute("TimeStamp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                item.TimeStamp = new DateTime(ticks);
            if (int.TryParse((string?)node.Attribute("LandBlock"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int landBlock))
                item.LandBlock = landBlock;
            if (double.TryParse((string?)node.Attribute("LocationX"), NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
                item.LocationX = x;
            if (double.TryParse((string?)node.Attribute("LocationY"), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                item.LocationY = y;
            if (double.TryParse((string?)node.Attribute("LocationZ"), NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                item.LocationZ = z;
            if ((string?)node.Attribute("Description") is { } description)
                item.Description = description;
            if (bool.TryParse((string?)node.Attribute("Opened"), out bool opened))
                item.Opened = opened;

            items.Add(item);
        }

        return true;
    }
}
