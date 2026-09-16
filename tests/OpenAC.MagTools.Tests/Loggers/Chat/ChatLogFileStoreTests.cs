using OpenAC.MagTools.Loggers.Chat;
using OpenAC.MagTools.Tests.Fakes;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Tests.Loggers.Chat;

public sealed class ChatLogFileStoreTests
{
    private const string Key = "Frostfell/+Acdream.ChatLogger.txt";

    [Fact]
    public void FlushWritesTheCurrentLineFormat()
    {
        var storage = new MemoryStorage();
        var store = new ChatLogFileStore(storage);
        var timestamp = new DateTimeOffset(2026, 9, 16, 12, 30, 0, TimeSpan.Zero);

        store.Enqueue(new LoggedChatEntry(
            timestamp, ChatClassifier.ChatChannels.Area, "You say, \"hi\""));
        store.Flush(Key);

        // Written in the machine's LOCAL time (see the M4 fix note on
        // Flush()), so the expected digits are computed the same way rather
        // than hardcoded to a UTC stamp that would only match on a UTC box.
        string expectedStamp = timestamp.ToLocalTime().ToString(
            "yyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string? text = storage.ReadText(Key);
        Assert.Equal(
            expectedStamp + ",1,You say, \"hi\"\n",
            text);
    }

    [Fact]
    public void FlushAppendsToWhatIsAlreadyThere()
    {
        var storage = new MemoryStorage();
        storage.Seed(Key, "260916120000,1,first\n");
        var store = new ChatLogFileStore(storage);

        store.Enqueue(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Area, "second"));
        store.Flush(Key);

        string text = storage.ReadText(Key)!;
        Assert.Contains("first", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("first", StringComparison.Ordinal)
            < text.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public void FlushWithNothingPendingDoesNotTouchStorage()
    {
        var storage = new MemoryStorage();
        var store = new ChatLogFileStore(storage);

        store.Flush(Key);

        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void FlushRollsAnOversizedFileToADatedNameBeforeAppending()
    {
        var storage = new MemoryStorage();
        // The roll threshold is an injected constructor parameter (rather
        // than a fixed 100MB constant), so the test can exercise the exact
        // same rolling logic against a few bytes instead of paying to
        // allocate and scan a 100MB string on every run.
        const int rollThreshold = 32;
        storage.Seed(Key, new string('x', rollThreshold));
        var store = new ChatLogFileStore(storage, rollThreshold);

        store.Enqueue(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Area, "fresh line"));
        store.Flush(Key);

        string datedKey = "Frostfell/+Acdream.ChatLogger "
            + DateTime.Now.ToString("yy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            + ".txt";

        string current = storage.ReadText(Key)!;
        Assert.True(current.Length < rollThreshold, "the current file must have rolled, not grown");
        Assert.Contains("fresh line", current, StringComparison.Ordinal);
        string rolled = storage.ReadText(datedKey)!;
        Assert.Equal(rollThreshold, rolled.Length);
        Assert.DoesNotContain("fresh line", rolled, StringComparison.Ordinal);
    }

    [Fact]
    public void FlushKeepsPendingLinesWhenStorageIsUnavailable()
    {
        var storage = new MemoryStorage { IsAvailable = false };
        var store = new ChatLogFileStore(storage);

        store.Enqueue(new LoggedChatEntry(
            DateTimeOffset.UtcNow, ChatClassifier.ChatChannels.Area, "queued while down"));
        store.Flush(Key);
        Assert.Equal(0, storage.WriteCount);

        storage.IsAvailable = true;
        store.Flush(Key);

        string? text = storage.ReadText(Key);
        Assert.NotNull(text);
        Assert.Contains("queued while down", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRecentParsesTheCurrentFormat()
    {
        var storage = new MemoryStorage();
        storage.Seed(Key, "260916123000,1,You say, \"hi\"\n260916123100,2,Bob tells you, \"hey\"\n");
        var store = new ChatLogFileStore(storage);

        IReadOnlyList<LoggedChatEntry> entries = store.ReadRecent(Key);

        Assert.Equal(2, entries.Count);
        Assert.Equal(ChatClassifier.ChatChannels.Area, entries[0].ChatType);
        Assert.Equal("You say, \"hi\"", entries[0].Message);
        Assert.Equal(ChatClassifier.ChatChannels.Tells, entries[1].ChatType);
    }

    [Fact]
    public void ReadRecentParsesTheLegacyFormat()
    {
        var storage = new MemoryStorage();
        storage.Seed(Key, "26/09/16 12:30:00,Area,You say, \"hi\"\n");
        var store = new ChatLogFileStore(storage);

        LoggedChatEntry entry = Assert.Single(store.ReadRecent(Key));
        Assert.Equal(ChatClassifier.ChatChannels.Area, entry.ChatType);
        Assert.Equal("You say, \"hi\"", entry.Message);
    }

    [Fact]
    public void ReadRecentIgnoresUnparsableLinesWithoutThrowing()
    {
        var storage = new MemoryStorage();
        storage.Seed(Key, "not a valid line at all\n260916123000,1,ok line\n");
        var store = new ChatLogFileStore(storage);

        LoggedChatEntry entry = Assert.Single(store.ReadRecent(Key));
        Assert.Equal("ok line", entry.Message);
    }

    [Fact]
    public void ReadRecentLimitsToTheTrailingBytesRequested()
    {
        var storage = new MemoryStorage();
        string oldLine = "260916120000,1,old\n";
        string newLine = "260916123000,1,new\n";
        storage.Seed(Key, oldLine + newLine);
        var store = new ChatLogFileStore(storage);

        IReadOnlyList<LoggedChatEntry> entries = store.ReadRecent(Key, limitBytes: newLine.Length);

        LoggedChatEntry entry = Assert.Single(entries);
        Assert.Equal("new", entry.Message);
    }

    [Fact]
    public void ReadRecentOnMissingKeyReturnsEmpty()
    {
        var storage = new MemoryStorage();
        var store = new ChatLogFileStore(storage);

        Assert.Empty(store.ReadRecent(Key));
    }
}
