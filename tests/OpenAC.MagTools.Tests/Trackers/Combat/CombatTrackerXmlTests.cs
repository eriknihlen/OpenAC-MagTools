using OpenAC.MagTools.Trackers.Combat;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

public sealed class CombatTrackerXmlTests
{
    [Fact]
    public void ExportReturnsNullWhenEveryListIsEmpty()
        => Assert.Null(CombatTrackerExporter.Export([], [], []));

    [Fact]
    public void ExportWritesIndentedXmlLikeXmlDocumentSave()
    {
        var info = new CombatInfo("Acdream", "Drudge") { KillingBlows = 1 };

        string? xml = CombatTrackerExporter.Export([info], [], []);

        Assert.NotNull(xml);
        // XmlDocument.Save (the original's own write path — this port
        // writes to IPluginStorage instead of a real file, but keeps the
        // same formatting) indents 2 spaces per nesting level and emits an
        // XML declaration; OuterXml is compact single-line with neither.
        // See docs/deviations.md.
        Assert.StartsWith("<?xml", xml);
        Assert.Contains("\n  <CombatInfos>", xml);
        Assert.Contains("\n    <CombatInfo ", xml);
    }

    [Fact]
    public void ZeroValuedAttributesAreOmittedOnExport()
    {
        var info = new CombatInfo("Acdream", "Drudge");
        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Acdream", "Drudge", AttackType.MeleeMissle, DamageElement.Slash,
            IsFailedAttack: false, IsCriticalHit: false, IsOverpower: false,
            IsSneakAttack: false, IsRecklessness: false, IsKillingBlow: false, DamageAmount: 50));

        string? xml = CombatTrackerExporter.Export([info], [], []);

        Assert.NotNull(xml);
        // KillingBlows is 0 -> omitted entirely.
        Assert.DoesNotContain("KillingBlows", xml);
        // FailedAttacks/Crits/TotalCritDamage/MaxCritDamage are 0 -> omitted.
        Assert.DoesNotContain("FailedAttacks", xml);
        Assert.DoesNotContain("Crits=", xml);
        Assert.DoesNotContain("TotalCritDamage", xml);
        Assert.DoesNotContain("MaxCritDamage", xml);
        // Non-zero attributes are present.
        Assert.Contains("TotalAttacks=\"1\"", xml);
        Assert.Contains("TotalNormalDamage=\"50\"", xml);
        Assert.Contains("MaxNormalDamage=\"50\"", xml);
        // Empty sections never appear.
        Assert.DoesNotContain("AetheriaInfos", xml);
        Assert.DoesNotContain("CloakInfos", xml);
    }

    [Fact]
    public void RoundTripPreservesEveryCounterAcrossBothAttackTypes()
    {
        var info = new CombatInfo("Acdream", "Drudge") { KillingBlows = 3 };
        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Acdream", "Drudge", AttackType.MeleeMissle, DamageElement.Slash,
            false, false, false, false, false, false, 40));
        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Acdream", "Drudge", AttackType.MeleeMissle, DamageElement.Slash,
            false, true, false, false, false, false, 90));
        info.AddFromCombatEventArgs(new CombatEventArgs(
            "Acdream", "Drudge", AttackType.Magic, DamageElement.Fire,
            false, false, false, false, false, false, 25));

        var aetheria = new AetheriaInfo("Acdream", "Acdream") { TotalSurges = 7 };
        var cloak = new CloakInfo("Acdream", "Acdream") { TotalSurges = 2 };

        string? xml = CombatTrackerExporter.Export([info], [aetheria], [cloak]);
        Assert.NotNull(xml);

        bool ok = CombatTrackerImporter.Import(
            xml, out List<CombatInfo> combatInfos, out List<AetheriaInfo> aetheriaInfos,
            out List<CloakInfo> cloakInfos);

        Assert.True(ok);
        CombatInfo roundTripped = Assert.Single(combatInfos);
        Assert.Equal("Acdream", roundTripped.SourceName);
        Assert.Equal("Drudge", roundTripped.TargetName);
        Assert.Equal(3, roundTripped.KillingBlows);

        DamageByAttackType melee = roundTripped.DamageByAttackTypes[AttackType.MeleeMissle];
        DamageByElement slash = melee.DamageByElements[DamageElement.Slash];
        Assert.Equal(2, slash.TotalAttacks);
        Assert.Equal(1, slash.Crits);
        Assert.Equal(40, slash.TotalNormalDamage);
        Assert.Equal(40, slash.MaxNormalDamage);
        Assert.Equal(90, slash.TotalCritDamage);
        Assert.Equal(90, slash.MaxCritDamage);

        DamageByAttackType magic = roundTripped.DamageByAttackTypes[AttackType.Magic];
        Assert.Equal(25, magic.DamageByElements[DamageElement.Fire].TotalNormalDamage);

        AetheriaInfo roundTrippedAetheria = Assert.Single(aetheriaInfos);
        Assert.Equal(7, roundTrippedAetheria.TotalSurges);
        CloakInfo roundTrippedCloak = Assert.Single(cloakInfos);
        Assert.Equal(2, roundTrippedCloak.TotalSurges);
    }

    [Fact]
    public void ImportOfNullOrEmptyStringReturnsFalseWithEmptyLists()
    {
        bool ok = CombatTrackerImporter.Import(
            null, out List<CombatInfo> combatInfos, out List<AetheriaInfo> aetheriaInfos,
            out List<CloakInfo> cloakInfos);

        Assert.False(ok);
        Assert.Empty(combatInfos);
        Assert.Empty(aetheriaInfos);
        Assert.Empty(cloakInfos);
    }

    [Fact]
    public void ImportOfMalformedXmlReturnsFalse()
    {
        bool ok = CombatTrackerImporter.Import(
            "<not-well-formed", out List<CombatInfo> combatInfos, out _, out _);

        Assert.False(ok);
        Assert.Empty(combatInfos);
    }

    [Fact]
    public void ImportOfXmlWithoutTheRootElementReturnsFalse()
    {
        bool ok = CombatTrackerImporter.Import(
            "<SomethingElse></SomethingElse>", out List<CombatInfo> combatInfos, out _, out _);

        Assert.False(ok);
        Assert.Empty(combatInfos);
    }
}
