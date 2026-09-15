using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

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
