using System.Globalization;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Loggers.Chat;

/// <summary>
/// One classified chat line, ready for a GUI row or a file line.
/// <see cref="FormattedTime"/> and <see cref="CleanedMessage"/> are computed
/// once, right here in the primary constructor, rather than on every read —
/// so a page view-model projecting hundreds of rows every frame does no
/// per-row work of its own.
/// </summary>
public readonly record struct LoggedChatEntry(
    DateTimeOffset TimeStamp,
    ChatClassifier.ChatChannels ChatType,
    string Message)
{
    // Local time, to match the persisted file line's stamp (ChatLogFileStore
    // also writes ToLocalTime()) — a UTC-kind TimeStamp must not show its raw
    // UTC digits in the GUI row while the same entry's file line is local.
    public string FormattedTime { get; init; } =
        TimeStamp.ToLocalTime().ToString("yy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    public string CleanedMessage { get; init; } = ChatClassifier.CleanMessage(Message);
}
