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
    public void ClassifyMapsNamedNoLootActionToNotPassing()
    {
        // The original's own check is a literal !result.IsNoLoot — a NoLoot
        // result is always a fail, named rule or not. This is NOT the
        // ManaStone/ManaTank case: a real .utl rule that legitimately
        // resolves to NoLoot with a name attached still fails today.
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.NoLoot, RuleName: "SomeAuthoredRule");
        var processor = new LootRuleProcessor(registry);

        LootVerdict? verdict = processor.Classify(EmptyContext());

        Assert.False(verdict!.Value.Passes);
    }

    [Theory]
    [InlineData("ManaStone")]
    [InlineData("ManaTank")]
    public void ManaStoneAndManaTankStillFailUntilOpenAcAddsTheEnumMembers(string ruleName)
    {
        // A host artefact, not original behavior: the classifier registry
        // reports these two specific keep-rules as Matched:true,
        // Action.NoLoot because the CURRENT OpenAC snapshot's shared
        // PluginLootAction enum has no ManaStone/ManaTank members yet.
        // LootRuleProcessor.Classify guards a future fix by NAME via
        // Enum.TryParse (see IsManaRuleAction) rather than referencing
        // PluginLootAction.ManaStone/.ManaTank directly, which wouldn't
        // compile against this snapshot. Until OpenAC adds those members,
        // TryParse can't resolve the name, so this is a no-op and the
        // verdict still fails — that's the DOCUMENTED CURRENT LIMITATION,
        // not the desired end state. See docs/deviations.md.
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.NoLoot, RuleName: ruleName);
        var processor = new LootRuleProcessor(registry);

        LootVerdict? verdict = processor.Classify(EmptyContext());

        Assert.False(verdict!.Value.Passes);
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

    [Fact]
    public void TryClassifyLiveReturnsTheRawClassificationWhenMatched()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(
            Matched: true, Action: PluginLootAction.KeepUpTo, RuleName: "arrows", KeepCount: 200);
        var processor = new LootRuleProcessor(registry);

        Assert.True(processor.TryClassifyLive(EmptyContext(), out PluginLootClassification classification));
        Assert.Equal(PluginLootAction.KeepUpTo, classification.Action);
        Assert.Equal(200, classification.KeepCount);
    }

    [Fact]
    public void TryClassifyLiveFailsWhenNotMatched()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ClassificationResult = new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot);
        var processor = new LootRuleProcessor(registry);

        Assert.False(processor.TryClassifyLive(EmptyContext(), out _));
    }

    [Fact]
    public void TryClassifyLiveFailsWithNoClassifier()
    {
        var registry = new FakeLootClassifierRegistry();
        var processor = new LootRuleProcessor(registry);

        Assert.False(processor.TryClassifyLive(EmptyContext(), out _));
    }

    [Fact]
    public void TryClassifyWithProfileForwardsTheProfileNameToTheRegistry()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ProfileClassifyHandler = (profile, _)
            => profile == "Vendor"
                ? new PluginLootClassification(Matched: true, Action: PluginLootAction.Keep)
                : null;
        var processor = new LootRuleProcessor(registry);

        Assert.True(processor.TryClassifyWithProfile("Vendor", EmptyContext(), out PluginLootClassification classification));
        Assert.Equal(PluginLootAction.Keep, classification.Action);
        Assert.False(processor.TryClassifyWithProfile("Trader", EmptyContext(), out _));
    }

    [Fact]
    public void ProfileExistsIsTrueWhenTheRegistryFindsTheNamedProfileRegardlessOfMatch()
    {
        var registry = new FakeLootClassifierRegistry();
        registry.Available.Add(new PluginLootClassifierInfo("engine", "Engine"));
        registry.ProfileClassifyHandler = (profile, _)
            => profile == "Vendor"
                ? new PluginLootClassification(Matched: false, Action: PluginLootAction.NoLoot)
                : null;
        var processor = new LootRuleProcessor(registry);

        Assert.True(processor.ProfileExists("Vendor"));
        Assert.False(processor.ProfileExists("Trader"));
    }

    [Fact]
    public void ProfileExistsIsFalseWithNoClassifier()
    {
        var registry = new FakeLootClassifierRegistry();
        var processor = new LootRuleProcessor(registry);

        Assert.False(processor.ProfileExists("Vendor"));
    }

    [Fact]
    public void BuildMinimalItemCarriesTheNameClassStackAndValue()
    {
        PluginInventoryItem item = LootRuleProcessor.BuildMinimalItem(
            7u, 700u, "Peerless Mana Potion", PluginObjectClass.Food, stackSize: 3, value: 25);

        Assert.Equal(7u, item.ObjectId);
        Assert.Equal(700u, item.WeenieClassId);
        Assert.Equal("Peerless Mana Potion", item.Name);
        Assert.Equal(PluginObjectClass.Food, item.ObjectClass);
        Assert.Equal(3, item.StackSize);
        Assert.Equal(25, item.Value);
    }
}
