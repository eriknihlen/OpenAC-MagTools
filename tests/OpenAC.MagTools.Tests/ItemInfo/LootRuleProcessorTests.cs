using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.ItemInfo;

public sealed class LootRuleProcessorTests
{
    private static PluginLootClassificationContext EmptyContext()
        => new(default, default, []);

    [Fact]
    public void NoRegisteredClassifierReturnsNullFromClassify()
    {
        var registry = new FakeLootClassifierRegistry();
        var processor = new LootRuleProcessor(registry);

        Assert.Null(processor.Classify(EmptyContext()));
    }

    [Fact]
    public void PrefersAClassifierWhoseIdContainsMossTank()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("other-engine", "Other"));
        registry.Available.Add(new PluginLootClassifierInfo("openac.moss-tank", "MossTank"));
        var processor = new LootRuleProcessor(registry);

        Assert.Equal("openac.moss-tank", processor.ActiveClassifierId);
    }

    [Fact]
    public void FallsBackToTheFirstRegisteredClassifierWhenNoneMatchMossTank()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("some-engine", "Some Engine"));
        var processor = new LootRuleProcessor(registry);

        Assert.Equal("some-engine", processor.ActiveClassifierId);
    }

    [Fact]
    public void ClassifyMapsMatchedNonNoLootToPasses()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.Keep, RuleName: "MyRule");
        var processor = new LootRuleProcessor(registry);

        LootVerdict? verdict = processor.Classify(EmptyContext());

        Assert.NotNull(verdict);
        Assert.True(verdict!.Value.Passes);
        Assert.False(verdict.Value.IsSalvage);
        Assert.Equal("MyRule", verdict.Value.RuleName);
    }

    [Fact]
    public void ClassifyMapsUnnamedNoLootActionToNotPassing()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.NoLoot, RuleName: string.Empty);
        var processor = new LootRuleProcessor(registry);

        LootVerdict? verdict = processor.Classify(EmptyContext());

        Assert.False(verdict!.Value.Passes);
    }

    [Fact]
    public void ClassifyMapsNamedNoLootActionToPassing()
    {
        // ManaStone/ManaTank rules: the classifier reports Matched:true,
        // Action.NoLoot, but with a real RuleName attached. The original
        // still printed "+(Rule)" for these (its own "!IsNoLoot" check),
        // so a NAMED NoLoot verdict counts as passing. See M5 in
        // docs/deviations.md.
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.NoLoot, RuleName: "ManaStone");
        var processor = new LootRuleProcessor(registry);

        LootVerdict? verdict = processor.Classify(EmptyContext());

        Assert.NotNull(verdict);
        Assert.True(verdict!.Value.Passes);
        Assert.Equal("ManaStone", verdict.Value.RuleName);
    }

    [Fact]
    public void ClassifyMapsSalvageActionToIsSalvage()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.Salvage, RuleName: "Salv");
        var processor = new LootRuleProcessor(registry);

        LootVerdict? verdict = processor.Classify(EmptyContext());

        Assert.True(verdict!.Value.Passes);
        Assert.True(verdict.Value.IsSalvage);
    }

    [Fact]
    public void NeedsIdentificationForwardsToTheActiveClassifier()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.NeedsIdentificationResult = true;
        var processor = new LootRuleProcessor(registry);

        Assert.True(processor.NeedsIdentification(EmptyContext()));
    }

    [Fact]
    public void NeedsIdentificationIsFalseWithNoClassifier()
    {
        var registry = new FakeLootClassifierRegistry();
        var processor = new LootRuleProcessor(registry);

        Assert.False(processor.NeedsIdentification(EmptyContext()));
    }
}
