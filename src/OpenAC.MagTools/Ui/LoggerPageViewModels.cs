using OpenAC.MagTools.Settings;
using ChatLoggerFeature = OpenAC.MagTools.Loggers.Chat.ChatLogger;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// Loggers → the two chat groups and their options. Both the channel checkbox
/// lists and the transcripts are live, backed by <see cref="ChatLoggerFeature"/>.
/// </summary>
public sealed class ChatLoggerPageViewModel
{
    private readonly ChatLoggerSettings _settings;
    private readonly ChatLoggerFeature _chatLogger;

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

    public IReadOnlyList<string> Group1Times =>
        [.. _chatLogger.Group1.Rows.Select(static row => row.TimeStamp.ToString("yy/MM/dd HH:mm"))];

    public IReadOnlyList<string> Group1Messages =>
        [.. _chatLogger.Group1.Rows.Select(
            static row => Chat.ChatClassifier.CleanMessage(row.Message))];

    public IReadOnlyList<string> Group2Times =>
        [.. _chatLogger.Group2.Rows.Select(static row => row.TimeStamp.ToString("yy/MM/dd HH:mm"))];

    public IReadOnlyList<string> Group2Messages =>
        [.. _chatLogger.Group2.Rows.Select(
            static row => Chat.ChatClassifier.CleanMessage(row.Message))];

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
