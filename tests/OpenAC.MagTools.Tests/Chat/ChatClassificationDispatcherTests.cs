using OpenAC.MagTools.Chat;
using OpenAC.MagTools.Loggers.Chat;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Combat;
using ChatLoggerFeature = OpenAC.MagTools.Loggers.Chat.ChatLogger;

namespace OpenAC.MagTools.Tests.Chat;

public sealed class ChatClassificationDispatcherTests
{
    [Fact]
    public void StartSubscribesExactlyOnceToTheRawFeed()
    {
        var host = new FakeHost();
        var dispatcher = new ChatClassificationDispatcher(host);

        dispatcher.Start();

        Assert.Equal(1, host.Automation.Chat.ReceivedSubscriberCount);
    }

    [Fact]
    public void StopUnsubscribesFromTheRawFeed()
    {
        var host = new FakeHost();
        var dispatcher = new ChatClassificationDispatcher(host);
        dispatcher.Start();

        dispatcher.Stop();

        Assert.Equal(0, host.Automation.Chat.ReceivedSubscriberCount);
    }

    [Fact]
    public void StartIsIdempotent()
    {
        var host = new FakeHost();
        var dispatcher = new ChatClassificationDispatcher(host);

        dispatcher.Start();
        dispatcher.Start();

        Assert.Equal(1, host.Automation.Chat.ReceivedSubscriberCount);
    }

    [Fact]
    public void ClassifiedFiresOnceWithTheSameLineForEveryDeliveredMessage()
    {
        var host = new FakeHost();
        host.Automation.Character.Name = "Acdream";
        var dispatcher = new ChatClassificationDispatcher(host);
        dispatcher.Start();

        var received = new List<ChatClassifier.ChatLine>();
        dispatcher.Classified += (_, line) => received.Add(line);

        host.Combat("You obliterate Drudge Skulker!");

        ChatClassifier.ChatLine line = Assert.Single(received);
        Assert.Equal(ChatMessageKind.Combat, line.Kind);
        Assert.Equal("You obliterate Drudge Skulker!", line.Message);
    }

    /// <summary>
    /// The whole point of M2 (see docs/deviations.md, "chat classification
    /// fan-out"): with ONE shared dispatcher wired into both consumers, one
    /// delivered line only ever costs one <see cref="ChatClassifier.Classify"/>
    /// call — proven here as one raw-feed subscription total, regardless of
    /// how many downstream trackers/loggers are listening.
    /// </summary>
    [Fact]
    public void OneSharedDispatcherFeedsBothTheChatLoggerAndTheCombatTrackerHostFromOneSubscription()
    {
        var host = new FakeHost();
        host.Automation.Character.Name = "Acdream";
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var dispatcher = new ChatClassificationDispatcher(host);
        var chatLogger = new ChatLoggerFeature(host, settings, dispatcher);
        var combatHost = new CombatTrackerHost(host, new ChatOutput(host), settings, dispatcher);
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));

        dispatcher.Start();
        chatLogger.Start(scheduler, "Frostfell", "Acdream");
        combatHost.Start(scheduler, "Frostfell", "Acdream");

        // Exactly one handler on the raw feed (the dispatcher's own) no
        // matter how many consumers subscribed to Classified.
        Assert.Equal(1, host.Automation.Chat.ReceivedSubscriberCount);

        host.Combat("You obliterate Drudge Skulker!");
        Assert.Single(combatHost.Current.CombatInfos);

        // Group1 logs Tells out of the box (Area is off by default).
        host.Tell("Bob", "hello there");
        Assert.Single(chatLogger.Group1.Rows);
    }
}
