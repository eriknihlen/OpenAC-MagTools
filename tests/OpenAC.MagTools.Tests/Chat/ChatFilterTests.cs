using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Chat;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Chat;

/// <summary>
/// One test per <c>Filters/*</c> rule (29 rules total): a positive case that
/// must be suppressed once its setting is on, and a negative case (the
/// setting left off) that must pass through untouched. A handful of rules
/// also assert their documented exceptions (the Spectral/Brilliance/Prodigal
/// expiry carve-out, the isChat gates).
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

    private bool Suppressed(string text, int kind = 0)
    {
        PluginChatMessage message = _host.Automation.Chat.Deliver(text, kind: kind);
        return !_host.Automation.Chat.Delivered.Any(
            delivered => delivered.Sequence == message.Sequence);
    }

    private void AddWorldObject(string name, PluginObjectClass objectClass)
        => _host.Automation.Objects.Objects.Add(
            new PluginWorldObject(1u, 0u, name, objectClass, 0u, 0u, 0u));

    // ---- 1. AttackEvades -------------------------------------------------

    [Fact]
    public void AttackEvadesSuppressesWhenOn()
    {
        Assert.False(Suppressed("Ruschk Sadist evaded your attack."));
        _settings.Filters.AttackEvades.Value = true;
        Assert.True(Suppressed("Ruschk Sadist evaded your attack."));
    }

    [Fact]
    public void AttackEvadesDoesNotEatARealChatLineWithTheSamePhrase()
    {
        // The !isChat gate: retail speech containing the phrase is still
        // speech, not the system evade line.
        _settings.Filters.AttackEvades.Value = true;
        Assert.False(Suppressed("You say, \"he evaded your attack.\""));
    }

    // ---- 2. DefenseEvades --------------------------------------------------

    [Fact]
    public void DefenseEvadesSuppressesWhenOn()
    {
        Assert.False(Suppressed("You evaded Ruschk Sadist!"));
        _settings.Filters.DefenseEvades.Value = true;
        Assert.True(Suppressed("You evaded Ruschk Sadist!"));
    }

    // ---- 3. AttackResists --------------------------------------------------

    [Fact]
    public void AttackResistsSuppressesWhenOn()
    {
        Assert.False(Suppressed("Sentient Crystal Shard resists your spell"));
        _settings.Filters.AttackResists.Value = true;
        Assert.True(Suppressed("Sentient Crystal Shard resists your spell"));
    }

    // ---- 4. DefenseResists -------------------------------------------------

    [Fact]
    public void DefenseResistsSuppressesWhenOn()
    {
        string text = "You resist the spell cast by Sentient Crystal Shard";
        Assert.False(Suppressed(text));
        _settings.Filters.DefenseResists.Value = true;
        Assert.True(Suppressed(text));
        Assert.True(Suppressed(
            "Ruschk Warlord tried to cast a spell on you, but was too far away!"));
    }

    // ---- 5. NPKFails --------------------------------------------------------

    [Fact]
    public void NpkFailsSuppressesWhenOn()
    {
        string text = "You fail to affect Bob, you are not a player killer!";
        Assert.False(Suppressed(text));
        _settings.Filters.NPKFails.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 6. DirtyFighting ---------------------------------------------------

    [Fact]
    public void DirtyFightingSuppressesWhenOn()
    {
        string text = "Dirty Fighting! Bob delivers a Traumatic Assault to Mob!";
        Assert.False(Suppressed(text));
        _settings.Filters.DirtyFighting.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 7. MonsterDeaths ---------------------------------------------------

    [Fact]
    public void MonsterDeathsSuppressesWhenOn()
    {
        Assert.False(Suppressed("You obliterate Drudge Skulker!"));
        _settings.Filters.MonsterDeaths.Value = true;
        Assert.True(Suppressed("You obliterate Drudge Skulker!"));
    }

    // ---- 8. SpellCastingMine --------------------------------------------------

    [Fact]
    public void SpellCastingMineSuppressesWhenOn()
    {
        string text = "You say, \"Zojak arwreth\"";
        Assert.False(Suppressed(text));
        _settings.Filters.SpellCastingMine.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 9. SpellCastingOthers -----------------------------------------------

    [Fact]
    public void SpellCastingOthersSuppressesWhenOn()
    {
        string text = "<Tell:IIDString:1:Bob>Bob<\\Tell> says, \"Malar tuash\"";
        Assert.False(Suppressed(text));
        _settings.Filters.SpellCastingOthers.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 10. SpellCastFizzles -------------------------------------------------

    [Fact]
    public void SpellCastFizzlesSuppressesWhenOn()
    {
        Assert.False(Suppressed("Your spell fizzled."));
        _settings.Filters.SpellCastFizzles.Value = true;
        Assert.True(Suppressed("Your spell fizzled."));
    }

    // ---- 11. CompUsage ----------------------------------------------------

    [Fact]
    public void CompUsageSuppressesWhenOn()
    {
        string text = "The spell consumed the following components: Herb, Powder";
        Assert.False(Suppressed(text));
        _settings.Filters.CompUsage.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 12. SpellExpires ---------------------------------------------------

    [Fact]
    public void SpellExpiresSuppressesWhenOnExceptTheRareItemException()
    {
        _settings.Filters.SpellExpires.Value = true;
        Assert.True(Suppressed("Focus Self VI has expired."));

        // The original never filters expiry lines mentioning these three
        // rare-item enchantment names.
        Assert.False(Suppressed("The spell Spectral Bane on your Coat has expired."));
        Assert.False(Suppressed("Brilliance VI has expired."));
        Assert.False(Suppressed("The Prodigal Aegis buff have expired."));
    }

    // ---- 13. HealingKitSuccess ----------------------------------------------

    [Fact]
    public void HealingKitSuccessSuppressesWhenOn()
    {
        string text = "You heal yourself for 88 Health points. "
            + "Your treated Healing Kit has 16 uses left.";
        Assert.False(Suppressed(text));
        _settings.Filters.HealingKitSuccess.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 14. HealingKitFail ---------------------------------------------------

    [Fact]
    public void HealingKitFailSuppressesWhenOn()
    {
        string text = "You fail to heal yourself. Your Treated Healing Kit has 18 uses left.";
        Assert.False(Suppressed(text));
        _settings.Filters.HealingKitFail.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 15. Salvaging ------------------------------------------------------

    [Fact]
    public void SalvagingSuppressesWhenOn()
    {
        string text = "You obtain 9 granite (ws 8.00) using your knowledge of Salvaging";
        Assert.False(Suppressed(text));
        _settings.Filters.Salvaging.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 16. SalvagingFails -----------------------------------------------

    [Fact]
    public void SalvagingFailsSuppressesWhenOn()
    {
        Assert.False(Suppressed("Salvaging Failed!"));
        _settings.Filters.SalvagingFails.Value = true;
        Assert.True(Suppressed("Salvaging Failed!"));
        Assert.True(Suppressed(
            "The following were not suitable for salvaging: Salvaged Sunstone (79)."));
    }

    // ---- 17. AuraOfCraftman -------------------------------------------------

    [Fact]
    public void AuraOfCraftmanSuppressesWhenOn()
    {
        string text =
            "Your Aura of the Craftman augmentation increased your skill by 5!";
        Assert.False(Suppressed(text));
        _settings.Filters.AuraOfCraftman.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 18. ManaStoneUsage -------------------------------------------------

    [Fact]
    public void ManaStoneUsageSuppressesWhenOn()
    {
        string text = "The Mana Stone gives 6,127 points of mana to the following items: ";
        Assert.False(Suppressed(text));
        _settings.Filters.ManaStoneUsage.Value = true;
        Assert.True(Suppressed(text));
        Assert.True(Suppressed("The Fez is destroyed."));
    }

    // ---- 19. TradeBuffBotSpam -----------------------------------------------

    [Fact]
    public void TradeBuffBotSpamSuppressesWhenOn()
    {
        Assert.False(Suppressed("Bob -t-"));
        _settings.Filters.TradeBuffBotSpam.Value = true;
        Assert.True(Suppressed("Bob -t-"));
    }

    // ---- 20. FailedAssess -----------------------------------------------------

    [Fact]
    public void FailedAssessSuppressesWhenOn()
    {
        Assert.False(Suppressed("Someone tried and failed to assess you!"));
        _settings.Filters.FailedAssess.Value = true;
        Assert.True(Suppressed("Someone tried and failed to assess you!"));
    }

    // ---- 21. KillTaskComplete ------------------------------------------------

    [Fact]
    public void KillTaskCompleteSuppressesWhenOn()
    {
        string text = "You have killed 50 Drudge Raveners! Your task is complete!";
        Assert.False(Suppressed(text));
        _settings.Filters.KillTaskComplete.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 22. VendorTells ------------------------------------------------------

    [Fact]
    public void VendorTellsSuppressesWhenOn()
    {
        AddWorldObject("Larry", PluginObjectClass.Vendor);
        string text = "Larry tells you, \"Buy something!\"";
        Assert.False(Suppressed(text));
        _settings.Filters.VendorTells.Value = true;
        Assert.True(Suppressed(text));
    }

    [Fact]
    public void VendorTellsLeavesATellFromAMonsterAlone()
    {
        AddWorldObject("Larry", PluginObjectClass.Monster);
        _settings.Filters.VendorTells.Value = true;
        Assert.False(Suppressed("Larry tells you, \"Grr.\""));
    }

    // ---- 23. MonsterTell -----------------------------------------------------

    [Fact]
    public void MonsterTellSuppressesWhenOn()
    {
        AddWorldObject("Ruschk Warlord", PluginObjectClass.Monster);
        string text = "Ruschk Warlord tells you, \"You will die!\"";
        Assert.False(Suppressed(text));
        _settings.Filters.MonsterTell.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 24. NpcChatter -----------------------------------------------------

    [Fact]
    public void NpcChatterSuppressesWhenOn()
    {
        AddWorldObject("Town Crier", PluginObjectClass.Npc);
        string text = "Town Crier says, \"Hear ye!\"";
        Assert.False(Suppressed(text));
        _settings.Filters.NpcChatter.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 25. MasterArbitratorSpam ---------------------------------------------

    [Fact]
    public void MasterArbitratorSpamSuppressesTheDocumentedLines()
    {
        string text = "Master Arbitrator tells you, "
            + "\"You shall be known to all as a Colosseum Champion!\"";
        Assert.False(Suppressed(text));
        _settings.Filters.MasterArbitratorSpam.Value = true;
        Assert.True(Suppressed(text));
    }

    [Fact]
    public void MasterArbitratorSpamLeavesOrdinaryArbitratorTellsAlone()
    {
        // The register's own comment: these are deliberately NOT filtered.
        _settings.Filters.MasterArbitratorSpam.Value = true;
        Assert.False(Suppressed(
            "Master Arbitrator tells you, \"Prepare your fellowship ahead of time.\""));
    }

    // ---- 26. AllMasterArbitratorChat -------------------------------------------

    [Fact]
    public void AllMasterArbitratorChatSuppressesEveryArbitratorLine()
    {
        string text = "Master Arbitrator tells you, \"Anything at all.\"";
        Assert.False(Suppressed(text));
        _settings.Filters.AllMasterArbitratorChat.Value = true;
        Assert.True(Suppressed(text));
    }

    // ---- 27. StatusTextYoureTooBusy -------------------------------------------

    [Fact]
    public void StatusTextYoureTooBusySuppressesWhenOn()
    {
        Assert.False(Suppressed("You're too busy!", PluginChatMessage.StatusTextKind));
        _settings.Filters.StatusTextYoureTooBusy.Value = true;
        Assert.True(Suppressed("You're too busy!", PluginChatMessage.StatusTextKind));
    }

    // ---- 28. StatusTextCasting -----------------------------------------------

    [Fact]
    public void StatusTextCastingSuppressesWhenOn()
    {
        Assert.False(Suppressed("Casting .....", PluginChatMessage.StatusTextKind));
        _settings.Filters.StatusTextCasting.Value = true;
        Assert.True(Suppressed("Casting .....", PluginChatMessage.StatusTextKind));
    }

    // ---- 29. StatusTextAll -----------------------------------------------------

    [Fact]
    public void StatusTextAllSuppressesEveryStatusLineWhenOn()
    {
        Assert.False(Suppressed("Anything", PluginChatMessage.StatusTextKind));
        _settings.Filters.StatusTextAll.Value = true;
        Assert.True(Suppressed("Anything", PluginChatMessage.StatusTextKind));

        // A status line is never matched against the transcript rules —
        // Kind routes it to SuppressStatusText only.
        Assert.False(Suppressed("Anything"));
    }
}
