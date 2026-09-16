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
    public void WithClassifierPrintsTheTellWrappedMarkerAndRuleName()
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
        Assert.StartsWith("<Tell:IIDString:221112:1>+(Epics)", line);
        Assert.Contains(@"<\\Tell> ", line);
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

        Assert.StartsWith("<Tell:IIDString:221112:1>-", host.ChatLines[0]);
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
    public void UnknownObjectIdDoesNothing()
    {
        (FakeHost host, _, _, ItemInfoPrinter printer) = Build();

        printer.Print(new ItemIdentArgs(999u));

        Assert.Empty(host.ChatLines);
    }
}
