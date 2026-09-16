using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// The original's dispatch rule for one queued command string (used by both
/// <see cref="LoginActions"/> and <see cref="PeriodicCommands"/>): a
/// <c>/mt…</c> command goes to the plugin's own console
/// (<c>ProcessMTCommand</c> in the original); anything else is submitted as
/// if the player had typed it into the chat box (the original's
/// <c>DecalProxy.DispatchChatToBoxWithPluginIntercept</c>, which let other
/// Decal plugins see the line too before falling back to the native chat
/// parser). OpenAC's <see cref="IPluginChat.Submit"/> is the client's own
/// equivalent route -- one native call rather than a synthetic keystroke
/// sequence.
/// </summary>
internal static class CommandDispatcher
{
    private const string MtPrefix = "/mt";

    public static void Dispatch(IPluginHost host, MtCommandRouter router, string command)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(command);

        if (TryGetMtArguments(command, out string arguments))
            router.Execute(arguments);
        else
            host.Automation.Chat.Submit(command);
    }

    /// <summary>
    /// True when <paramref name="command"/> is a <c>/mt</c> command, with
    /// <paramref name="arguments"/> set to the text after the verb (what
    /// <see cref="MtCommandRouter.Execute(string?)"/> expects) -- matched
    /// case-insensitively, same as the router's own commands.
    /// </summary>
    /// <remarks>
    /// L2: the prefix match is deliberately narrowed to "<c>/mt</c> followed
    /// by whitespace or end of string" rather than a plain
    /// <c>StartsWith("/mt")</c>. A bare <c>StartsWith</c> would also match a
    /// command that merely happens to begin with the same four characters --
    /// <c>/mtx</c>, <c>/mtblah</c> -- routing it into the console as if it
    /// were a (malformed) <c>/mt</c> verb instead of submitting it to chat as
    /// the player typed it. The boundary check keeps those going to chat.
    /// </remarks>
    internal static bool TryGetMtArguments(string command, out string arguments)
    {
        string trimmed = command.TrimStart();
        if (trimmed.StartsWith(MtPrefix, StringComparison.OrdinalIgnoreCase)
            && (trimmed.Length == MtPrefix.Length || char.IsWhiteSpace(trimmed[MtPrefix.Length])))
        {
            arguments = trimmed[MtPrefix.Length..].TrimStart();
            return true;
        }

        arguments = string.Empty;
        return false;
    }
}
