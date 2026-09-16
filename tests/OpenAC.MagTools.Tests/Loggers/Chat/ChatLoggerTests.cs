using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Loggers.Chat;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Chat;
using OpenAC.MagTools.Tests.Fakes;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;
using ChatLoggerFeature = OpenAC.MagTools.Loggers.Chat.ChatLogger;

namespace OpenAC.MagTools.Tests.Loggers.Chat;

public sealed class ChatLoggerTests
{
    private static bool TryClassify(
        FakeHost host, PluginChatMessage message, out ChatClassifier.ChatChannels type)
        => ChatLoggerFeature.TryClassify(message, host.Automation, out type);

    [Fact]
    public void TryClassifyDropsNpcSpeechNpcTellsAndSpellCasts()
    {
        var host = new FakeHost();
        host.Automation.Objects.Objects.Add(
            new PluginWorldObject(1u, 0u, "Master Arbitrator", PluginObjectClass.Npc, 0u, 0u, 0u));

        Assert.False(TryClassify(
            host,
            host.Say(
                "Master Arbitrator", "Arena Three is now available for new warriors!",
                fromObjectId: 1u),
            out _));
        Assert.False(TryClassify(
            host, host.Tell("Master Arbitrator", "You fought too recently.", fromObjectId: 1u),
            out _));
        Assert.False(TryClassify(host, host.Say("Bob", "Zojak arwreth", mine: true), out _));
        Assert.False(TryClassify(host, host.Say("Bob", "Malar tuash", mine: false), out _));
    }

    [Fact]
    public void TryClassifyKeepsATellOrChannelLineThatStartsWithASpellWord()
    {
        // The spell-cast drop is local-speech-only (see ChatLine.IsSpellCast's
        // remarks) — a tell or channel line that merely starts with the same
        // word is ordinary chat and gets tagged/logged like any other.
        var host = new FakeHost();

        Assert.True(TryClassify(
            host, host.Tell("Bob", "Zojak arwreth"), out ChatClassifier.ChatChannels tellType));
        Assert.Equal(ChatClassifier.ChatChannels.Tells, tellType);

        Assert.True(TryClassify(
            host, host.ChannelSay("General", "Bob", "Zojak arwreth"),
            out ChatClassifier.ChatChannels channelType));
        Assert.Equal(ChatClassifier.ChatChannels.General, channelType);
    }

    [Fact]
    public void TryClassifyDropsNonChatLines()
    {
        var host = new FakeHost();
        Assert.False(TryClassify(host, host.System("Your spell fizzled."), out _));
        Assert.False(TryClassify(host, host.Combat("You hit for 10 damage."), out _));
    }

    [Fact]
    public void TryClassifyTagsLocalSpeechAsArea()
    {
        var host = new FakeHost();
        Assert.True(TryClassify(
            host, host.Say("Bob", "hello there", mine: true),
            out ChatClassifier.ChatChannels type));
        Assert.Equal(ChatClassifier.ChatChannels.Area, type);
    }

    [Fact]
    public void TryClassifyTagsATellAsTells()
    {
        var host = new FakeHost();
        Assert.True(TryClassify(
            host, host.YouTell("Bob", "hi"), out ChatClassifier.ChatChannels type));
        Assert.Equal(ChatClassifier.ChatChannels.Tells, type);
    }

    [Fact]
    public void TryClassifyTagsANamedChannel()
    {
        var host = new FakeHost();
        Assert.True(TryClassify(
            host, host.ChannelSay("General", "Bob", "hi"),
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
        host.Say("Bob", "hello", mine: true);

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
        host.Say("Bob", "hello", mine: true);

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
        host.Say("Bob", "hello", mine: true);
        logger.Stop();

        string? text = host.Storage.ReadText("Frostfell/+Acdream.ChatLogger.txt");
        Assert.NotNull(text);
        Assert.Contains("hello", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsPersistedWhenPersistentIsOff()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Persistent.Value = false;
        settings.ChatLogger.Group1.Area.Value = true;
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");
        host.Say("Bob", "hello", mine: true);
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
        host.ChannelSay("Fellowship", "Bob", "hi");
        logger.Stop();

        Assert.Null(host.Storage.ReadText("Frostfell/+Acdream.ChatLogger.txt"));
    }

    [Fact]
    public void StartImportsRecentHistoryIntoBothGroupsWhenPersistentIsOn()
    {
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Persistent.Value = true;
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
    public void StartDoesNotImportWhenPersistentIsOff()
    {
        // Nothing is ever written while persistence is off, so a leftover
        // file from an earlier on-session is stale, not "recent" history —
        // Import() skips reading storage at all in that case.
        (FakeHost host, SettingsManager settings, ChatLoggerFeature logger) = Build();
        settings.ChatLogger.Persistent.Value = false;
        settings.ChatLogger.Group1.Area.Value = true;
        host.Storage.Seed(
            "Frostfell/+Acdream.ChatLogger.txt",
            "260916120000,1,You say, \"already here\"\n");
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        logger.Start(scheduler, "Frostfell", "+Acdream");

        Assert.Empty(logger.Group1.Rows);
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
        host.Say("Bob", "hello", mine: true);
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
        host.Say("Bob", "hello", mine: true);

        // A double subscription would have logged the line twice.
        Assert.Single(logger.Group1.Rows);
    }
}
