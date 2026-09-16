using System.Globalization;
using System.Xml.Linq;

namespace OpenAC.MagTools.Trackers.Player;

/// <summary>
/// Ports <c>PlayerTrackerExporter</c>/<c>PlayerTrackerImporter</c>'s
/// <c>&lt;Players&gt;&lt;Player .../&gt;&lt;/Players&gt;</c> shape verbatim.
/// </summary>
public static class PlayerTrackerXml
{
    public static string Export(IEnumerable<TrackedPlayer> items)
    {
        var root = new XElement("Players");
        foreach (TrackedPlayer item in items)
        {
            root.Add(new XElement(
                "Player",
                new XAttribute("Name", item.Name),
                new XAttribute("LastSeen", item.LastSeen.Ticks.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LandBlock", item.LandBlock.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LocationX", item.LocationX.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LocationY", item.LocationY.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("LocationZ", item.LocationZ.ToString(CultureInfo.InvariantCulture))));
        }

        return new XDocument(root).ToString();
    }

    public static bool TryImport(string content, out List<TrackedPlayer> items)
    {
        items = [];
        if (string.IsNullOrWhiteSpace(content))
            return false;

        XElement? root;
        try
        {
            root = XDocument.Parse(content).Element("Players");
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        if (root is null)
            return false;

        foreach (XElement node in root.Elements("Player"))
        {
            string? name = (string?)node.Attribute("Name");
            if (string.IsNullOrEmpty(name))
                continue;

            var item = new TrackedPlayer(name);

            if (long.TryParse((string?)node.Attribute("LastSeen"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                item.LastSeen = new DateTime(ticks);
            if (int.TryParse((string?)node.Attribute("LandBlock"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int landBlock))
                item.LandBlock = landBlock;
            if (double.TryParse((string?)node.Attribute("LocationX"), NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
                item.LocationX = x;
            if (double.TryParse((string?)node.Attribute("LocationY"), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                item.LocationY = y;
            if (double.TryParse((string?)node.Attribute("LocationZ"), NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                item.LocationZ = z;

            items.Add(item);
        }

        return true;
    }
}
