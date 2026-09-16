using System.Xml.Linq;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools.Tests;

public sealed class SettingsFileTests
{
    [Fact]
    public void AMissingSettingReadsItsDefault()
    {
        var file = new SettingsFile(new MemoryStorage());
        Assert.True(file.GetSetting("ManaManagement/AutoRecharge", true));
        Assert.Equal(7, file.GetSetting("Misc/OutputTargetWindow", 7));
    }

    [Fact]
    public void AWrittenSettingRoundTripsThroughTheDocument()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        file.PutSetting("Filters/AttackEvades", true);
        file.PutSetting("Misc/OutputTargetWindow", 2);
        file.Flush();

        var reloaded = new SettingsFile(storage);
        Assert.True(reloaded.GetSetting("Filters/AttackEvades", false));
        Assert.Equal(2, reloaded.GetSetting("Misc/OutputTargetWindow", 1));
    }

    [Fact]
    public void WritingCreatesEveryMissingElementOnThePath()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        file.PutSetting("ChatLogger/Group2/Allegiance", true);
        file.Flush();

        string xml = storage.ReadText(SettingsFile.StorageKey)!;
        Assert.Contains("<ChatLogger>", xml, StringComparison.Ordinal);
        Assert.Contains("<Group2>", xml, StringComparison.Ordinal);
        Assert.Contains("<Allegiance>True</Allegiance>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BooleansAreWrittenTheWayTheOriginalWroteThem()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        file.PutSetting("Filters/AttackEvades", true);
        file.PutSetting("Filters/DefenseEvades", false);
        file.Flush();

        string xml = storage.ReadText(SettingsFile.StorageKey)!;
        Assert.Contains("<AttackEvades>True</AttackEvades>", xml, StringComparison.Ordinal);
        Assert.Contains("<DefenseEvades>False</DefenseEvades>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExistingMagToolsFileIsReadUnchanged()
    {
        // The shape from the original's own documentation, dropped in by hand.
        const string existing = """
            <Mag-Tools>
              <ManaManagement><AutoRecharge>True</AutoRecharge></ManaManagement>
              <Filters><AttackEvades>False</AttackEvades></Filters>
              <Misc>
                <OutputTargetWindow>2</OutputTargetWindow>
              </Misc>
              <AutoTradeAccept><Whitelist><Name>MyMule</Name></Whitelist></AutoTradeAccept>
              <_myaccount_Frostfell><MyChar>
                 <OnLoginCommands><Command>/mt autopack</Command></OnLoginCommands>
                 <PeriodicCommands><PeriodicCommand offset="0" interval="5">/mt autopack</PeriodicCommand></PeriodicCommands>
              </MyChar></_myaccount_Frostfell>
            </Mag-Tools>
            """;

        var storage = new MemoryStorage();
        storage.Seed(SettingsFile.StorageKey, existing);

        var file = new SettingsFile(storage);
        var settings = new SettingsManager(file);

        Assert.True(settings.ManaManagement.AutoRecharge.Value);
        Assert.False(settings.Filters.AttackEvades.Value);
        Assert.Equal(2, settings.Misc.OutputTargetWindow.Value);
        Assert.Equal(["MyMule"], settings.AutoTradeAccept.WhitelistPatterns);

        string scope = SettingsScope.Character("myaccount", "Frostfell", "MyChar");
        Assert.Equal(
            ["/mt autopack"],
            settings.Commands.GetOnLoginCommands(scope));

        IReadOnlyList<PeriodicCommand> periodic =
            settings.Commands.GetPeriodicCommands(scope);
        PeriodicCommand only = Assert.Single(periodic);
        Assert.Equal("/mt autopack", only.Command);
        Assert.Equal(TimeSpan.FromMinutes(5), only.Interval);
        Assert.Equal(TimeSpan.Zero, only.OffsetFromMidnight);
    }

    [Fact]
    public void ListValuedNodesAreReplacedWholesale()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        file.SetNodeChildren("AutoTradeAccept/Whitelist", "Name", ["A", "B"]);
        Assert.Equal(["A", "B"], file.GetChildrenInnerTexts("AutoTradeAccept/Whitelist"));

        file.SetNodeChildren("AutoTradeAccept/Whitelist", "Name", ["C"]);
        Assert.Equal(["C"], file.GetChildrenInnerTexts("AutoTradeAccept/Whitelist"));
    }

    [Fact]
    public void WritesAreDebouncedIntoOneSave()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        file.PutSetting("Filters/AttackEvades", true);
        file.PutSetting("Filters/DefenseEvades", true);
        file.PutSetting("Filters/AttackResists", true);
        Assert.Equal(0, storage.WriteCount);
        Assert.True(file.HasPendingSave);

        // Short of the quiet period, nothing is written.
        file.Tick(0.1d);
        Assert.Equal(0, storage.WriteCount);

        file.Tick(0.2d);
        Assert.Equal(1, storage.WriteCount);
        Assert.False(file.HasPendingSave);
    }

    [Fact]
    public void FlushWritesWhatTheDebounceStillOwes()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        file.PutSetting("Filters/AttackEvades", true);
        file.Flush();

        Assert.Equal(1, storage.WriteCount);
        Assert.False(file.HasPendingSave);

        // A second flush with nothing pending does not write again.
        file.Flush();
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public void ACorruptFileFallsBackToAnEmptyDocument()
    {
        var storage = new MemoryStorage();
        storage.Seed(SettingsFile.StorageKey, "<Mag-Tools><broken>");

        var file = new SettingsFile(storage);
        Assert.True(file.GetSetting("ManaManagement/AutoRecharge", true));
    }

    [Fact]
    public void TwoInstancesOverOneStorageBothKeepTheirWrites()
    {
        // Two live plugin sessions (or a settings panel and a headless bot)
        // sharing one storage backend. Without a reload-before-write, the
        // second Flush would serialize its own stale in-memory copy and wipe
        // out the first instance's already-persisted change.
        var storage = new MemoryStorage();
        var first = new SettingsFile(storage);
        var second = new SettingsFile(storage);

        first.PutSetting("Filters/AttackEvades", true);
        first.Flush();

        second.PutSetting("Filters/DefenseEvades", true);
        second.Flush();

        var final = new SettingsFile(storage);
        Assert.True(final.GetSetting("Filters/AttackEvades", false));
        Assert.True(final.GetSetting("Filters/DefenseEvades", false));
    }

    [Fact]
    public void FlushPreservesUnrelatedNodesWrittenAfterConstruction()
    {
        // The scenario the reload-before-write exists for: this instance was
        // constructed before another session (or a hand-imported file) added
        // Misc/WindowPositions; without a reload, this instance's Flush would
        // never have seen it and would overwrite it away.
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        var writtenElsewhere = new SettingsFile(storage);
        writtenElsewhere.SetNodeChildren(
            "Misc/WindowPositions", "Window", ["Main:10,10", "Hud:20,20"]);
        writtenElsewhere.Flush();

        file.PutSetting("Filters/AttackEvades", true);
        file.Flush();

        var final = new SettingsFile(storage);
        Assert.Equal(
            ["Main:10,10", "Hud:20,20"],
            final.GetChildrenInnerTexts("Misc/WindowPositions"));
        Assert.True(final.GetSetting("Filters/AttackEvades", false));
    }

    [Fact]
    public void ANodeFetchedBeforeATickDrivenFlushStillWritesThroughAfterIt()
    {
        // The P1 review finding: Flush used to reload storage into a brand
        // new XDocument and swap it in wholesale, so any XElement a caller
        // held from GetNode (ScopedCommandStore does this) was silently
        // detached — later writes to it never reached storage again. Flush
        // must instead merge the reload into the existing document.
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        XElement node = file.GetNode("ChatLogger/Group1/Custom", createIfMissing: true)!;
        node.Value = "first";
        file.MarkDirty("ChatLogger/Group1/Custom");

        file.Tick(SettingsFile.SaveDebounce.TotalSeconds);
        Assert.Equal(1, file.SaveCount);
        Assert.True(node.Parent is not null, "the node must still be attached");

        // Write through the SAME element reference after the flush.
        node.Value = "second";
        file.MarkDirty("ChatLogger/Group1/Custom");
        file.Tick(SettingsFile.SaveDebounce.TotalSeconds);

        var reloaded = new SettingsFile(storage);
        Assert.Equal("second", reloaded.GetSetting("ChatLogger/Group1/Custom", string.Empty));
    }

    [Fact]
    public void FlushPreservesElementIdentityForAnUntouchedSubtree()
    {
        // SyncChildrenWholesale's real guarantee: a caller holding an
        // XElement from GetNode() for a subtree NOTHING dirtied keeps that
        // exact same instance across a flush that reloads and merges in a
        // sibling's concurrent write — it must not be silently replaced by a
        // clone just because its parent's wholesale-sync path ran.
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);

        XElement untouched = file.GetNode("Misc/WindowPositions", createIfMissing: true)!;
        untouched.Add(new XElement("Window", "Main:10,10"));
        file.MarkDirty("Misc/WindowPositions");
        file.Flush();

        var writtenElsewhere = new SettingsFile(storage);
        writtenElsewhere.PutSetting("Filters/AttackEvades", true);
        writtenElsewhere.Flush();

        // Dirty something ELSE in `file`'s own document so its next Flush()
        // reloads and merges — Misc/WindowPositions is untouched by this
        // instance the whole time.
        file.PutSetting("Filters/DefenseEvades", true);
        file.Flush();

        Assert.True(untouched.Parent is not null, "the node must still be attached");
        Assert.Equal(
            "Main:10,10",
            file.GetChildrenInnerTexts("Misc/WindowPositions")[0]);

        // The instance itself still resolves to the SAME element GetNode()
        // handed out earlier.
        XElement again = file.GetNode("Misc/WindowPositions", createIfMissing: false)!;
        Assert.Same(untouched, again);
    }

    [Fact]
    public void FlushLeavesTheDocumentDirtyWhenStorageIsUnavailable()
    {
        var storage = new MemoryStorage { IsAvailable = false };
        var file = new SettingsFile(storage);

        file.PutSetting("Filters/AttackEvades", true);
        file.Flush();

        Assert.Equal(0, storage.WriteCount);
        Assert.True(file.HasPendingSave);

        storage.IsAvailable = true;
        file.Flush();

        Assert.Equal(1, storage.WriteCount);
        Assert.False(file.HasPendingSave);
    }

    [Fact]
    public void FlushCopiesTheLeafValueWhenAnUntouchedNodeChangedFromContainerToLeaf()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);
        XElement node = file.GetNode("Misc/WindowPositions", createIfMissing: true)!;
        node.Add(new XElement("Window", "Main:10,10"));
        file.MarkDirty("Misc/WindowPositions");
        file.Flush();

        var writtenElsewhere = new SettingsFile(storage);
        writtenElsewhere.PutSetting("Misc/WindowPositions", "flat-value");
        writtenElsewhere.Flush();

        // `file` never touches Misc/WindowPositions itself again — only
        // Filters/AttackEvades is dirty for this flush.
        file.PutSetting("Filters/AttackEvades", true);
        file.Flush();

        // The held reference sees the new leaf value in place...
        Assert.Equal("flat-value", node.Value);
        Assert.Empty(node.Elements());
        // ...and so does a fresh read.
        Assert.Equal("flat-value", file.GetSetting("Misc/WindowPositions", string.Empty));
    }

    [Fact]
    public void FlushRebuildsChildrenWhenAnUntouchedNodeChangedFromLeafToContainer()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);
        file.PutSetting("Misc/WindowPositions", "flat-value");
        file.Flush();

        var writtenElsewhere = new SettingsFile(storage);
        writtenElsewhere.SetNodeChildren(
            "Misc/WindowPositions", "Window", ["Main:10,10", "Hud:20,20"]);
        writtenElsewhere.Flush();

        file.PutSetting("Filters/AttackEvades", true);
        file.Flush();

        Assert.Equal(
            ["Main:10,10", "Hud:20,20"],
            file.GetChildrenInnerTexts("Misc/WindowPositions"));
    }
}

public sealed class SettingTests
{
    [Fact]
    public void AssigningTheSameValueDoesNotRaiseChanged()
    {
        var file = new SettingsFile(new MemoryStorage());
        var setting = new Setting<bool>(file, "Filters/AttackEvades", "Attack Evades");
        int changes = 0;
        setting.Changed += _ => changes++;

        setting.Value = false;
        Assert.Equal(0, changes);

        setting.Value = true;
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheTypeErasedFaceParsesAndFormats()
    {
        var file = new SettingsFile(new MemoryStorage());
        ISetting setting = new Setting<int>(file, "Misc/OutputTargetWindow", "Output Window", 1);

        Assert.Equal("int", setting.TypeName);
        Assert.Equal("1", setting.ValueText);
        Assert.True(setting.TrySetFromText("3"));
        Assert.Equal("3", setting.ValueText);
        Assert.False(setting.TrySetFromText("not a number"));
        Assert.Equal("3", setting.ValueText);
    }
}

public sealed class SettingsManagerTests
{
    private static SettingsManager NewManager()
        => new(new SettingsFile(new MemoryStorage()));

    [Fact]
    public void EverySettingIsReachableAsGroupDotField()
    {
        SettingsManager settings = NewManager();

        Assert.NotNull(settings.Find("Filters.AttackEvades"));
        Assert.NotNull(settings.Find("filters.attackevades"));
        Assert.NotNull(settings.Find("Misc.OutputTargetWindow"));
        Assert.Null(settings.Find("Misc.NoSuchSetting"));
    }

    [Fact]
    public void TheSettingsNotApplicableToOpenAcAreAbsent()
    {
        SettingsManager settings = NewManager();

        foreach (string name in new[]
        {
            "Misc.RemoveWindowFrame",
            "Misc.MaximizeChatOnLogin",
            "Misc.NoFocusFPS",
            "Misc.MaxFPS",
            "Misc.WindowPositions",
        })
        {
            Assert.Null(settings.Find(name));
        }
    }

    [Fact]
    public void DefaultsMatchTheOriginalSettingTable()
    {
        SettingsManager settings = NewManager();

        Assert.True(settings.ManaManagement.AutoRecharge.Value);
        Assert.True(settings.AutoBuySell.Enabled.Value);
        Assert.False(settings.AutoBuySell.TestMode.Value);
        Assert.True(settings.Looting.AutoLootChests.Value);
        Assert.False(settings.Looting.LootSalvage.Value);
        Assert.True(settings.ItemInfoOnIdent.Enabled.Value);
        Assert.True(settings.CorpseTracker.TrackPermittedCorpses.Value);
        Assert.False(settings.PlayerTracker.Enabled.Value);
        Assert.True(settings.ChatLogger.Group1.Tells.Value);
        Assert.False(settings.ChatLogger.Group2.Tells.Value);
        Assert.Equal(1, settings.Misc.OutputTargetWindow.Value);
        Assert.False(settings.Filters.AttackEvades.Value);
    }

    [Fact]
    public void TurningOnAChildTurnsOnItsParent()
    {
        SettingsManager settings = NewManager();
        settings.AutoBuySell.Enabled.Value = false;

        settings.AutoBuySell.TestMode.Value = true;
        Assert.True(settings.AutoBuySell.Enabled.Value);

        settings.ItemInfoOnIdent.Enabled.Value = false;
        settings.ItemInfoOnIdent.AutoClipboard.Value = true;
        Assert.True(settings.ItemInfoOnIdent.Enabled.Value);
    }

    [Fact]
    public void TurningOffAChildLeavesItsParentAlone()
    {
        SettingsManager settings = NewManager();
        settings.AutoBuySell.TestMode.Value = true;
        Assert.True(settings.AutoBuySell.Enabled.Value);

        settings.AutoBuySell.TestMode.Value = false;
        Assert.True(settings.AutoBuySell.Enabled.Value);
    }

    [Fact]
    public void LootMyCorpsesKeepsTheOriginalSingularElementName()
    {
        SettingsManager settings = NewManager();
        Assert.Equal("Looting/AutoLootMyCorpse", settings.Looting.AutoLootMyCorpses.XPath);
    }

    [Fact]
    public void AllListsGroupsInAFixedExplicitOrder()
    {
        // Not GetProperties() order: that is a runtime metadata-layout
        // artifact, not a documented contract. Reflection order happens to
        // match declaration order today, but nothing guarantees it stays
        // that way; this test pins the explicit order instead.
        SettingsManager settings = NewManager();

        string[] groupOrder = [.. settings.All
            .Select(descriptor => descriptor.GroupName)
            .Distinct()];

        Assert.Equal(
            [
                "ManaManagement", "AutoBuySell", "AutoTradeAdd",
                "AutoTradeAccept", "Looting", "Tinkering",
                "InventoryManagement", "ItemInfoOnIdent", "CombatTracker",
                "CorpseTracker", "PlayerTracker", "ChatLogger", "Misc",
                "Filters",
            ],
            groupOrder);

        // Every group's settings stay contiguous (no interleaving), which is
        // what "explicit order" promises callers of Find() and /mt opt list.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        foreach (SettingDescriptor descriptor in settings.All)
        {
            if (descriptor.GroupName != previous)
            {
                Assert.True(
                    seen.Add(descriptor.GroupName),
                    $"Group {descriptor.GroupName} is not contiguous.");
                previous = descriptor.GroupName;
            }
        }
    }
}

public sealed class OptionListViewModelTests
{
    [Fact]
    public void ChecksReflectASettingChangedFromOutsideTheList()
    {
        var settings = new SettingsManager(new SettingsFile(new MemoryStorage()));
        settings.ItemInfoOnIdent.Enabled.Value = false;

        var options = new OptionListViewModel(
        [
            settings.ItemInfoOnIdent.Enabled,
            settings.ItemInfoOnIdent.AutoClipboard,
        ]);

        Assert.Equal([false, false], options.Checks);

        // AutoClipboard's parent-forcing wiring flips Enabled, not the list
        // itself. Without a Changed subscription, Checks stays the stale
        // snapshot taken at construction.
        settings.ItemInfoOnIdent.AutoClipboard.Value = true;

        Assert.Equal([true, true], options.Checks);
    }

    [Fact]
    public void DisposeUnsubscribesFromEverySetting()
    {
        var settings = new SettingsManager(new SettingsFile(new MemoryStorage()));
        settings.ItemInfoOnIdent.Enabled.Value = false;

        var options = new OptionListViewModel(
        [
            settings.ItemInfoOnIdent.Enabled,
            settings.ItemInfoOnIdent.AutoClipboard,
        ]);

        options.Dispose();

        // Without the subscription, Checks stays the snapshot from before
        // Dispose — a settings change no longer reaches a disposed list.
        settings.ItemInfoOnIdent.AutoClipboard.Value = true;

        Assert.Equal([false, false], options.Checks);
    }
}

public sealed class SettingsScopeTests
{
    [Fact]
    public void CharacterScopeUsesTheUnderscorePrefixedEncodedPath()
    {
        Assert.Equal(
            "_acct_Frostfell/MyChar",
            SettingsScope.Character("acct", "Frostfell", "MyChar"));
    }

    [Fact]
    public void ServerScopeUsesTheUnderscorePrefixedEncodedName()
    {
        Assert.Equal("_Frostfell", SettingsScope.Server("Frostfell"));
    }

    [Fact]
    public void NamesThatAreNotLegalXmlNamesAreEncoded()
    {
        // A space is not legal in an element name, so it becomes _x0020_.
        string path = SettingsScope.Character("acct", "Dark Majesty", "My Char");
        Assert.Equal("_acct_Dark_x0020_Majesty/My_x0020_Char", path);
    }

    [Fact]
    public void AScopedCommandListRoundTrips()
    {
        var storage = new MemoryStorage();
        var file = new SettingsFile(storage);
        var store = new ScopedCommandStore(file);
        string scope = SettingsScope.Character("acct", "Dark Majesty", "My Char");

        store.SetOnLoginCommands(scope, ["/mt autopack", "/vt start"]);
        store.SetPeriodicCommands(scope,
        [
            new PeriodicCommand("/mt opt list", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2)),
        ]);
        file.Flush();

        var reloaded = new ScopedCommandStore(new SettingsFile(storage));
        Assert.Equal(["/mt autopack", "/vt start"], reloaded.GetOnLoginCommands(scope));

        PeriodicCommand only = Assert.Single(reloaded.GetPeriodicCommands(scope));
        Assert.Equal("/mt opt list", only.Command);
        Assert.Equal(TimeSpan.FromMinutes(5), only.Interval);
        Assert.Equal(TimeSpan.FromMinutes(2), only.OffsetFromMidnight);
    }
}
