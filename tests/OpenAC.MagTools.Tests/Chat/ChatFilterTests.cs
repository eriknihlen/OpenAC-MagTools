using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Chat;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Chat;

/// <summary>
/// One test per <c>Filters/*</c> rule (29 rules total): a positive case that
/// must be suppressed once its setting is on, and a negative case (the
/// setting left off) that must pass through untouched. Lines are built in the
/// host's structured shape via <see cref="ChatFixtures"/> — explicit
/// Kind/Sender/SenderObjectId/ChannelName — never retail's composed display
/// text, which only the legacy-import tests use.
/// </summary>
public sealed class ChatFilterTests
{
    private readonly FakeHost _host = new();
    private readonly SettingsManager _settings;
    private readonly ChatFilter _filter;

    public ChatFilterTests()
    {
        _settings = new SettingsManager(new SettingsFile(_host.Storage));
        _filter = new ChatFilter(_host, _settings);
        _filter.Enable();
    }

    private bool Suppressed(PluginChatMessage message)
        => !_host.Automation.Chat.Delivered.Any(
            delivered => delivered.Sequence == message.Sequence);

    /// <summary>A bare system/combat-shaped line — never chat, no speaker.</summary>
    private bool SuppressedSystemText(string text) => Suppressed(_host.System(text));

    private void AddWorldObject(uint objectId, string name, PluginObjectClass objectClass)
        => _host.Automation.Objects.Objects.Add(
            new PluginWorldObject(objectId, 0u, name, objectClass, 0u, 0u, 0u));

    // ---- 1. AttackEvades -------------------------------------------------

    [Fact]
    public void AttackEvadesSuppressesWhenOn()
    {
        Assert.False(SuppressedSystemText("Ruschk Sadist evaded your attack."));
        _settings.Filters.AttackEvades.Value = true;
        Assert.True(SuppressedSystemText("Ruschk Sadist evaded your attack."));
    }

    [Fact]
    public void AttackEvadesDoesNotEatARealChatLineWithTheSamePhrase()
    {
        // The !isChat gate: retail speech containing the phrase is still
        // speech, not the system evade line.
        _settings.Filters.AttackEvades.Value = true;
        Assert.False(Suppressed(_host.Say("Bob", "he evaded your attack.", mine: true)));
    }

    // ---- 2. DefenseEvades --------------------------------------------------

    [Fact]
    public void DefenseEvadesSuppressesWhenOn()
    {
        Assert.False(SuppressedSystemText("You evaded Ruschk Sadist!"));
        _settings.Filters.DefenseEvades.Value = true;
        Assert.True(SuppressedSystemText("You evaded Ruschk Sadist!"));
    }

    [Fact]
    public void DefenseEvadesDoesNotEatARealChatLineWithTheSamePhrase()
    {
        _settings.Filters.DefenseEvades.Value = true;
        Assert.False(Suppressed(_host.Say("Bob", "You evaded that one!", mine: true)));
    }

    // ---- 3. AttackResists --------------------------------------------------

    [Fact]
    public void AttackResistsSuppressesWhenOn()
    {
        Assert.False(SuppressedSystemText("Sentient Crystal Shard resists your spell"));
        _settings.Filters.AttackResists.Value = true;
        Assert.True(SuppressedSystemText("Sentient Crystal Shard resists your spell"));
    }

    // ---- 4. DefenseResists -------------------------------------------------

    [Fact]
    public void DefenseResistsSuppressesWhenOn()
    {
        string text = "You resist the spell cast by Sentient Crystal Shard";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.DefenseResists.Value = true;
        Assert.True(SuppressedSystemText(text));
        Assert.True(SuppressedSystemText(
            "Ruschk Warlord tried to cast a spell on you, but was too far away!"));
    }

    // ---- 5. NPKFails --------------------------------------------------------

    [Fact]
    public void NpkFailsSuppressesWhenOn()
    {
        string text = "You fail to affect Bob, you are not a player killer!";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.NPKFails.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 6. DirtyFighting ---------------------------------------------------

    [Fact]
    public void DirtyFightingSuppressesWhenOn()
    {
        string text = "Dirty Fighting! Bob delivers a Traumatic Assault to Mob!";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.DirtyFighting.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 7. MonsterDeaths ---------------------------------------------------

    [Fact]
    public void MonsterDeathsSuppressesWhenOn()
    {
        Assert.False(SuppressedSystemText("You obliterate Drudge Skulker!"));
        _settings.Filters.MonsterDeaths.Value = true;
        Assert.True(SuppressedSystemText("You obliterate Drudge Skulker!"));
    }

    // ---- 8. SpellCastingMine --------------------------------------------------

    [Fact]
    public void SpellCastingMineSuppressesWhenOn()
    {
        PluginChatMessage message() => _host.Say("Bob", "Zojak arwreth", mine: true);
        Assert.False(Suppressed(message()));
        _settings.Filters.SpellCastingMine.Value = true;
        Assert.True(Suppressed(message()));
    }

    [Fact]
    public void SpellCastingMineLeavesAnotherPlayersCastAlone()
    {
        _settings.Filters.SpellCastingMine.Value = true;
        Assert.False(Suppressed(_host.Say("Bob", "Zojak arwreth", mine: false)));
    }

    [Fact]
    public void SpellCastingMineLeavesATellAndAChannelLineAlone()
    {
        // The original anchored on "You say, \"..."/"<Tell...> says, \"..."
        // only — never a tell or a channel line, even one whose body starts
        // with a spell word.
        _settings.Filters.SpellCastingMine.Value = true;
        Assert.False(Suppressed(_host.YouTell("Bob", "Zojak arwreth")));
        Assert.False(Suppressed(_host.ChannelSay("General", "Bob", "Zojak arwreth", mine: true)));
    }

    // ---- 9. SpellCastingOthers -----------------------------------------------

    [Fact]
    public void SpellCastingOthersSuppressesWhenOn()
    {
        PluginChatMessage message() => _host.Say("Bob", "Malar tuash", mine: false);
        Assert.False(Suppressed(message()));
        _settings.Filters.SpellCastingOthers.Value = true;
        Assert.True(Suppressed(message()));
    }

    [Fact]
    public void SpellCastingOthersLeavesMyOwnCastAlone()
    {
        _settings.Filters.SpellCastingOthers.Value = true;
        Assert.False(Suppressed(_host.Say("Bob", "Malar tuash", mine: true)));
    }

    [Fact]
    public void SpellCastingOthersLeavesATellAndAChannelLineAlone()
    {
        _settings.Filters.SpellCastingOthers.Value = true;
        Assert.False(Suppressed(_host.Tell("Bob", "Malar tuash")));
        Assert.False(Suppressed(_host.ChannelSay("General", "Bob", "Malar tuash")));
    }

    // ---- 10. SpellCastFizzles -------------------------------------------------

    [Fact]
    public void SpellCastFizzlesSuppressesWhenOn()
    {
        Assert.False(SuppressedSystemText("Your spell fizzled."));
        _settings.Filters.SpellCastFizzles.Value = true;
        Assert.True(SuppressedSystemText("Your spell fizzled."));
    }

    // ---- 11. CompUsage ----------------------------------------------------

    [Fact]
    public void CompUsageSuppressesWhenOn()
    {
        string text = "The spell consumed the following components: Herb, Powder";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.CompUsage.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 12. SpellExpires ---------------------------------------------------

    [Fact]
    public void SpellExpiresSuppressesWhenOnExceptTheRareItemException()
    {
        _settings.Filters.SpellExpires.Value = true;
        Assert.True(SuppressedSystemText("Focus Self VI has expired."));

        // The original never filters expiry lines mentioning these three
        // rare-item enchantment names.
        Assert.False(SuppressedSystemText("The spell Spectral Bane on your Coat has expired."));
        Assert.False(SuppressedSystemText("Brilliance VI has expired."));
        Assert.False(SuppressedSystemText("The Prodigal Aegis buff have expired."));
    }

    // ---- 13. HealingKitSuccess ----------------------------------------------

    [Fact]
    public void HealingKitSuccessSuppressesWhenOn()
    {
        string text = "You heal yourself for 88 Health points. "
            + "Your treated Healing Kit has 16 uses left.";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.HealingKitSuccess.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 14. HealingKitFail ---------------------------------------------------

    [Fact]
    public void HealingKitFailSuppressesWhenOn()
    {
        string text = "You fail to heal yourself. Your Treated Healing Kit has 18 uses left.";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.HealingKitFail.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 15. Salvaging ------------------------------------------------------

    [Fact]
    public void SalvagingSuppressesWhenOn()
    {
        string text = "You obtain 9 granite (ws 8.00) using your knowledge of Salvaging";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.Salvaging.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 16. SalvagingFails -----------------------------------------------

    [Fact]
    public void SalvagingFailsSuppressesWhenOn()
    {
        Assert.False(SuppressedSystemText("Salvaging Failed!"));
        _settings.Filters.SalvagingFails.Value = true;
        Assert.True(SuppressedSystemText("Salvaging Failed!"));
        Assert.True(SuppressedSystemText(
            "The following were not suitable for salvaging: Salvaged Sunstone (79)."));
    }

    // ---- 17. AuraOfCraftman -------------------------------------------------

    [Fact]
    public void AuraOfCraftmanSuppressesWhenOn()
    {
        string text =
            "Your Aura of the Craftman augmentation increased your skill by 5!";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.AuraOfCraftman.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 18. ManaStoneUsage -------------------------------------------------

    [Fact]
    public void ManaStoneUsageSuppressesWhenOn()
    {
        string text = "The Mana Stone gives 6,127 points of mana to the following items: ";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.ManaStoneUsage.Value = true;
        Assert.True(SuppressedSystemText(text));
        Assert.True(SuppressedSystemText("The Fez is destroyed."));
    }

    // ---- 19. TradeBuffBotSpam -----------------------------------------------

    [Fact]
    public void TradeBuffBotSpamSuppressesMyOwnLocalSpeechEndingInTheTag()
    {
        PluginChatMessage message() => _host.Say("Bob", "Bob -t-", mine: true);
        Assert.False(Suppressed(message()));
        _settings.Filters.TradeBuffBotSpam.Value = true;
        Assert.True(Suppressed(message()));
    }

    [Fact]
    public void TradeBuffBotSpamSuppressesAnEmoteEndingInTheTag()
    {
        PluginChatMessage message() => _host.Emote("Bob", "waves -b-");
        Assert.False(Suppressed(message()));
        _settings.Filters.TradeBuffBotSpam.Value = true;
        Assert.True(Suppressed(message()));
    }

    [Fact]
    public void TradeBuffBotSpamLeavesAnotherPlayersOrdinarySpeechAlone()
    {
        _settings.Filters.TradeBuffBotSpam.Value = true;
        Assert.False(Suppressed(_host.Say("Bob", "Bob -t-", mine: false)));
    }

    // ---- 20. FailedAssess -----------------------------------------------------

    [Fact]
    public void FailedAssessSuppressesWhenOn()
    {
        Assert.False(SuppressedSystemText("Someone tried and failed to assess you!"));
        _settings.Filters.FailedAssess.Value = true;
        Assert.True(SuppressedSystemText("Someone tried and failed to assess you!"));
    }

    // ---- 21. KillTaskComplete ------------------------------------------------

    [Fact]
    public void KillTaskCompleteSuppressesWhenOn()
    {
        string text = "You have killed 50 Drudge Raveners! Your task is complete!";
        Assert.False(SuppressedSystemText(text));
        _settings.Filters.KillTaskComplete.Value = true;
        Assert.True(SuppressedSystemText(text));
    }

    // ---- 22. VendorTells ------------------------------------------------------

    [Fact]
    public void VendorTellsSuppressesWhenOn()
    {
        AddWorldObject(1u, "Larry", PluginObjectClass.Vendor);
        PluginChatMessage message() => _host.Tell("Larry", "Buy something!", fromObjectId: 1u);
        Assert.False(Suppressed(message()));
        _settings.Filters.VendorTells.Value = true;
        Assert.True(Suppressed(message()));
    }

    [Fact]
    public void VendorTellsLeavesATellFromAMonsterAlone()
    {
        AddWorldObject(1u, "Larry", PluginObjectClass.Monster);
        _settings.Filters.VendorTells.Value = true;
        Assert.False(Suppressed(_host.Tell("Larry", "Grr.", fromObjectId: 1u)));
    }

    [Fact]
    public void VendorTellsLeavesMyOwnSentTellAlone()
    {
        AddWorldObject(1u, "Larry", PluginObjectClass.Vendor);
        _settings.Filters.VendorTells.Value = true;
        Assert.False(Suppressed(_host.YouTell("Larry", "Buy something!")));
    }

    // ---- 23. MonsterTell -----------------------------------------------------

    [Fact]
    public void MonsterTellSuppressesWhenOn()
    {
        AddWorldObject(1u, "Ruschk Warlord", PluginObjectClass.Monster);
        PluginChatMessage message() =>
            _host.Tell("Ruschk Warlord", "You will die!", fromObjectId: 1u);
        Assert.False(Suppressed(message()));
        _settings.Filters.MonsterTell.Value = true;
        Assert.True(Suppressed(message()));
    }

    // ---- 24. NpcChatter -----------------------------------------------------

    [Fact]
    public void NpcChatterSuppressesWhenOn()
    {
        AddWorldObject(1u, "Town Crier", PluginObjectClass.Npc);
        PluginChatMessage message() => _host.Say("Town Crier", "Hear ye!", fromObjectId: 1u);
        Assert.False(Suppressed(message()));
        _settings.Filters.NpcChatter.Value = true;
        Assert.True(Suppressed(message()));
    }

    [Fact]
    public void NpcChatterLeavesMyOwnSpeechAlone()
    {
        AddWorldObject(1u, "+Acdream", PluginObjectClass.Npc);
        _settings.Filters.NpcChatter.Value = true;
        Assert.False(Suppressed(_host.Say("+Acdream", "Hear ye!", mine: true)));
    }

    [Fact]
    public void NpcChatterFallsBackToNameLookupForZeroIdLocalSpeech()
    {
        // A local-speech line with no SenderObjectId at all (id 0, not the
        // local player) is the by-name fallback's actual reason to exist.
        AddWorldObject(1u, "Ghost Voice", PluginObjectClass.Npc);
        _settings.Filters.NpcChatter.Value = true;
        Assert.True(Suppressed(_host.Say("Ghost Voice", "Hear ye!", fromObjectId: 0u)));
    }

    [Fact]
    public void NpcChatterNameCacheIsInvalidatedOnLogoff()
    {
        _settings.Filters.NpcChatter.Value = true;

        // First lookup with no matching world object: caches "not found".
        Assert.False(Suppressed(_host.Say("Ghost Voice", "Hear ye!", fromObjectId: 0u)));

        // A stale cache would keep reporting "not found" for the same name
        // even after the object shows up (a fresh session's world state).
        AddWorldObject(1u, "Ghost Voice", PluginObjectClass.Npc);
        _filter.OnLogoff();

        Assert.True(Suppressed(_host.Say("Ghost Voice", "Hear ye!", fromObjectId: 0u)));
    }

    // ---- 25. MasterArbitratorSpam ---------------------------------------------

    [Fact]
    public void MasterArbitratorSpamSuppressesTheDocumentedLines()
    {
        PluginChatMessage message() => _host.Tell(
            "Master Arbitrator",
            "You shall be known to all as a Colosseum Champion!",
            fromObjectId: 1u);
        Assert.False(Suppressed(message()));
        _settings.Filters.MasterArbitratorSpam.Value = true;
        Assert.True(Suppressed(message()));
    }

    [Fact]
    public void MasterArbitratorSpamLeavesOrdinaryArbitratorTellsAlone()
    {
        // The register's own comment: these are deliberately NOT filtered.
        _settings.Filters.MasterArbitratorSpam.Value = true;
        Assert.False(Suppressed(_host.Tell(
            "Master Arbitrator", "Prepare your fellowship ahead of time.", fromObjectId: 1u)));
    }

    // ---- 26. AllMasterArbitratorChat -------------------------------------------

    [Fact]
    public void AllMasterArbitratorChatSuppressesEveryArbitratorLine()
    {
        PluginChatMessage message() =>
            _host.Tell("Master Arbitrator", "Anything at all.", fromObjectId: 1u);
        Assert.False(Suppressed(message()));
        _settings.Filters.AllMasterArbitratorChat.Value = true;
        Assert.True(Suppressed(message()));
    }

    // ---- 27. StatusTextYoureTooBusy -------------------------------------------

    [Fact]
    public void StatusTextYoureTooBusySuppressesWhenOn()
    {
        Assert.False(Suppressed(_host.Status("You're too busy!")));
        _settings.Filters.StatusTextYoureTooBusy.Value = true;
        Assert.True(Suppressed(_host.Status("You're too busy!")));
    }

    // ---- 28. StatusTextCasting -----------------------------------------------

    [Fact]
    public void StatusTextCastingSuppressesWhenOn()
    {
        Assert.False(Suppressed(_host.Status("Casting .....")));
        _settings.Filters.StatusTextCasting.Value = true;
        Assert.True(Suppressed(_host.Status("Casting .....")));
    }

    // ---- 29. StatusTextAll -----------------------------------------------------

    [Fact]
    public void StatusTextAllSuppressesEveryStatusLineWhenOn()
    {
        Assert.False(Suppressed(_host.Status("Anything")));
        _settings.Filters.StatusTextAll.Value = true;
        Assert.True(Suppressed(_host.Status("Anything")));

        // A status line is never matched against the transcript rules —
        // Kind routes it to SuppressStatusText only.
        Assert.False(SuppressedSystemText("Anything"));
    }
}
