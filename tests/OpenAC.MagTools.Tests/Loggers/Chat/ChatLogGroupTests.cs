using OpenAC.MagTools.Loggers.Chat;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Tests.Loggers.Chat;

public sealed class ChatLogGroupTests
{
    [Fact]
    public void EnabledChannelsDefaultsToTellsOnlyForGroupOne()
    {
        var settingsManager = new SettingsManager(new SettingsFile(new MemoryStorage()));
        var chatGroup = new ChatLogGroup(settingsManager.ChatLogger.Group1);

        Assert.Equal(ChatClassifier.ChatChannels.Tells, chatGroup.EnabledChannels());
    }

    [Fact]
    public void EnabledChannelsReflectsEachCheckbox()
    {
        var settingsManager = new SettingsManager(new SettingsFile(new MemoryStorage()));
        ChatLoggerGroupSettings group = settingsManager.ChatLogger.Group1;
        group.Area.Value = true;
        group.Fellowship.Value = true;

        var chatGroup = new ChatLogGroup(group);
        Assert.Equal(
            ChatClassifier.ChatChannels.Area
                | ChatClassifier.ChatChannels.Tells
                | ChatClassifier.ChatChannels.Fellowship,
            chatGroup.EnabledChannels());
    }

    [Fact]
    public void AddOnlyKeepsEntriesTheGroupsChannelsClaim()
    {
        var settingsManager = new SettingsManager(new SettingsFile(new MemoryStorage()));
        ChatLoggerGroupSettings group = settingsManager.ChatLogger.Group1;
        group.Area.Value = true;
        group.Tells.Value = false;

        var chatGroup = new ChatLogGroup(group);

        chatGroup.Add(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Area, "You say, \"hi\""));
        chatGroup.Add(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Tells, "Bob tells you, \"hi\""));

        LoggedChatEntry kept = Assert.Single(chatGroup.Rows);
        Assert.Equal(ChatClassifier.ChatChannels.Area, kept.ChatType);
    }

    [Fact]
    public void AddInsertsNewestFirst()
    {
        var settingsManager = new SettingsManager(new SettingsFile(new MemoryStorage()));
        settingsManager.ChatLogger.Group1.Area.Value = true;
        var chatGroup = new ChatLogGroup(settingsManager.ChatLogger.Group1);

        chatGroup.Add(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Area, "first"));
        chatGroup.Add(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Area, "second"));

        Assert.Equal(["second", "first"], chatGroup.Rows.Select(row => row.Message));
    }

    [Fact]
    public void AddTrimsToNineThousandOneOnceTenThousandIsReached()
    {
        // The original's exact hysteresis: trim happens BEFORE the insert
        // that would push the list to 10,000, so the count that survives a
        // trim is 9,001 (9,000 kept + the row just added) rather than 9,000.
        var settingsManager = new SettingsManager(new SettingsFile(new MemoryStorage()));
        settingsManager.ChatLogger.Group1.Area.Value = true;
        var chatGroup = new ChatLogGroup(settingsManager.ChatLogger.Group1);

        for (int index = 0; index < 10_001; index++)
        {
            chatGroup.Add(new LoggedChatEntry(
                DateTimeOffset.UtcNow,
                ChatClassifier.ChatChannels.Area,
                index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        Assert.Equal(9_001, chatGroup.Rows.Count);
        // Newest ("10000") survives; the oldest rows were trimmed.
        Assert.Equal("10000", chatGroup.Rows[0].Message);
    }

    [Fact]
    public void ClearEmptiesTheList()
    {
        var settingsManager = new SettingsManager(new SettingsFile(new MemoryStorage()));
        settingsManager.ChatLogger.Group1.Area.Value = true;
        var chatGroup = new ChatLogGroup(settingsManager.ChatLogger.Group1);
        chatGroup.Add(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Area, "hi"));

        chatGroup.Clear();

        Assert.Empty(chatGroup.Rows);
    }
}
