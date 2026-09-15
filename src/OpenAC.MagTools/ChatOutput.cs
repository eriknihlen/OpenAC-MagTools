using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools;

/// <summary>
/// The single place the plugin writes to the game's chat. Everything the
/// plugin prints keeps the original's <c>&lt;{Mag-Tools}&gt;: </c> prefix.
/// </summary>
/// <remarks>
/// The original chose a colour and a target window per line (plugin text 5,
/// item info 14, window from Misc/OutputTargetWindow). The contract only has
/// <see cref="IPluginChat.PostSystemMessage"/> today, so every line lands in
/// the default window. Colour and window routing arrive with the chat slice of
/// the plugin API; isolating them here means nothing else has to change.
/// </remarks>
public sealed class ChatOutput
{
    public const string Prefix = "<{Mag-Tools}>: ";

    private readonly IPluginHost _host;

    public ChatOutput(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Writes one line with the plugin prefix.</summary>
    public void Write(string text) => WriteRaw(Prefix + text);

    /// <summary>Writes one line exactly as given, with no prefix.</summary>
    public void WriteRaw(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _host.Automation.Chat.PostSystemMessage(text);
    }

    /// <summary>
    /// The original's exception report, verbatim in shape:
    /// message, source and stack on their own lines, plus an optional note.
    /// </summary>
    public void WriteException(Exception exception, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string text = Prefix + "Exception caught: " + exception.Message
            + Environment.NewLine + exception.Source
            + Environment.NewLine + exception.StackTrace;
        if (!string.IsNullOrEmpty(note))
            text += Environment.NewLine + "Note: " + note;

        WriteRaw(text);
    }
}
