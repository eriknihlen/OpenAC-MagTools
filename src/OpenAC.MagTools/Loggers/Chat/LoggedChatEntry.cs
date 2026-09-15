using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Loggers.Chat;

/// <summary>One classified chat line, ready for a GUI row or a file line.</summary>
public readonly record struct LoggedChatEntry(
    DateTimeOffset TimeStamp,
    ChatClassifier.ChatChannels ChatType,
    string Message);
