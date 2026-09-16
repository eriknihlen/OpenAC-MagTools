using System.Globalization;
using System.Xml;

namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Port of the original's <c>Trackers.Combat.CombatTrackerImporter</c>,
/// reading the string <see cref="AcDream.Plugin.Abstractions.IPluginStorage"/>
/// hands back instead of loading a real file.
/// </summary>
public static class CombatTrackerImporter
{
    /// <summary>
    /// Parses <paramref name="xml"/> into fresh lists. Returns false (and
    /// empty lists) for null/empty input or malformed XML — same shape as
    /// the original's "file does not exist" early return.
    /// </summary>
    public static bool Import(
        string? xml,
        out List<CombatInfo> combatInfos,
        out List<AetheriaInfo> aetheriaInfos,
        out List<CloakInfo> cloakInfos)
    {
        combatInfos = [];
        aetheriaInfos = [];
        cloakInfos = [];

        if (string.IsNullOrEmpty(xml))
            return false;

        var xmlDocument = new XmlDocument();
        try
        {
            xmlDocument.LoadXml(xml);
        }
        catch (XmlException)
        {
            return false;
        }

        XmlNode? parentNode = xmlDocument.SelectSingleNode("CombatTracker");
        if (parentNode is null)
            return false;

        XmlNode? combatInfosNode = xmlDocument.SelectSingleNode("CombatTracker/CombatInfos");
        if (combatInfosNode is { HasChildNodes: true })
        {
            foreach (XmlNode combatInfoNode in combatInfosNode.ChildNodes)
            {
                if (combatInfoNode.Attributes is not { Count: > 0 } attributes)
                    continue;

                XmlAttribute? sourceName = attributes["SourceName"];
                XmlAttribute? targetName = attributes["TargetName"];
                if (sourceName is null || targetName is null)
                    continue;

                var info = new CombatInfo(sourceName.Value, targetName.Value);

                if (attributes["KillingBlows"] is { } killingBlows
                    && int.TryParse(killingBlows.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int kb))
                {
                    info.KillingBlows = kb;
                }

                if (combatInfoNode.HasChildNodes)
                {
                    foreach (XmlNode attackTypeNode in combatInfoNode.ChildNodes)
                    {
                        if (!Enum.TryParse(attackTypeNode.Name, out AttackType attackType))
                            continue;

                        var byAttackType = new DamageByAttackType();

                        if (attackTypeNode.HasChildNodes)
                        {
                            foreach (XmlNode elementNode in attackTypeNode.ChildNodes)
                            {
                                if (elementNode.Attributes is not { Count: > 0 } elementAttributes)
                                    continue;
                                if (!Enum.TryParse(elementNode.Name, out DamageElement damageElement))
                                    continue;

                                var byElement = new DamageByElement
                                {
                                    TotalAttacks = ReadInt(elementAttributes, "TotalAttacks"),
                                    FailedAttacks = ReadInt(elementAttributes, "FailedAttacks"),
                                    Crits = ReadInt(elementAttributes, "Crits"),
                                    TotalNormalDamage = ReadInt(elementAttributes, "TotalNormalDamage"),
                                    MaxNormalDamage = ReadInt(elementAttributes, "MaxNormalDamage"),
                                    TotalCritDamage = ReadInt(elementAttributes, "TotalCritDamage"),
                                    MaxCritDamage = ReadInt(elementAttributes, "MaxCritDamage"),
                                };

                                byAttackType.DamageByElements[damageElement] = byElement;
                            }
                        }

                        info.DamageByAttackTypes[attackType] = byAttackType;
                    }
                }

                combatInfos.Add(info);
            }
        }

        XmlNode? aetheriaInfosNode = xmlDocument.SelectSingleNode("CombatTracker/AetheriaInfos");
        if (aetheriaInfosNode is { HasChildNodes: true })
        {
            foreach (XmlNode aetheriaInfoNode in aetheriaInfosNode.ChildNodes)
            {
                if (aetheriaInfoNode.Attributes is not { Count: > 0 } attributes)
                    continue;

                XmlAttribute? sourceName = attributes["SourceName"];
                XmlAttribute? targetName = attributes["TargetName"];
                if (sourceName is null || targetName is null)
                    continue;

                aetheriaInfos.Add(new AetheriaInfo(sourceName.Value, targetName.Value)
                {
                    TotalSurges = ReadInt(attributes, "TotalSurges"),
                });
            }
        }

        XmlNode? cloakInfosNode = xmlDocument.SelectSingleNode("CombatTracker/CloakInfos");
        if (cloakInfosNode is { HasChildNodes: true })
        {
            foreach (XmlNode cloakInfoNode in cloakInfosNode.ChildNodes)
            {
                if (cloakInfoNode.Attributes is not { Count: > 0 } attributes)
                    continue;

                XmlAttribute? sourceName = attributes["SourceName"];
                XmlAttribute? targetName = attributes["TargetName"];
                if (sourceName is null || targetName is null)
                    continue;

                cloakInfos.Add(new CloakInfo(sourceName.Value, targetName.Value)
                {
                    TotalSurges = ReadInt(attributes, "TotalSurges"),
                });
            }
        }

        return true;
    }

    private static int ReadInt(XmlAttributeCollection attributes, string name)
    {
        if (attributes[name] is { } attribute
            && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return 0;
    }
}
