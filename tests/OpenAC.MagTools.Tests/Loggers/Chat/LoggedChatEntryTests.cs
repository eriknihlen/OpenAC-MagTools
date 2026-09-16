using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Tests.Loggers.Chat;

public sealed class LoggedChatEntryTests
{
    [Fact]
    public void FormattedTimeIsLocalTimeEvenForAUtcStampedEntry()
    {
        // Matches ChatLogFileStore.Flush's own ToLocalTime() on the persisted
        // file line — the GUI row and the file line for the same entry must
        // show the same clock digits, not a UTC/local mismatch.
        var utcStamp = new DateTimeOffset(2026, 9, 16, 12, 30, 0, TimeSpan.Zero);
        var entry = new OpenAC.MagTools.Loggers.Chat.LoggedChatEntry(
            utcStamp, ChatClassifier.ChatChannels.Area, "hi");

        string expected = utcStamp.ToLocalTime().ToString(
            "yy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, entry.FormattedTime);
    }

    [Fact]
    public void CleanedMessageStripsMarkupAtConstruction()
    {
        var entry = new OpenAC.MagTools.Loggers.Chat.LoggedChatEntry(
            DateTimeOffset.UtcNow,
            ChatClassifier.ChatChannels.General,
            "[General] <Tell:IIDString:0:PlayerName>PlayerName<\\Tell> says, \"kk\"");

        Assert.Equal("[General] PlayerName says, \"kk\"", entry.CleanedMessage);
    }
}
