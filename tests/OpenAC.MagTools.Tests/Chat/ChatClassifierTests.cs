using OpenAC.MagTools.Chat;

namespace OpenAC.MagTools.Tests.Chat;

/// <summary>
/// Coverage for the legacy composed-text regex API — reserved for a
/// hand-authored legacy import file and the rare System-shaped fallback in
/// <see cref="ChatClassifier.Classify"/> (see its remarks). Production
/// classification off the host's structured fields is
/// <see cref="ChatClassifierClassifyTests"/>.
/// </summary>
public sealed class ChatClassifierTests
{
    [Theory]
    [InlineData("You say, \"test\"")]
    [InlineData("<Tell:IIDString:1343111160:PlayerName>PlayerName<\\Tell> says, \"asdf\"")]
    [InlineData("Master Arbitrator says, \"Arena Three is now available for new warriors!\"")]
    [InlineData("You tell PlayerName, \"test\"")]
    [InlineData("<Tell:IIDString:1343111160:PlayerName>PlayerName<\\Tell> tells you, \"test\"")]
    [InlineData("Master Arbitrator tells you, \"You fought too recently.\"")]
    public void IsChatRecognizesEveryShapeUnderTheDefaultFlags(string text)
        => Assert.True(ChatClassifier.IsChat(text));

    [Theory]
    [InlineData("You evaded Remoran Corsair!")]
    [InlineData("Ruschk Sadist evaded your attack.")]
    [InlineData("Your spell fizzled.")]
    [InlineData("You have killed 50 Drudge Raveners! Your task is complete!")]
    public void IsChatRejectsNonChatSystemLines(string text)
        => Assert.False(ChatClassifier.IsChat(text));

    [Fact]
    public void IsChatHonorsARestrictedFlagSet()
    {
        string npcTell = "Master Arbitrator tells you, \"Hello.\"";
        Assert.True(ChatClassifier.IsChat(npcTell, ChatClassifier.ChatFlags.NpcTellsYou));
        Assert.False(ChatClassifier.IsChat(npcTell, ChatClassifier.ChatFlags.NpcSays));
    }

    [Theory]
    [InlineData("You say, \"test\"", "")] // "You say" has no source name of its own.
    [InlineData(
        "<Tell:IIDString:123:Bob>Bob<\\Tell> says, \"hi\"", "Bob")]
    [InlineData("Master Arbitrator says, \"Arena Three is available!\"", "Master Arbitrator")]
    [InlineData(
        "<Tell:IIDString:123:Bob>Bob<\\Tell> tells you, \"hi\"", "Bob")]
    [InlineData("Master Arbitrator tells you, \"Well done!\"", "Master Arbitrator")]
    [InlineData("Your spell fizzled.", "")]
    public void GetSourceOfChatExtractsTheSpeakerName(string text, string expected)
        => Assert.Equal(expected, ChatClassifier.GetSourceOfChat(text));

    [Fact]
    public void GetChatChannelMapsEachChannelName()
    {
        Assert.Equal(
            ChatClassifier.ChatChannels.Area,
            ChatClassifier.GetChatChannel("You say, \"hi\""));
        Assert.Equal(
            ChatClassifier.ChatChannels.Tells,
            ChatClassifier.GetChatChannel("You tell Bob, \"hi\""));
        Assert.Equal(
            ChatClassifier.ChatChannels.Fellowship,
            ChatClassifier.GetChatChannel(
                "[Fellowship] <Tell:IIDString:0:Bob>Bob<\\Tell> says, \"hi\""));
        Assert.Equal(
            ChatClassifier.ChatChannels.Allegiance,
            ChatClassifier.GetChatChannel(
                "[Allegiance] <Tell:IIDString:0:Bob>Bob<\\Tell> says, \"kk\""));
        Assert.Equal(
            ChatClassifier.ChatChannels.General,
            ChatClassifier.GetChatChannel(
                "[General] <Tell:IIDString:0:Bob>Bob<\\Tell> says, \"hi\""));
        Assert.Equal(
            ChatClassifier.ChatChannels.None,
            ChatClassifier.GetChatChannel("Your spell fizzled."));
    }

    [Fact]
    public void CleanMessageStripsTheClickableNameMarkup()
    {
        string dirty = "[Allegiance] <Tell:IIDString:0:PlayerName>PlayerName<\\Tell> says, \"kk\"";
        Assert.Equal("[Allegiance] PlayerName says, \"kk\"", ChatClassifier.CleanMessage(dirty));
    }

    [Fact]
    public void CleanMessageLeavesAPlainLineUnchanged()
    {
        Assert.Equal(
            "Your spell fizzled.", ChatClassifier.CleanMessage("Your spell fizzled."));
    }

    [Theory]
    [InlineData("You say, \"Zojak arwreth\"", true)]
    [InlineData("You say, \"hello there\"", false)]
    public void IsSpellCastingMessageMatchesMyOwnCastingSpeech(string text, bool expected)
        => Assert.Equal(
            expected,
            ChatClassifier.IsSpellCastingMessage(text, isMine: true, isPlayer: false));

    [Fact]
    public void IsSpellCastingMessageMatchesAnotherPlayersCastingSpeech()
    {
        string text = "<Tell:IIDString:1:Bob>Bob<\\Tell> says, \"Malar tuash\"";
        Assert.True(ChatClassifier.IsSpellCastingMessage(text, isMine: false));
        Assert.False(ChatClassifier.IsSpellCastingMessage(
            "You say, \"Malar tuash\"", isMine: false));
    }
}
