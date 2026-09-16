using System.Xml;

namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.CombatTrackerExporter</c>,
/// adapted from a real file write to a string (the caller writes it through
/// <see cref="AcDream.Plugin.Abstractions.IPluginStorage"/>, which is
/// text-key/text-value only — there is no raw file handle to hand
/// <see cref="XmlDocument.Save(string)"/>).
/// </summary>
public static class CombatTrackerExporter
{
    /// <summary>
    /// Builds the export XML, or null when all three lists are empty — same
    /// as the original's <c>ExportStats</c> skipping the write entirely in
    /// that case.
    /// </summary>
    public static string? Export(
        IReadOnlyList<CombatInfo> combatInfos,
        IReadOnlyList<AetheriaInfo> aetheriaInfos,
        IReadOnlyList<CloakInfo> cloakInfos)
    {
        ArgumentNullException.ThrowIfNull(combatInfos);
        ArgumentNullException.ThrowIfNull(aetheriaInfos);
        ArgumentNullException.ThrowIfNull(cloakInfos);

        if (combatInfos.Count == 0 && aetheriaInfos.Count == 0 && cloakInfos.Count == 0)
            return null;

        var xmlDocument = new XmlDocument();
        xmlDocument.LoadXml("<CombatTracker></CombatTracker>");

        XmlNode? parentNode = xmlDocument.SelectSingleNode("CombatTracker");
        if (parentNode is null)
            return null;

        if (combatInfos.Count > 0)
        {
            parentNode.InnerXml += "<CombatInfos></CombatInfos>";
            XmlNode combatInfosNode = xmlDocument.SelectSingleNode("CombatTracker/CombatInfos")!;

            foreach (CombatInfo info in combatInfos)
            {
                XmlNode combatInfoNode = combatInfosNode.AppendChild(
                    xmlDocument.CreateElement("CombatInfo"))!;

                SetAttribute(xmlDocument, combatInfoNode, "SourceName", info.SourceName);
                SetAttribute(xmlDocument, combatInfoNode, "TargetName", info.TargetName);
                if (info.KillingBlows != 0)
                    SetAttribute(xmlDocument, combatInfoNode, "KillingBlows", info.KillingBlows);

                foreach (KeyValuePair<AttackType, DamageByAttackType> attackTypePair
                    in info.DamageByAttackTypes)
                {
                    XmlNode attackTypeNode = combatInfoNode.AppendChild(
                        xmlDocument.CreateElement(attackTypePair.Key.ToString()))!;

                    foreach (KeyValuePair<DamageElement, DamageByElement> elementPair
                        in attackTypePair.Value.DamageByElements)
                    {
                        XmlNode elementNode = attackTypeNode.AppendChild(
                            xmlDocument.CreateElement(elementPair.Key.ToString()))!;

                        DamageByElement byElement = elementPair.Value;

                        SetAttribute(xmlDocument, elementNode, "TotalAttacks", byElement.TotalAttacks);
                        if (byElement.FailedAttacks != 0)
                            SetAttribute(xmlDocument, elementNode, "FailedAttacks", byElement.FailedAttacks);
                        if (byElement.Crits != 0)
                            SetAttribute(xmlDocument, elementNode, "Crits", byElement.Crits);
                        if (byElement.TotalNormalDamage != 0)
                            SetAttribute(xmlDocument, elementNode, "TotalNormalDamage", byElement.TotalNormalDamage);
                        if (byElement.MaxNormalDamage != 0)
                            SetAttribute(xmlDocument, elementNode, "MaxNormalDamage", byElement.MaxNormalDamage);
                        if (byElement.TotalCritDamage != 0)
                            SetAttribute(xmlDocument, elementNode, "TotalCritDamage", byElement.TotalCritDamage);
                        if (byElement.MaxCritDamage != 0)
                            SetAttribute(xmlDocument, elementNode, "MaxCritDamage", byElement.MaxCritDamage);
                    }
                }
            }
        }

        if (aetheriaInfos.Count > 0)
        {
            parentNode.InnerXml += "<AetheriaInfos></AetheriaInfos>";
            XmlNode aetheriaInfosNode = xmlDocument.SelectSingleNode("CombatTracker/AetheriaInfos")!;

            foreach (AetheriaInfo info in aetheriaInfos)
            {
                XmlNode aetheriaInfoNode = aetheriaInfosNode.AppendChild(
                    xmlDocument.CreateElement("AetheriaInfo"))!;

                SetAttribute(xmlDocument, aetheriaInfoNode, "SourceName", info.SourceName);
                SetAttribute(xmlDocument, aetheriaInfoNode, "TargetName", info.TargetName);
                if (info.TotalSurges != 0)
                    SetAttribute(xmlDocument, aetheriaInfoNode, "TotalSurges", info.TotalSurges);
            }
        }

        if (cloakInfos.Count > 0)
        {
            parentNode.InnerXml += "<CloakInfos></CloakInfos>";
            XmlNode cloakInfosNode = xmlDocument.SelectSingleNode("CombatTracker/CloakInfos")!;

            foreach (CloakInfo info in cloakInfos)
            {
                XmlNode cloakInfoNode = cloakInfosNode.AppendChild(
                    xmlDocument.CreateElement("CloakInfo"))!;

                SetAttribute(xmlDocument, cloakInfoNode, "SourceName", info.SourceName);
                SetAttribute(xmlDocument, cloakInfoNode, "TargetName", info.TargetName);
                if (info.TotalSurges != 0)
                    SetAttribute(xmlDocument, cloakInfoNode, "TotalSurges", info.TotalSurges);
            }
        }

        // The original wrote through XmlDocument.Save(path), which indents
        // (2 spaces per level) and emits an XML declaration — OuterXml does
        // neither (compact, single line, no declaration). Save(TextWriter)
        // reproduces that exact formatting against the string this port
        // hands to IPluginStorage instead of a real file. See
        // docs/deviations.md.
        using var writer = new StringWriter();
        xmlDocument.Save(writer);
        return writer.ToString();
    }

    private static void SetAttribute(XmlDocument document, XmlNode node, string name, string value)
    {
        XmlAttribute attribute = document.CreateAttribute(name);
        attribute.Value = value;
        node.Attributes!.Append(attribute);
    }

    private static void SetAttribute(XmlDocument document, XmlNode node, string name, int value)
        => SetAttribute(document, node, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
