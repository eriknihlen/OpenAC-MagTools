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
    private readonly ChatLogFileStore _fileStore;
    private Action<PluginChatMessage>? _onReceived;
    private IDisposable? _flushRegistration;
    private string _server = string.Empty;
    private string _character = string.Empty;
    private bool _running;

    public ChatLogger(IPluginHost host, SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings;
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

        _onReceived = OnReceived;
        _host.Automation.Chat.Received += _onReceived;

        _flushRegistration = scheduler.Every(FlushInterval, Flush);
    }

    /// <summary>Ends logging for the session that just ended, flushing what is buffered.</summary>
    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

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

    private void OnReceived(PluginChatMessage message)
    {
        if (!TryClassify(message, _host.Automation, out ChatClassifier.ChatChannels type))
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

        ChatClassifier.ChatLine line = ChatClassifier.Classify(message, automation);

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
