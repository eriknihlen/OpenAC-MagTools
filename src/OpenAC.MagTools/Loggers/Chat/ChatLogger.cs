using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Loggers.Chat;

/// <summary>
/// Port of the original's <c>Loggers.Chat.ChatLogger</c> orchestration:
/// classifies every received line (dropping NPC says/tells and spell-casting
/// echoes, tagging the rest as Tells/Area/a named channel), feeds the two GUI
/// transcript groups, and buffers persisted lines for
/// <see cref="ChatLogFileStore"/>.
/// </summary>
/// <remarks>
/// Starts on <see cref="SessionContext.LoginComplete"/> and stops on
/// <see cref="SessionContext.Logoff"/>, the way the original loaded/saved its
/// file at Activate/Deactivate.
/// </remarks>
public sealed class ChatLogger
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(10);

    private readonly IPluginHost _host;
    private readonly SettingsManager _settings;
    private readonly OpenAC.MagTools.Chat.ChatClassificationDispatcher? _dispatcher;
    private readonly ChatLogFileStore _fileStore;
    private Action<PluginChatMessage>? _onReceived;
    private Action<PluginChatMessage, ChatClassifier.ChatLine>? _onClassified;
    private IDisposable? _flushRegistration;
    private string _server = string.Empty;
    private string _character = string.Empty;
    private bool _running;

    /// <param name="dispatcher">
    /// The shared classify-once fan-out (see
    /// <see cref="OpenAC.MagTools.Chat.ChatClassificationDispatcher"/>) — pass the same
    /// instance <c>CombatTrackerHost</c> uses so a chat line is classified
    /// exactly once per delivery instead of once per consumer. Optional so
    /// a caller (or a test) that has no dispatcher still gets correct,
    /// self-contained behavior by classifying inline.
    /// </param>
    public ChatLogger(
        IPluginHost host, SettingsManager settings, OpenAC.MagTools.Chat.ChatClassificationDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
        _dispatcher = dispatcher;
        _fileStore = new ChatLogFileStore(host.Storage);
        Group1 = new ChatLogGroup(settings.ChatLogger.Group1);
        Group2 = new ChatLogGroup(settings.ChatLogger.Group2);
    }

    public ChatLogGroup Group1 { get; }

    public ChatLogGroup Group2 { get; }

    /// <summary>
    /// Begins logging for the session that just started. <paramref name="scheduler"/>
    /// drives the 10-minute persisted-file flush.
    /// </summary>
    public void Start(TickScheduler scheduler, string worldName, string characterName)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;

        _running = true;
        _server = worldName ?? string.Empty;
        _character = characterName ?? string.Empty;

        Import();

        if (_dispatcher is not null)
        {
            _onClassified = OnClassified;
            _dispatcher.Classified += _onClassified;
        }
        else
        {
            _onReceived = OnReceived;
            _host.Automation.Chat.Received += _onReceived;
        }

        _flushRegistration = scheduler.Every(FlushInterval, Flush);
    }

    /// <summary>Ends logging for the session that just ended, flushing what is buffered.</summary>
    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

        if (_onClassified is not null && _dispatcher is not null)
            _dispatcher.Classified -= _onClassified;
        _onClassified = null;

        if (_onReceived is not null)
            _host.Automation.Chat.Received -= _onReceived;
        _onReceived = null;

        _flushRegistration?.Dispose();
        _flushRegistration = null;

        Flush();
    }

    /// <summary>
    /// Clears both in-memory transcripts. Deliberately does NOT touch the
    /// persisted file — the original's equivalent button accidentally called
    /// into the combat tracker's exporter with this file's name instead
    /// (a copy-paste bug); that behaviour is not reproduced.
    /// </summary>
    public void ClearHistory()
    {
        Group1.Clear();
        Group2.Clear();
    }

    private string StorageKey => _server + "/" + _character + ".ChatLogger.txt";

    private void Import()
    {
        // Nothing is ever written to storage while persistence is off (see
        // Flush()), so reading it here would only ever replay whatever was
        // left over from a session where it was still on — stale history the
        // user has since turned off, not "recent" by any definition they'd
        // recognize.
        if (!_settings.ChatLogger.Persistent.Value)
            return;

        if (string.IsNullOrEmpty(_character))
            return;

        foreach (LoggedChatEntry entry in _fileStore.ReadRecent(StorageKey))
        {
            Group1.Add(entry);
            Group2.Add(entry);
        }
    }

    /// <summary>The no-dispatcher fallback path: classifies inline.</summary>
    private void OnReceived(PluginChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Text))
            return;

        OnClassified(message, ChatClassifier.Classify(message, _host.Automation));
    }

    /// <summary>
    /// The shared-dispatcher path: <paramref name="line"/> was already
    /// classified once by <see cref="OpenAC.MagTools.Chat.ChatClassificationDispatcher"/>.
    /// </summary>
    private void OnClassified(PluginChatMessage message, ChatClassifier.ChatLine line)
    {
        if (!TryClassify(line, out ChatClassifier.ChatChannels type))
            return;

        var entry = new LoggedChatEntry(message.Received, type, message.Text);
        Group1.Add(entry);
        Group2.Add(entry);

        if (!_settings.ChatLogger.Persistent.Value)
            return;

        ChatClassifier.ChatChannels persistedChannels =
            Group1.EnabledChannels() | Group2.EnabledChannels();
        if ((persistedChannels & type) != 0)
            _fileStore.Enqueue(entry);
    }

    private void Flush()
    {
        if (!_settings.ChatLogger.Persistent.Value)
        {
            // Leave whatever is pending alone rather than discarding it: it
            // was enqueued while Persistent was on (OnReceived gates
            // Enqueue() the same way), so a mid-session toggle-off-then-on
            // still writes it out on the next flush instead of losing it.
            return;
        }

        if (string.IsNullOrEmpty(_character))
            return;

        _fileStore.Flush(StorageKey);
    }

    /// <summary>
    /// Port of the original's <c>ChatLogger.Current_ChatBoxMessage</c>
    /// classification, now read from the message's structured fields via
    /// <see cref="ChatClassifier.Classify"/> instead of retail's composed
    /// text: NPC says/tells and spell-casting echoes are dropped outright,
    /// tells and local/channel speech are tagged, and everything else
    /// (system text, combat lines, etc.) is not logged at all.
    /// </summary>
    internal static bool TryClassify(
        PluginChatMessage message,
        IAutomationSurface automation,
        out ChatClassifier.ChatChannels type)
    {
        type = ChatClassifier.ChatChannels.None;
        if (string.IsNullOrEmpty(message.Text))
            return false;

        return TryClassify(ChatClassifier.Classify(message, automation), out type);
    }

    /// <summary>
    /// Same verdict as the message-based overload, but reused from an
    /// already-classified <see cref="ChatClassifier.ChatLine"/> — the shape
    /// <see cref="OpenAC.MagTools.Chat.ChatClassificationDispatcher"/>'s subscribers use so a
    /// line is never classified twice.
    /// </summary>
    internal static bool TryClassify(
        ChatClassifier.ChatLine line, out ChatClassifier.ChatChannels type)
    {
        type = ChatClassifier.ChatChannels.None;

        if (string.IsNullOrEmpty(line.Message))
            return false;

        if (!line.IsChat)
            return false;

        if (line.IsNpc)
            return false;

        if (line.IsSpellCast(mine: true, others: true))
            return false;

        if (line.Channel == ChatClassifier.ChatChannels.None)
            return false;

        type = line.Channel;
        return true;
    }
}
