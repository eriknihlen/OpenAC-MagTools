using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.ItemInfo;

public sealed class ItemInfoPrinterTests
{
    private static (FakeHost Host, SettingsManager Settings, LootRuleProcessor Rules, ItemInfoPrinter Printer)
        Build()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var rules = new LootRuleProcessor(host.LootClassifiers);
        var chat = new ChatOutput(host);
        var printer = new ItemInfoPrinter(host, chat, settings.ItemInfoOnIdent, rules);
        return (host, settings, rules, printer);
    }

    private static void PutSword(FakeHost host, uint id = 1u, uint containerId = 0u)
    {
        host.Automation.Objects.Replace(new PluginWorldObject(
            id, 0u, "Sword", PluginObjectClass.MeleeWeapon, 0u, containerId, 0u)
        {
            HasAppraisalData = true,
        });
    }

    [Fact]
    public void NoClassifierPrintsThePlainInfoLine()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        PutSword(host);

        printer.Print(new ItemIdentArgs(1u));

        Assert.Single(host.ChatLines);
        Assert.Contains("Sword", host.ChatLines[0]);
        Assert.DoesNotContain("<Tell:", host.ChatLines[0]);
    }

    [Fact]
    public void NoClassifierAndDontShowIfNoRuleSuppressesTheLine()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        PutSword(host);

        printer.Print(new ItemIdentArgs(1u, DontShowIfItemHasNoRule: true));

        Assert.Empty(host.ChatLines);
    }

    [Fact]
    public void WithClassifierPrintsThePlainMarkerAndRuleNameWithNoTellWrapper()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        PutSword(host);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Sword"));
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        host.LootClassifiers.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.Keep, RuleName: "Epics");

        printer.Print(new ItemIdentArgs(1u));

        Assert.Single(host.ChatLines);
        string line = host.ChatLines[0];
        // No <Tell:...> wrapper: the host's chat markup only tags speech
        // ChatKinds, so a plugin-posted <Tell:...> would render as literal
        // text instead of a clickable link. See docs/deviations.md.
        Assert.StartsWith("+(Epics) ", line);
        Assert.DoesNotContain("<Tell:", line);
        Assert.Contains("Sword", line);
    }

    [Fact]
    public void FailingVerdictUsesAMinusMarker()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        PutSword(host);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Sword"));
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        host.LootClassifiers.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.NoLoot, RuleName: string.Empty);

        printer.Print(new ItemIdentArgs(1u));

        Assert.StartsWith("-", host.ChatLines[0]);
        Assert.DoesNotContain("<Tell:", host.ChatLines[0]);
    }

    [Fact]
    public void SalvageSuppressionHidesTheLineWhenRequested()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        PutSword(host);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Sword"));
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        host.LootClassifiers.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.Salvage, RuleName: "Salv");

        printer.Print(new ItemIdentArgs(1u, DontShowIfIsSalvageRule: true));

        Assert.Empty(host.ChatLines);
    }

    [Fact]
    public void NoRuleNameSuppressesTheLineWhenRequestedEvenWithAClassifier()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        PutSword(host);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Sword"));
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        host.LootClassifiers.ClassificationResult = new PluginLootClassification(
            Matched: false, Action: PluginLootAction.NoLoot, RuleName: string.Empty);

        printer.Print(new ItemIdentArgs(1u, DontShowIfItemHasNoRule: true));

        Assert.Empty(host.ChatLines);
    }

    [Fact]
    public void AutoClipboardWritesTheRuleLessTextToTheClipboardWhenAllowed()
    {
        (FakeHost host, SettingsManager settings, _, ItemInfoPrinter printer) = Build();
        settings.ItemInfoOnIdent.AutoClipboard.Value = true;
        PutSword(host);

        printer.Print(new ItemIdentArgs(1u, AllowAutoClipboard: true));

        Assert.Single(host.Clipboard.Written);
        Assert.Contains("Sword", host.Clipboard.Written[0]);
        Assert.DoesNotContain("<Tell:", host.Clipboard.Written[0]);
    }

    [Fact]
    public void AutoClipboardWithAClassifierOmitsTheTellWrapper()
    {
        (FakeHost host, SettingsManager settings, _, ItemInfoPrinter printer) = Build();
        settings.ItemInfoOnIdent.AutoClipboard.Value = true;
        PutSword(host);
        host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Sword"));
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        host.LootClassifiers.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.Keep, RuleName: "Epics");

        printer.Print(new ItemIdentArgs(1u, AllowAutoClipboard: true));

        Assert.Single(host.Clipboard.Written);
        Assert.StartsWith("+(Epics) ", host.Clipboard.Written[0]);
        Assert.DoesNotContain("<Tell:", host.Clipboard.Written[0]);
    }

    [Fact]
    public void NoAutoClipboardWithoutTheSettingOn()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        PutSword(host);

        printer.Print(new ItemIdentArgs(1u, AllowAutoClipboard: true));

        Assert.Empty(host.Clipboard.Written);
    }

    [Fact]
    public void LandscapeItemWithNoInventoryRecordIsStillClassified()
    {
        // The original always asked VTank to classify every appraised item,
        // not just owned/open-container ones. A landscape item (never added
        // to Owned, never in an open container) has no PluginInventoryItem —
        // it should still get a verdict off a synthetic item built from its
        // captured properties. See M6 in docs/deviations.md.
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();
        host.Automation.Objects.Replace(new PluginWorldObject(
            2u, 0u, "Loose Sword", PluginObjectClass.MeleeWeapon, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
        });
        host.Automation.Objects.Properties[2u] = ItemInfoFixtures.Props(
            ints: new Dictionary<uint, int> { { 44, 30 } }); // Damage
        host.LootClassifiers.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        host.LootClassifiers.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.Keep, RuleName: "Epics");

        printer.Print(new ItemIdentArgs(2u));

        Assert.Single(host.ChatLines);
        Assert.StartsWith("+(Epics) ", host.ChatLines[0]);
        Assert.Contains("Loose Sword", host.ChatLines[0]);
    }

    [Fact]
    public void UnknownObjectIdDoesNothing()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();

        printer.Print(new ItemIdentArgs(999u));

        Assert.Empty(host.ChatLines);
    }
}
