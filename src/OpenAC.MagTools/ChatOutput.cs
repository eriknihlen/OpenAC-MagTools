using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools;

/// <summary>
/// The single place the plugin writes to the game's chat. Everything the
/// plugin prints keeps the original's <c>&lt;{Mag-Tools}&gt;: </c> prefix.
/// </summary>
/// <remarks>
/// The original chose a colour and a target window per line: plugin text used
/// colour id 5, item info used colour id 14 (window came from
/// <c>Misc/OutputTargetWindow</c>, still unrouted — see the TODO below). Those
/// colour ids ARE the client's log-text-type ids, so
/// <see cref="IPluginChat.PostMessage"/> takes the same numbers unchanged.
/// </remarks>
public sealed class ChatOutput
{
    public const string Prefix = "<{Mag-Tools}>: ";

    /// <summary>The original's plugin-text colour id, now a log-text-type id.</summary>
    private const int PluginTextLogType = 5;

    /// <summary>The original's item-info colour id, now a log-text-type id.</summary>
    private const int ItemInfoLogType = 14;

    private readonly IPluginHost _host;

    public ChatOutput(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Writes one line with the plugin prefix, in the plugin-text colour.</summary>
    public void Write(string text) => WriteRaw(Prefix + text);

    /// <summary>Writes one line exactly as given, with no prefix, in the plugin-text colour.</summary>
    public void WriteRaw(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // TODO(P-misc): route to Misc/OutputTargetWindow once the contract
        // exposes a target-window concept for PostMessage.
        _host.Automation.Chat.PostMessage(text, PluginTextLogType);
    }

    /// <summary>Writes item-info text (e.g. an appraisal report) in its own colour.</summary>
    public void WriteItemInfo(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _host.Automation.Chat.PostMessage(text, ItemInfoLogType);
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
