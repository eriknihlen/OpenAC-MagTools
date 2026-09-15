using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// Loggers → the two chat groups and their options. The channel checkbox lists
/// are live; the transcripts themselves arrive with the chat-logger slice.
/// </summary>
/// <remarks>TODO(P2): the chat logger fills the two transcript lists.</remarks>
public sealed class ChatLoggerPageViewModel
{
    private readonly ChatLoggerSettings _settings;

    public ChatLoggerPageViewModel(SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.ChatLogger;

        Group1Options = new OptionListViewModel([.. _settings.Group1.All]);
        Group2Options = new OptionListViewModel([.. _settings.Group2.All]);

        SelectGroup1Row = index => Group1SelectedRow = index;
        SelectGroup2Row = index => Group2SelectedRow = index;
        ClearHistory = static () => { };
        TogglePersistent = () =>
            _settings.Persistent.Value = !_settings.Persistent.Value;
    }

    public IReadOnlyList<string> Group1Times { get; } = [];
    public IReadOnlyList<string> Group1Messages { get; } = [];
    public IReadOnlyList<string> Group2Times { get; } = [];
    public IReadOnlyList<string> Group2Messages { get; } = [];

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
