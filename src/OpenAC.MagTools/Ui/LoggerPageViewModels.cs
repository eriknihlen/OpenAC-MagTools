using OpenAC.MagTools.Loggers.Chat;
using OpenAC.MagTools.Settings;
using ChatLoggerFeature = OpenAC.MagTools.Loggers.Chat.ChatLogger;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// Loggers → the two chat groups and their options. Both the channel checkbox
/// lists and the transcripts are live, backed by <see cref="ChatLoggerFeature"/>.
/// </summary>
public sealed class ChatLoggerPageViewModel : IDisposable
{
    private readonly ChatLoggerSettings _settings;
    private readonly ChatLoggerFeature _chatLogger;
    private readonly RowProjectionCache _group1Cache = new();
    private readonly RowProjectionCache _group2Cache = new();
    private bool _disposed;

    public ChatLoggerPageViewModel(SettingsManager settings, ChatLoggerFeature chatLogger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(chatLogger);
        _settings = settings.ChatLogger;
        _chatLogger = chatLogger;

        Group1Options = new OptionListViewModel([.. _settings.Group1.All]);
        Group2Options = new OptionListViewModel([.. _settings.Group2.All]);

        SelectGroup1Row = index => Group1SelectedRow = index;
        SelectGroup2Row = index => Group2SelectedRow = index;
        ClearHistory = _chatLogger.ClearHistory;
        TogglePersistent = () =>
            _settings.Persistent.Value = !_settings.Persistent.Value;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Group1Options.Dispose();
        Group2Options.Dispose();
    }

    /// <summary>Reverses <see cref="Dispose"/> for a Disable()/Enable() cycle.</summary>
    public void Resubscribe()
    {
        if (!_disposed)
            return;
        _disposed = false;
        Group1Options.Resubscribe();
        Group2Options.Resubscribe();
    }

    /// <summary>
    /// Both projected columns for a group, rebuilt only when the group's
    /// <see cref="ChatLogGroup.Revision"/> moves — the row text itself
    /// (<see cref="LoggedChatEntry.FormattedTime"/>,
    /// <see cref="LoggedChatEntry.CleanedMessage"/>) is already precomputed
    /// per row, so a rebuild here is just two list copies, not per-row work.
    /// </summary>
    private sealed class RowProjectionCache
    {
        private long _revision = -1;
        private IReadOnlyList<string> _times = [];
        private IReadOnlyList<string> _messages = [];

        public (IReadOnlyList<string> Times, IReadOnlyList<string> Messages) Get(
            ChatLogGroup group)
        {
            if (group.Revision != _revision)
            {
                _revision = group.Revision;
                _times = [.. group.Rows.Select(static row => row.FormattedTime)];
                _messages = [.. group.Rows.Select(static row => row.CleanedMessage)];
            }

            return (_times, _messages);
        }
    }

    public IReadOnlyList<string> Group1Times => _group1Cache.Get(_chatLogger.Group1).Times;

    public IReadOnlyList<string> Group1Messages => _group1Cache.Get(_chatLogger.Group1).Messages;

    public IReadOnlyList<string> Group2Times => _group2Cache.Get(_chatLogger.Group2).Times;

    public IReadOnlyList<string> Group2Messages => _group2Cache.Get(_chatLogger.Group2).Messages;

    public int Group1SelectedRow { get; private set; } = -1;
    public int Group2SelectedRow { get; private set; } = -1;

    public Action<int> SelectGroup1Row { get; }
    public Action<int> SelectGroup2Row { get; }

    public OptionListViewModel Group1Options { get; }
    public OptionListViewModel Group2Options { get; }

    public Action ClearHistory { get; }

    public bool Persistent => _settings.Persistent.Value;

    public Action TogglePersistent { get; }
}
