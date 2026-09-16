using OpenAC.MagTools.Loggers.Chat;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;
using ChatLoggerFeature = OpenAC.MagTools.Loggers.Chat.ChatLogger;

namespace OpenAC.MagTools.Tests.Loggers.Chat;

public sealed class ChatLoggerTests
{
    [Theory]
    [InlineData(
        "Master Arbitrator says, \"Arena Three is now available for new warriors!\"", false)]
    [InlineData("Master Arbitrator tells you, \"You fought too recently.\"", false)]
    [InlineData("You say, \"Zojak arwreth\"", false)]
    [InlineData("<Tell:IIDString:1:Bob>Bob<\\Tell> says, \"Malar tuash\"", false)]
    [InlineData("You say, \"hello there\"", true)]
    [InlineData("You tell Bob, \"hi\"", true)]
    [InlineData("Your spell fizzled.", false)]
    public void TryClassifyMatchesTheOriginalsDropAndTagRules(string text, bool expected)
        => Assert.Equal(expected, ChatLoggerFeature.TryClassify(text, out _));

    [Fact]
    public void TryClassifyTagsLocalSpeechAsArea()
    {
        Assert.True(ChatLoggerFeature.TryClassify("You say, \"hello\"", out ChatClassifier.ChatChannels type));
        Assert.Equal(ChatClassifier.ChatChannels.Area, type);
    }

    [Fact]
    public void TryClassifyTagsATellAsTells()
    {
        Assert.True(
            ChatLoggerFeature.TryClassify("You tell Bob, \"hi\"", out ChatClassifier.ChatChannels type));
        Assert.Equal(ChatClassifier.ChatChannels.Tells, type);
    }

    [Fact]
    public void TryClassifyTagsANamedChannel()
    {
        Assert.True(ChatLoggerFeature.TryClassify(
            "[General] <Tell:IIDString:0:Bob>Bob<\\Tell> says, \"hi\"",
            out ChatClassifier.ChatChannels type));
        Assert.Equal(ChatClassifier.ChatChannels.General, type);
    }

    private static (FakeHost Host, SettingsManager Settings, ChatLoggerFeature Logger) Build()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var logger = new ChatLoggerFeature(host, settings);
        return (host, settings, logger);
    }

    [Fact]
    public void StartSubscribesAndReceivedLinesReachBothGroups()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Group1.Area.Value = true;
        settings.ChatLogger.Group2.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");
        host.Automation.Chat.Deliver("You say, \"hello\"");

        Assert.Single(logger.Group1.Rows);
        Assert.Single(logger.Group2.Rows);
    }

    [Fact]
    public void StopUnsubscribesSoLaterLinesAreNotLogged()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Group1.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");
        logger.Stop();
        host.Automation.Chat.Deliver("You say, \"hello\"");

        Assert.Empty(logger.Group1.Rows);
    }

    [Fact]
    public void PersistentLinesAreBufferedAndFlushedToTheServerScopedKey()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Persistent.Value = true;
        settings.ChatLogger.Group1.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");
        host.Automation.Chat.Deliver("You say, \"hello\"");
        logger.Stop();

        string? text = host.Storage.ReadText("Frostfell/+Acdream.ChatLogger.txt");
        Assert.NotNull(text);
        Assert.Contains("You say, \"hello\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsPersistedWhenPersistentIsOff()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Persistent.Value = false;
        settings.ChatLogger.Group1.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");
        host.Automation.Chat.Deliver("You say, \"hello\"");
        logger.Stop();

        Assert.Null(host.Storage.ReadText("Frostfell/+Acdream.ChatLogger.txt"));
    }

    [Fact]
    public void OnlyChannelsEitherGroupActuallyLogsArePersisted()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Persistent.Value = true;
        // Neither group logs Fellowship.
        settings.ChatLogger.Group1.Fellowship.Value = false;
        settings.ChatLogger.Group2.Fellowship.Value = false;
        settings.ChatLogger.Group1.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");
        host.Automation.Chat.Deliver(
            "[Fellowship] <Tell:IIDString:0:Bob>Bob<\\Tell> says, \"hi\"");
        logger.Stop();

        Assert.Null(host.Storage.ReadText("Frostfell/+Acdream.ChatLogger.txt"));
    }

    [Fact]
    public void StartImportsRecentHistoryIntoBothGroups()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Group1.Area.Value = true;
        host.Storage.Seed(
            "Frostfell/+Acdream.ChatLogger.txt",
            "260916120000,1,You say, \"already here\"\n");
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");

        LoggedChatEntry imported = Assert.Single(logger.Group1.Rows);
        Assert.Equal("You say, \"already here\"", imported.Message);
    }

    [Fact]
    public void ClearHistoryClearsBothGroupsButNeverTouchesStorage()
    {
        // The original's equivalent button had a copy-paste bug that called
        // into the combat tracker's exporter with this file's name; that is
        // deliberately not reproduced.
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Persistent.Value = true;
        settings.ChatLogger.Group1.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        logger.Start(scheduler, "Frostfell", "+Acdream");
        host.Automation.Chat.Deliver("You say, \"hello\"");
        int writesBeforeClear = host.Storage.WriteCount;

        logger.ClearHistory();

        Assert.Empty(logger.Group1.Rows);
        Assert.Empty(logger.Group2.Rows);
        Assert.Equal(writesBeforeClear, host.Storage.WriteCount);
    }

    [Fact]
    public void StartingTwiceInARowIsANoOp()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Group1.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");
        logger.Start(scheduler, "Frostfell", "+Acdream");
        host.Automation.Chat.Deliver("You say, \"hello\"");

        // A double subscription would have logged the line twice.
        Assert.Single(logger.Group1.Rows);
    }
}
