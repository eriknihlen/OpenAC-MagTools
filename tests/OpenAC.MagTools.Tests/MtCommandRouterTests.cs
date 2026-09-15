using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests;

public sealed class MtCommandRouterTests
{
    private readonly FakeHost _host = new();
    private readonly MtCommandRouter _router;

    public MtCommandRouterTests()
    {
        var settings = new SettingsManager(new SettingsFile(_host.Storage));
        _router = new MtCommandRouter(_host, new ChatOutput(_host), settings);
    }

    private IReadOnlyList<string> Chat => _host.ChatLines;

    private static string Line(string text) => ChatOutput.Prefix + text;

    // ---- shape ---------------------------------------------------------------

    [Fact]
    public void TestIsANoOpThatStillCountsAsHandled()
    {
        Assert.True(_router.Execute("test"));
        Assert.Empty(Chat);
    }

    [Theory]
    [InlineData("send enter")]
    [InlineData("click ok")]
    [InlineData("jump 500")]
    [InlineData("movement w 200")]
    [InlineData("quit")]
    [InlineData("get xy")]
    [InlineData("client minimize")]
    [InlineData("fellow create Bob")]
    public void TheWin32CommandsReportThatTheyDoNotApply(string command)
    {
        Assert.True(_router.Execute(command));
        Assert.Contains(Chat, line => line.Contains(
            "is not applicable in OpenAC", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryNotApplicableEntryIsRecognised()
    {
        foreach (string command in MtCommandRouter.NotApplicable)
        {
            var host = new FakeHost();
            var router = new MtCommandRouter(
                host,
                new ChatOutput(host),
                new SettingsManager(new SettingsFile(host.Storage)));

            Assert.True(router.Execute(command));
            Assert.Equal(
                Line(command + " is not applicable in OpenAC"),
                Assert.Single(host.ChatLines));
        }
    }

    [Theory]
    [InlineData("trade accept")]
    [InlineData("vendor buy")]
    [InlineData("vendor addbuy Peerless Mana Potion 5")]
    public void TradeAndVendorCommandsNameTheMissingApi(string command)
    {
        Assert.True(_router.Execute(command));
        Assert.Contains(Chat, line => line.Contains(
            "requires the trade/vendor API (coming)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("logoff")]
    [InlineData("logout")]
    public void LogoutSaysItIsNotAvailableYet(string command)
    {
        Assert.True(_router.Execute(command));
        Assert.Equal(Line("logout is not available yet"), Assert.Single(Chat));
    }

    [Fact]
    public void FellowQuitIsNotSwallowedByTheNotApplicableQuit()
    {
        Assert.True(_router.Execute("fellow quit"));
        Assert.Equal(["quit:False"], _host.Automation.Fellowship.Calls);
    }

    // ---- native commands -----------------------------------------------------

    [Fact]
    public void FaceSendsTheHeading()
    {
        Assert.True(_router.Execute("face 180"));
        Assert.Equal([180f], _host.Automation.Navigation.Headings);
    }

    [Fact]
    public void FaceRejectsANonNumericHeading()
    {
        Assert.False(_router.Execute("face north"));
        Assert.Empty(_host.Automation.Navigation.Headings);
    }

    [Theory]
    [InlineData("fellow open", "setopen:True")]
    [InlineData("fellow close", "setopen:False")]
    [InlineData("fellow disband", "quit:True")]
    public void FellowshipCommandsMapToTheAutomation(string command, string expected)
    {
        Assert.True(_router.Execute(command));
        Assert.Equal([expected], _host.Automation.Fellowship.Calls);
    }

    [Fact]
    public void FellowRecruitPicksTheClosestObjectWithThatName()
    {
        _host.Automation.Objects.Objects.AddRange(
        [
            FakeObjects.Landscape(10u, "Bob", PluginObjectClass.Player, 30d),
            FakeObjects.Landscape(11u, "Bob", PluginObjectClass.Player, 5d),
        ]);

        Assert.True(_router.Execute("fellow recruit bob"));
        Assert.Equal(["recruit:11"], _host.Automation.Fellowship.Calls);
    }

    [Fact]
    public void SelectSearchesInventoryFirst()
    {
        _host.Automation.Items.Owned.Add(FakeItems.Item(5u, "Pyreal"));
        _host.Automation.Objects.Objects.Add(
            FakeObjects.Landscape(9u, "Pyreal", PluginObjectClass.Money, 1d));

        Assert.True(_router.Execute("select pyreal"));
        Assert.Equal(5u, _host.Selection.SelectedObjectId);
    }

    [Fact]
    public void ThePartialFormMatchesASubstringAndThePlainFormDoesNot()
    {
        _host.Automation.Items.Owned.Add(
            FakeItems.Item(7u, "Peerless Mana Potion"));

        Assert.False(_router.Execute("select mana"));
        Assert.Null(_host.Selection.SelectedObjectId);

        Assert.True(_router.Execute("selectp mana"));
        Assert.Equal(7u, _host.Selection.SelectedObjectId);
    }

    [Fact]
    public void AnExactMatchWinsOverASubstringMatch()
    {
        _host.Automation.Items.Owned.AddRange(
        [
            FakeItems.Item(1u, "Mana Stone of Doom"),
            FakeItems.Item(2u, "Mana Stone"),
        ]);

        Assert.True(_router.Execute("selectp mana stone"));
        Assert.Equal(2u, _host.Selection.SelectedObjectId);
    }

    [Fact]
    public void UseRunsTheItem()
    {
        _host.Automation.Items.Owned.Add(FakeItems.Item(3u, "Lockpick"));

        Assert.True(_router.Execute("use lockpick"));
        Assert.Equal(("use", 3u, 0u), Assert.Single(_host.Automation.Items.Calls));
    }

    [Fact]
    public void UseAOnBAppliesTheFirstToTheSecond()
    {
        _host.Automation.Items.Owned.AddRange(
        [
            FakeItems.Item(3u, "Lockpick"),
            FakeItems.Item(4u, "Chest Key"),
        ]);

        Assert.True(_router.Execute("use lockpick on chest key"));
        Assert.Equal(("apply", 3u, 4u), Assert.Single(_host.Automation.Items.Calls));
        Assert.Equal(4u, _host.Selection.SelectedObjectId);
    }

    [Fact]
    public void UseiOnlySearchesInventory()
    {
        _host.Automation.Objects.Objects.Add(
            FakeObjects.Landscape(9u, "Lever", PluginObjectClass.Misc, 1d));

        Assert.False(_router.Execute("usei lever"));
        Assert.Empty(_host.Automation.Items.Calls);

        Assert.True(_router.Execute("usel lever"));
        Assert.Equal(("use", 9u, 0u), Assert.Single(_host.Automation.Items.Calls));
    }

    [Theory]
    [InlineData("closestnpc", PluginObjectClass.Npc)]
    [InlineData("closestvendor", PluginObjectClass.Vendor)]
    [InlineData("closestportal", PluginObjectClass.Portal)]
    public void TheClosestKeywordsResolveByClass(
        string keyword,
        PluginObjectClass objectClass)
    {
        _host.Automation.Objects.Objects.AddRange(
        [
            FakeObjects.Landscape(20u, "Far", objectClass, 50d),
            FakeObjects.Landscape(21u, "Near", objectClass, 2d),
            FakeObjects.Landscape(22u, "Rock", PluginObjectClass.Misc, 1d),
        ]);

        Assert.True(_router.Execute("use " + keyword));
        Assert.Equal(("use", 21u, 0u), Assert.Single(_host.Automation.Items.Calls));
    }

    [Fact]
    public void EquipPicksAnUnequippedItemAndDequipPicksAnEquippedOne()
    {
        _host.Automation.Items.Owned.AddRange(
        [
            FakeItems.Item(1u, "Sword", equippedLocation: 1u),
            FakeItems.Item(2u, "Sword"),
        ]);

        Assert.True(_router.Execute("equip sword"));
        Assert.Equal([2u], _host.Automation.Equipment.Equipped);

        Assert.True(_router.Execute("dequip sword"));
        Assert.Equal(("use", 1u, 0u), Assert.Single(_host.Automation.Items.Calls));
    }

    [Fact]
    public void GiveNeedsBothHalvesOfTheArgument()
    {
        _host.Automation.Items.Owned.Add(FakeItems.Item(3u, "Note"));
        _host.Automation.Objects.Objects.Add(
            FakeObjects.Landscape(8u, "Mule", PluginObjectClass.Player, 2d));

        Assert.False(_router.Execute("give note"));
        Assert.Empty(_host.Automation.Items.Calls);

        Assert.True(_router.Execute("give note to mule"));
        Assert.Equal(("give", 3u, 8u), Assert.Single(_host.Automation.Items.Calls));
    }

    [Fact]
    public void LootOnlySearchesTheOpenContainer()
    {
        _host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Gem"));
        _host.Automation.Loot.Contents.Add(FakeItems.Item(2u, "Gem"));

        Assert.True(_router.Execute("loot gem"));
        Assert.Equal([2u], _host.Automation.Loot.PickedUp);
    }

    [Fact]
    public void DropOnlySearchesInventory()
    {
        _host.Automation.Loot.Contents.Add(FakeItems.Item(2u, "Gem"));
        Assert.False(_router.Execute("drop gem"));

        _host.Automation.Items.Owned.Add(FakeItems.Item(1u, "Gem"));
        Assert.True(_router.Execute("drop gem"));
        Assert.Equal(("drop", 1u, 0u), Assert.Single(_host.Automation.Items.Calls));
    }

    [Theory]
    [InlineData("magic", "mode:Magic")]
    [InlineData("melee", "mode:Melee")]
    [InlineData("missile", "mode:Missile")]
    [InlineData("peace", "mode:Peace")]
    public void CombatStateSwitchesMode(string argument, string expected)
    {
        Assert.True(_router.Execute("combatstate " + argument));
        Assert.Equal([expected], _host.Automation.Combat.Calls);
    }

    [Fact]
    public void CombatStateRejectsAnUnknownMode()
    {
        Assert.False(_router.Execute("combatstate dance"));
        Assert.Empty(_host.Automation.Combat.Calls);
    }

    [Fact]
    public void AttackMeleeEntersMeleeFirst()
    {
        Assert.True(_router.Execute("attack_melee"));
        Assert.Equal(["mode:Melee"], _host.Automation.Combat.Calls);
    }

    [Fact]
    public void AttackMeleeInMeleeModeSelectsTheNearestMonster()
    {
        _host.Automation.Combat.Snapshot = new PluginCombatSnapshot(
            0u, PluginCombatMode.Melee, PluginAttackHeight.Medium, 1f, 0f,
            false, false, false, false);
        _host.Automation.Objects.Objects.AddRange(
        [
            FakeObjects.Landscape(30u, "Drudge", PluginObjectClass.Monster, 40d),
            FakeObjects.Landscape(31u, "Drudge", PluginObjectClass.Monster, 4d),
        ]);

        Assert.True(_router.Execute("attack_melee closest"));
        Assert.Equal(["attack:31"], _host.Automation.Combat.Calls);
        Assert.Equal(31u, _host.Selection.SelectedObjectId);
    }

    [Fact]
    public void CastAcceptsASpellIdAndASpellName()
    {
        _host.Automation.Spells.KnownSelfBuffs =
            [FakeSpellCatalog.Spell(1234u, "Strength Self VI")];

        Assert.True(_router.Execute("cast 1234"));
        Assert.Equal((1234u, 0u), _host.Automation.Magic.Casts[0]);

        Assert.True(_router.Execute("cast strength self vi"));
        Assert.Equal((1234u, 0u), _host.Automation.Magic.Casts[1]);
    }

    [Fact]
    public void CastOnATargetResolvesTheTargetFromTheLandscape()
    {
        _host.Automation.Spells.KnownAttackSpells =
            [FakeSpellCatalog.Spell(99u, "Flame Bolt VI")];
        _host.Automation.Objects.Objects.Add(
            FakeObjects.Landscape(40u, "Drudge", PluginObjectClass.Monster, 8d));

        Assert.True(_router.Execute("castp flame bolt on drudge"));
        Assert.Equal((99u, 40u), Assert.Single(_host.Automation.Magic.Casts));
    }

    [Fact]
    public void AutopackSaysItIsNotAvailableYet()
    {
        Assert.True(_router.Execute("autopack"));
        Assert.Equal(Line("autopack is not available yet"), Assert.Single(Chat));
    }

    [Fact]
    public void DumpSpellsWritesTheKnownListsToStorage()
    {
        _host.Automation.Spells.KnownSelfBuffs =
            [FakeSpellCatalog.Spell(1u, "Strength Self VI")];
        _host.Automation.Spells.KnownAttackSpells =
            [FakeSpellCatalog.Spell(2u, "Flame Bolt VI")];

        Assert.True(_router.Execute("dumpspells"));

        string? csv = _host.Storage.ReadText("mt spelldump.txt");
        Assert.NotNull(csv);
        Assert.Contains("1,Strength Self VI", csv, StringComparison.Ordinal);
        Assert.Contains("2,Flame Bolt VI", csv, StringComparison.Ordinal);
        Assert.Equal(Line("Spell dump written to: mt spelldump.txt"), Assert.Single(Chat));
    }

    [Fact]
    public void AnUnknownCommandIsReportedAndNotEaten()
    {
        Assert.False(_router.Execute("wibble"));
        Assert.Equal(Line("Unknown command: wibble"), Assert.Single(Chat));
    }
}

public sealed class MtOptionCommandTests
{
    private readonly FakeHost _host = new();
    private readonly SettingsManager _settings;
    private readonly MtCommandRouter _router;

    public MtOptionCommandTests()
    {
        _settings = new SettingsManager(new SettingsFile(_host.Storage));
        _router = new MtCommandRouter(_host, new ChatOutput(_host), _settings);
    }

    private IReadOnlyList<string> Chat => _host.ChatLines;

    [Fact]
    public void ListPrintsEverySettingWithItsType()
    {
        Assert.True(_router.Execute("opt list"));

        Assert.Equal(_settings.All.Count, Chat.Count);
        Assert.Contains(
            ChatOutput.Prefix + "Filters.AttackEvades <bool>",
            Chat);
        Assert.Contains(
            ChatOutput.Prefix + "Misc.OutputTargetWindow <int>",
            Chat);
    }

    [Fact]
    public void GetPrintsTheClassAndTheFieldNotTheFieldTwice()
    {
        Assert.True(_router.Execute("opt get Filters.AttackEvades"));
        Assert.Equal(
            ChatOutput.Prefix + "Filters.AttackEvades = False",
            Assert.Single(Chat));
    }

    [Fact]
    public void GetIsCaseInsensitive()
    {
        Assert.True(_router.Execute("opt get filters.attackevades"));
        Assert.Equal(
            ChatOutput.Prefix + "Filters.AttackEvades = False",
            Assert.Single(Chat));
    }

    [Fact]
    public void GetReportsAnUnknownSetting()
    {
        Assert.False(_router.Execute("opt get Misc.Nonsense"));
        Assert.Equal(
            ChatOutput.Prefix + "Failed to Get Misc.Nonsense",
            Assert.Single(Chat));
    }

    [Fact]
    public void SetParsesTheValueForTheSettingsType()
    {
        Assert.True(_router.Execute("opt set Filters.AttackEvades true"));
        Assert.True(_settings.Filters.AttackEvades.Value);

        Assert.True(_router.Execute("opt set Misc.OutputTargetWindow 2"));
        Assert.Equal(2, _settings.Misc.OutputTargetWindow.Value);
    }

    [Fact]
    public void SetRejectsAValueOfTheWrongType()
    {
        Assert.False(_router.Execute("opt set Misc.OutputTargetWindow left"));
        Assert.Equal(1, _settings.Misc.OutputTargetWindow.Value);
        Assert.Contains(
            ChatOutput.Prefix + "Failed to Set Misc.OutputTargetWindow to left",
            Chat);
    }

    [Fact]
    public void RememberAndRestoreRoundTripOneValue()
    {
        Assert.True(_router.Execute("opt remember Filters.AttackEvades"));
        Assert.True(_router.Execute("opt set Filters.AttackEvades true"));
        Assert.True(_settings.Filters.AttackEvades.Value);

        Assert.True(_router.Execute("opt restore Filters.AttackEvades"));
        Assert.False(_settings.Filters.AttackEvades.Value);
    }

    [Fact]
    public void RestoreWithoutARememberedValueFails()
    {
        Assert.False(_router.Execute("opt restore Filters.AttackEvades"));
        Assert.Equal(
            ChatOutput.Prefix + "Failed to Restore Filters.AttackEvades",
            Assert.Single(Chat));
    }
}

public sealed class VendorCommandArgumentTests
{
    [Fact]
    public void ATrailingCountIsParsedAndStrippedFromTheName()
    {
        Assert.Equal(
            ("Peerless Mana Potion", 5),
            VendorCommandArguments.ParseAddBuy("Peerless Mana Potion 5"));
    }

    [Fact]
    public void NoCountMeansOne()
    {
        Assert.Equal(
            ("Peerless Mana Potion", 1),
            VendorCommandArguments.ParseAddBuy("Peerless Mana Potion"));
    }

    [Fact]
    public void ASingleTokenIsAlwaysTheName()
    {
        Assert.Equal(("42", 1), VendorCommandArguments.ParseAddBuy("42"));
    }

    [Fact]
    public void ANonNumericTailIsPartOfTheName()
    {
        Assert.Equal(
            ("Mana Stone", 1),
            VendorCommandArguments.ParseAddBuy("Mana Stone"));
    }

    [Fact]
    public void AnEmptyArgumentYieldsAnEmptyName()
    {
        Assert.Equal((string.Empty, 1), VendorCommandArguments.ParseAddBuy("  "));
    }
}
