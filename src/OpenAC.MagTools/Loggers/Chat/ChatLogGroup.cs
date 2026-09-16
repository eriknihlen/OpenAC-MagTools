using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Loggers.Chat;

/// <summary>
/// One of the two GUI transcript lists, mirroring the original's
/// <c>ChatLoggerGUI</c>: entries whose channel matches this group's six
/// checkboxes are kept, newest first, trimmed to 9,000 rows once the list
/// reaches 10,000 (the original's exact "list of 10,000 -> 9,000" hysteresis,
/// so a burst of chat cannot force a re-trim on every single line).
/// </summary>
public sealed class ChatLogGroup
{
    private const int MaxRows = 10_000;
    private const int TrimToRows = 9_000;

    private readonly ChatLoggerGroupSettings _settings;
    private readonly List<LoggedChatEntry> _rows = [];

    public ChatLogGroup(ChatLoggerGroupSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<LoggedChatEntry> Rows => _rows;

    /// <summary>
    /// Bumped on every <see cref="Add"/> that actually inserts a row and on
    /// every <see cref="Clear"/>, so a page view-model can cache its row
    /// projections and only rebuild them when this changes instead of
    /// re-projecting <see cref="Rows"/> on every read.
    /// </summary>
    public long Revision { get; private set; }

    public ChatClassifier.ChatChannels EnabledChannels()
    {
        var channels = ChatClassifier.ChatChannels.None;
        if (_settings.Area.Value) channels |= ChatClassifier.ChatChannels.Area;
        if (_settings.Tells.Value) channels |= ChatClassifier.ChatChannels.Tells;
        if (_settings.Fellowship.Value) channels |= ChatClassifier.ChatChannels.Fellowship;
        if (_settings.General.Value) channels |= ChatClassifier.ChatChannels.General;
        if (_settings.Trade.Value) channels |= ChatClassifier.ChatChannels.Trade;
        if (_settings.Allegiance.Value) channels |= ChatClassifier.ChatChannels.Allegiance;
        return channels;
    }

    /// <summary>Adds the entry if this group's channels claim it.</summary>
    public void Add(LoggedChatEntry entry)
    {
        if ((EnabledChannels() & entry.ChatType) == 0)
            return;

        // Trim BEFORE inserting the new row, so hitting the cap retains
        // 9,001 rows (9,000 kept + the one just added), matching the
        // original's exact hysteresis instead of trimming to 9,000 after.
        if (_rows.Count >= MaxRows)
            _rows.RemoveRange(TrimToRows, _rows.Count - TrimToRows);

        _rows.Insert(0, entry);
        Revision++;
    }

    public void Clear()
    {
        if (_rows.Count == 0)
            return;

        _rows.Clear();
        Revision++;
    }
}
