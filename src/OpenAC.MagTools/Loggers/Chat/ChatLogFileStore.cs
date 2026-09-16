using System.Globalization;
using System.Text;
using AcDream.Plugin.Abstractions;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Loggers.Chat;

/// <summary>
/// Port of the original's <c>BufferedChatLogFileWriter</c> +
/// <c>ChatLogImporter</c> against <see cref="IPluginStorage"/>'s text-only
/// (whole-file read/write) contract instead of a real append-mode file
/// stream.
/// </summary>
/// <remarks>
/// A flush therefore reads the whole current file, appends the buffered
/// lines in memory, and writes the whole thing back — the file-size-based
/// roll check happens against that reloaded content before appending, same
/// as the original checked <c>FileInfo.Length</c> before opening for append.
/// "Renaming" the oversized file away is a write to the dated key followed by
/// a delete of the original, since storage has no move/rename primitive.
/// </remarks>
public sealed class ChatLogFileStore
{
    private const long DefaultRollSizeBytes = 100_000_000;
    private const int DefaultImportLimitBytes = 1_048_576;

    private readonly IPluginStorage _storage;
    private readonly long _rollSizeBytes;
    private readonly List<LoggedChatEntry> _pending = [];

    public ChatLogFileStore(IPluginStorage storage, long rollSizeBytes = DefaultRollSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (rollSizeBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(rollSizeBytes));
        _storage = storage;
        _rollSizeBytes = rollSizeBytes;
    }

    public void Enqueue(LoggedChatEntry entry) => _pending.Add(entry);

    /// <summary>Drops anything buffered without writing it (persistence turned off).</summary>
    public void Discard() => _pending.Clear();

    /// <summary>Writes whatever is buffered, rolling the file first if it is oversized.</summary>
    public void Flush(string storageKey)
    {
        if (_pending.Count == 0)
            return;

        if (!_storage.IsAvailable)
        {
            // Keep what is buffered rather than dropping it: storage coming
            // back before the next flush (or before Enqueue() stops being
            // called) still gets to write it, instead of silently losing
            // lines to a transient unavailability.
            return;
        }

        string? existing = _storage.ReadText(storageKey);

        if (existing is not null
            && Encoding.UTF8.GetByteCount(existing) >= _rollSizeBytes)
        {
            _storage.WriteText(RolledKey(storageKey), existing);
            _storage.Delete(storageKey);
            existing = null;
        }

        var builder = new StringBuilder(existing ?? string.Empty);
        foreach (LoggedChatEntry entry in _pending)
        {
            builder
                // Written in local time to match TryParseLine's read-back,
                // which treats an Unspecified-kind parse as local
                // (DateTimeOffset(DateTime) does that automatically for a
                // Kind.Unspecified value) — without ToLocalTime() here, a
                // UTC-kind TimeStamp would format its own UTC clock digits
                // and then be misread as local time on the next import.
                .Append(entry.TimeStamp.ToLocalTime().ToString(
                    "yyMMddHHmmss", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(((int)entry.ChatType).ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(ChatClassifier.CleanMessage(entry.Message))
                .Append('\n');
        }

        _storage.WriteText(storageKey, builder.ToString());
        _pending.Clear();
    }

    /// <summary>
    /// Reads the tail of the log file (both the legacy
    /// <c>yy/MM/dd HH:mm:ss,ChannelName,message</c> format and the current
    /// <c>yyMMddHHmmss,intValue,message</c> one) and returns every line that
    /// parses, oldest first.
    /// </summary>
    public IReadOnlyList<LoggedChatEntry> ReadRecent(
        string storageKey, int limitBytes = DefaultImportLimitBytes)
    {
        string? text = _storage.IsAvailable ? _storage.ReadText(storageKey) : null;
        if (string.IsNullOrEmpty(text))
            return [];

        if (text.Length > limitBytes)
            text = text[^limitBytes..];

        var entries = new List<LoggedChatEntry>();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            if (TryParseLine(line, out LoggedChatEntry entry))
                entries.Add(entry);
        }

        return entries;
    }

    private static bool TryParseLine(string line, out LoggedChatEntry entry)
    {
        entry = default;

        int firstComma = line.IndexOf(',', StringComparison.Ordinal);
        if (firstComma < 0)
            return false;
        int secondComma = line.IndexOf(',', firstComma + 1);
        if (secondComma < 0)
            return false;

        string timePart = line[..firstComma];
        string typePart = line[(firstComma + 1)..secondComma];
        string message = line[(secondComma + 1)..];

        DateTime timestamp;
        ChatClassifier.ChatChannels type;

        if (timePart.Contains('/', StringComparison.Ordinal))
        {
            if (!DateTime.TryParseExact(
                    timePart, "yy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out timestamp))
                return false;
            if (!Enum.TryParse(typePart, out type))
                return false;
        }
        else
        {
            if (!DateTime.TryParseExact(
                    timePart, "yyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out timestamp))
                return false;
            if (!int.TryParse(
                    typePart, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int typeInt))
                return false;
            type = (ChatClassifier.ChatChannels)typeInt;
        }

        entry = new LoggedChatEntry(new DateTimeOffset(timestamp), type, message);
        return true;
    }

    private static string RolledKey(string storageKey)
    {
        int slash = storageKey.LastIndexOf('/');
        string directory = slash >= 0 ? storageKey[..(slash + 1)] : string.Empty;
        string fileName = slash >= 0 ? storageKey[(slash + 1)..] : storageKey;

        int lastDot = fileName.LastIndexOf('.');
        string nameWithoutExtension = lastDot >= 0 ? fileName[..lastDot] : fileName;
        string extension = lastDot >= 0 ? fileName[lastDot..] : string.Empty;

        string dated = nameWithoutExtension + " "
            + DateTime.Now.ToString("yy-MM-dd", CultureInfo.InvariantCulture)
            + extension;
        return directory + dated;
    }
}
