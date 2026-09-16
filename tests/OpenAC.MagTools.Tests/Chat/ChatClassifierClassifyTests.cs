using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Chat;
using OpenAC.MagTools.Tests.Fakes;
using ChatLine = OpenAC.MagTools.Chat.ChatClassifier.ChatLine;

namespace OpenAC.MagTools.Tests.Chat;

/// <summary>
/// <see cref="ChatClassifier.Classify"/> read straight off the host's
/// structured <see cref="PluginChatMessage"/> fields — the production path,
/// as opposed to the legacy composed-text regexes in
/// <see cref="ChatClassifierTests"/>.
/// </summary>
public sealed class ChatClassifierClassifyTests
{
    private readonly FakeHost _host = new();

    private ChatLine Classify(PluginChatMessage message) =>
        ChatClassifier.Classify(message, _host.Automation);

    // ---- IsChat -------------------------------------------------------------

    [Theory]
    [InlineData(ChatMessageKind.LocalSpeech)]
    [InlineData(ChatMessageKind.RangedSpeech)]
    [InlineData(ChatMessageKind.Channel)]
    [InlineData(ChatMessageKind.Tell)]
    public void IsChatTrueForEverySpeechKind(ChatMessageKind kind)
        => Assert.True(Classify(new PluginChatMessage(1, 0u, (int)kind, "Bob", "hi", "General")).IsChat);

    [Theory]
    [InlineData(ChatMessageKind.System)]
    [InlineData(ChatMessageKind.Popup)]
    [InlineData(ChatMessageKind.Emote)]
    [InlineData(ChatMessageKind.SoulEmote)]
    [InlineData(ChatMessageKind.Combat)]
    public void IsChatFalseForEveryNonSpeechKind(ChatMessageKind kind)
        => Assert.False(Classify(new PluginChatMessage(1, 0u, (int)kind, "Bob", "hi", "")).IsChat);

    // ---- Source ---------------------------------------------------------------

    [Fact]
    public void SourceIsTheSenderFieldVerbatim()
    {
        ChatLine line = Classify(_host.Say("Bob", "hello", fromObjectId: 5u));
        Assert.Equal("Bob", line.Source);
    }

    // ---- Channel ----------------------------------------------------------------

    [Fact]
    public void ChannelIsAreaForLocalAndRangedSpeech()
    {
        Assert.Equal(
            ChatClassifier.ChatChannels.Area, Classify(_host.Say("Bob", "hi")).Channel);
        Assert.Equal(
            ChatClassifier.ChatChannels.Area,
            Classify(_host.Say("Bob", "hi", ranged: true)).Channel);
    }

    [Fact]
    public void ChannelIsTellsForATellEitherDirection()
    {
        Assert.Equal(
            ChatClassifier.ChatChannels.Tells, Classify(_host.Tell("Bob", "hi")).Channel);
        Assert.Equal(
            ChatClassifier.ChatChannels.Tells, Classify(_host.YouTell("Bob", "hi")).Channel);
    }

    [Theory]
    [InlineData("Fellowship", ChatClassifier.ChatChannels.Fellowship)]
    [InlineData("Allegiance", ChatClassifier.ChatChannels.Allegiance)]
    [InlineData("General", ChatClassifier.ChatChannels.General)]
    [InlineData("Trade", ChatClassifier.ChatChannels.Trade)]
    [InlineData("LFG", ChatClassifier.ChatChannels.LFG)]
    [InlineData("Roleplay", ChatClassifier.ChatChannels.Roleplay)]
    [InlineData("Society", ChatClassifier.ChatChannels.Society)]
    [InlineData("Vassals", ChatClassifier.ChatChannels.None)]
    public void ChannelMapsEachNamedTurbineChannel(
        string channelName, ChatClassifier.ChatChannels expected)
        => Assert.Equal(
            expected, Classify(_host.ChannelSay(channelName, "Bob", "hi")).Channel);

    [Fact]
    public void ChannelIsNoneForNonSpeechKinds()
        => Assert.Equal(
            ChatClassifier.ChatChannels.None, Classify(_host.System("Anything")).Channel);

    // ---- IsMine -----------------------------------------------------------------

    [Fact]
    public void IsMineTrueForMyOwnLocalSpeech()
        => Assert.True(Classify(_host.Say("Bob", "hi", mine: true)).IsMine);

    [Fact]
    public void IsMineFalseForAnotherPlayersLocalSpeech()
        => Assert.False(Classify(_host.Say("Bob", "hi", mine: false)).IsMine);

    [Fact]
    public void IsMineTrueForMyOwnChannelSendEvenWhenSenderIsEmpty()
        => Assert.True(Classify(_host.ChannelSay("General", "Bob", "hi", mine: true)).IsMine);

    [Fact]
    public void IsMineTrueForAChannelBroadcastEchoingMyOwnCharacterName()
    {
        // Some channels self-echo through OnChannelBroadcast instead of
        // OnSelfSent, so Sender carries the local player's OWN name rather
        // than being empty — IsMine must still recognize that as mine.
        _host.Automation.Character.Name = "+Acdream";
        Assert.True(Classify(_host.ChannelSay("Allegiance", "+Acdream", "hi")).IsMine);
    }

    [Fact]
    public void IsMineFalseForAnotherPlayersChannelBroadcast()
        => Assert.False(Classify(_host.ChannelSay("General", "Bob", "hi")).IsMine);

    [Fact]
    public void IsMineTrueForATellISent()
        => Assert.True(Classify(_host.YouTell("Bob", "hi")).IsMine);

    [Fact]
    public void IsMineFalseForATellIReceived()
        => Assert.False(Classify(_host.Tell("Bob", "hi")).IsMine);

    [Fact]
    public void IsMineRecognizesMyGuidEvenWithoutTheYouSenderRewrite()
    {
        _host.Automation.Character.ObjectId = 0x50000042u;
        var message = new PluginChatMessage(
            1, 0x50000042u, (int)ChatMessageKind.LocalSpeech, "+Acdream", "hi", "");
        Assert.True(Classify(message).IsMine);
    }

    // ---- SpeakerClass / IsNpc -----------------------------------------------------

    [Fact]
    public void SpeakerClassResolvesViaObjectsTryGetWhenSenderObjectIdIsNonZero()
    {
        _host.Automation.Objects.Objects.Add(
            new PluginWorldObject(7u, 0u, "Larry", PluginObjectClass.Vendor, 0u, 0u, 0u));
        ChatLine line = Classify(_host.Tell("Larry", "Buy!", fromObjectId: 7u));
        Assert.Equal(PluginObjectClass.Vendor, line.SpeakerClass);
        Assert.True(line.IsNpc);
    }

    [Fact]
    public void SpeakerClassIsNullWhenSenderObjectIdIsZeroAndNoFallbackIsGiven()
    {
        ChatLine line = ChatClassifier.Classify(
            _host.YouTell("Bob", "hi"), _host.Automation, resolveSpeakerByName: null);
        Assert.Null(line.SpeakerClass);
    }

    [Fact]
    public void SpeakerClassUsesTheNameFallbackOnlyWhenSenderObjectIdIsZero()
    {
        var calls = new List<string>();
        Func<string, PluginObjectClass?> fallback = name =>
        {
            calls.Add(name);
            return name == "Larry" ? PluginObjectClass.Vendor : null;
        };

        // SenderObjectId nonzero: TryGet path, fallback never consulted.
        _host.Automation.Objects.Objects.Add(
            new PluginWorldObject(7u, 0u, "Larry", PluginObjectClass.Vendor, 0u, 0u, 0u));
        ChatClassifier.Classify(
            _host.Tell("Larry", "Buy!", fromObjectId: 7u), _host.Automation, fallback);
        Assert.Empty(calls);

        // SenderObjectId zero: fallback consulted.
        ChatLine line = ChatClassifier.Classify(
            _host.YouTell("Larry", "hi"), _host.Automation, fallback);
        Assert.Equal(["Larry"], calls);
        Assert.Equal(PluginObjectClass.Vendor, line.SpeakerClass);
    }

    [Fact]
    public void SpeakerClassSkipsTheNameFallbackForAChannelLine()
    {
        // A received channel broadcast's SenderObjectId is ALWAYS 0
        // (ChatLog.OnChannelBroadcast hardcodes it), but a channel speaker
        // is, in practice, never an NPC — the fallback must not run for it
        // even when the caller supplies one.
        var calls = new List<string>();
        Func<string, PluginObjectClass?> fallback = name =>
        {
            calls.Add(name);
            return PluginObjectClass.Npc;
        };

        ChatLine line = ChatClassifier.Classify(
            _host.ChannelSay("General", "Bob", "hi"), _host.Automation, fallback);

        Assert.Empty(calls);
        Assert.Null(line.SpeakerClass);
    }

    [Fact]
    public void SpeakerClassIsNullForASystemLineEvenWithANonzeroSenderObjectId()
    {
        // ChatLog.OnPlayerKilled puts the VICTIM's guid in SenderObjectId
        // for a Kind == System death message — that id is not a speaker,
        // and Objects.TryGet must never be asked about it.
        _host.Automation.Objects.Objects.Add(
            new PluginWorldObject(9u, 0u, "Ruschk Warlord", PluginObjectClass.Monster, 0u, 0u, 0u));
        var message = new PluginChatMessage(
            1, 9u, (int)ChatMessageKind.System, "", "You have slain Ruschk Warlord!", "");

        ChatLine line = ChatClassifier.Classify(message, _host.Automation);

        Assert.Null(line.SpeakerClass);
        Assert.False(line.IsNpc);
    }

    [Fact]
    public void IsNpcTrueForVendorMonsterAndPlainNpcOnly()
    {
        Assert.True(new ChatLine(
            ChatMessageKind.Tell, "Larry", ChatClassifier.ChatChannels.Tells, true, false,
            PluginObjectClass.Vendor, "hi", 1u, 0, 0).IsNpc);
        Assert.True(new ChatLine(
            ChatMessageKind.Tell, "Bob", ChatClassifier.ChatChannels.Tells, true, false,
            PluginObjectClass.Monster, "hi", 1u, 0, 0).IsNpc);
        Assert.True(new ChatLine(
            ChatMessageKind.LocalSpeech, "Crier", ChatClassifier.ChatChannels.Area, true, false,
            PluginObjectClass.Npc, "hi", 1u, 0, 0).IsNpc);
        Assert.False(new ChatLine(
            ChatMessageKind.Tell, "Bob", ChatClassifier.ChatChannels.Tells, true, false,
            PluginObjectClass.Player, "hi", 1u, 0, 0).IsNpc);
        Assert.False(new ChatLine(
            ChatMessageKind.Tell, "Bob", ChatClassifier.ChatChannels.Tells, true, false,
            null, "hi", 0u, 0, 0).IsNpc);
    }

    // ---- IsSpellCast --------------------------------------------------------------

    [Fact]
    public void IsSpellCastRequiresChat()
        => Assert.False(Classify(_host.System("Zojak arwreth")).IsSpellCast(true, true));

    [Fact]
    public void IsSpellCastIsLocalSpeechOnlyEvenForOtherChatKinds()
    {
        // The original's two regexes anchor on "You say"/"X says" only —
        // never a shout (ranged speech), a tell, or a channel line, even one
        // whose body starts with the exact same spell word.
        Assert.False(Classify(_host.Tell("Bob", "Zojak arwreth")).IsSpellCast(true, true));
        Assert.False(Classify(_host.YouTell("Bob", "Zojak arwreth")).IsSpellCast(true, true));
        Assert.False(
            Classify(_host.ChannelSay("General", "Bob", "Zojak arwreth")).IsSpellCast(true, true));
        Assert.False(
            Classify(_host.Say("Bob", "Zojak arwreth", ranged: true)).IsSpellCast(true, true));
    }

    [Fact]
    public void IsSpellCastMineHonorsTheMineFlag()
    {
        ChatLine mine = Classify(_host.Say("Bob", "Zojak arwreth", mine: true));
        Assert.True(mine.IsSpellCast(mine: true, others: false));
        Assert.False(mine.IsSpellCast(mine: false, others: true));
    }

    [Fact]
    public void IsSpellCastOthersHonorsTheOthersFlag()
    {
        ChatLine others = Classify(_host.Say("Bob", "Malar tuash", mine: false));
        Assert.True(others.IsSpellCast(mine: false, others: true));
        Assert.False(others.IsSpellCast(mine: true, others: false));
    }

    [Fact]
    public void IsSpellCastRequiresASpaceAfterTheWord()
        => Assert.False(Classify(_host.Say("Bob", "Zojakx arwreth", mine: true))
            .IsSpellCast(true, true));

    [Fact]
    public void IsSpellCastFalseForOrdinarySpeech()
        => Assert.False(Classify(_host.Say("Bob", "hello there", mine: true))
            .IsSpellCast(true, true));

    // ---- System-shaped fallback -----------------------------------------------------

    [Fact]
    public void SystemKindFallsBackToTheLegacyShapeWhenComposed()
    {
        ChatLine line = Classify(_host.System(
            "Master Arbitrator says, \"Arena Three is now available for new warriors!\""));
        Assert.True(line.IsChat);
        Assert.Equal("Master Arbitrator", line.Source);
        Assert.Equal(ChatClassifier.ChatChannels.Area, line.Channel);
    }

    [Fact]
    public void SystemKindWithoutComposedShapeStaysNonChat()
        => Assert.False(Classify(_host.System("Your spell fizzled.")).IsChat);
}
