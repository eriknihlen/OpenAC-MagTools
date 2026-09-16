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
    public string FormattedTime { get; init; } =
        TimeStamp.ToString("yy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    public string CleanedMessage { get; init; } = ChatClassifier.CleanMessage(Message);
}
