using System.Text.RegularExpressions;

namespace OpenAC.MagTools.Chat;

/// <summary>
/// Port of the original's <c>Mag.Shared.Util</c> chat-classification helpers:
/// which lines are "chat" at all, who said one, which channel it rode, and how
/// to strip the retail clickable-name markup out of its text.
/// </summary>
/// <remarks>
/// The client keeps the raw wire text — including retail's
/// <c>&lt;Tell:IIDString:objectId:Name&gt;Name&lt;\Tell&gt;</c> clickable-name
/// markup — in <c>PluginChatMessage.Text</c>, exactly as the retail client (and
/// therefore the original Decal plugin) saw it. The regexes below are ported
/// verbatim against that same shape.
/// </remarks>
public static class ChatClassifier
{
    // Local chat.
    // You say, "test"
    private static readonly Regex YouSay = new(
        "^You say, \"(?<msg>.*)\"$", RegexOptions.Compiled);

    // <Tell:IIDString:1343111160:PlayerName>PlayerName<\Tell> says, "asdf"
    private static readonly Regex PlayerSaysLocal = new(
        "^<Tell:IIDString:[0-9]+:(?<name>[\\w\\s'-]+)>[\\w\\s'-]+<\\\\Tell> says, \"(?<msg>.*)\"$",
        RegexOptions.Compiled);

    // Master Arbitrator says, "Arena Three is now available for new warriors!"
    private static readonly Regex NpcSays = new(
        "^(?<name>[\\w\\s'-]+) says, \"(?<msg>.*)\"$", RegexOptions.Compiled);

    // Channel chat.
    // [Allegiance] <Tell:IIDString:0:PlayerName>PlayerName<\Tell> says, "kk"
    private static readonly Regex PlayerSaysChannel = new(
        "^\\[(?<channel>.+)]+ <Tell:IIDString:[0-9]+:(?<name>[\\w\\s'-]+)>[\\w\\s'-]+<\\\\Tell> says, \"(?<msg>.*)\"$",
        RegexOptions.Compiled);

    // Tells.
    // You tell PlayerName, "test"
    private static readonly Regex YouTell = new(
        "^You tell .+, \"(?<msg>.*)\"$", RegexOptions.Compiled);

    // <Tell:IIDString:1343111160:PlayerName>PlayerName<\Tell> tells you, "test"
    private static readonly Regex PlayerTellsYou = new(
        "^<Tell:IIDString:[0-9]+:(?<name>[\\w\\s'-]+)>[\\w\\s'-]+<\\\\Tell> tells you, \"(?<msg>.*)\"$",
        RegexOptions.Compiled);

    // Master Arbitrator tells you, "..."
    private static readonly Regex NpcTellsYou = new(
        "^(?<name>[\\w\\s'-]+) tells you, \"(?<msg>.*)\"$", RegexOptions.Compiled);

    [Flags]
    public enum ChatFlags : byte
    {
        None = 0x00,

        PlayerSaysLocal = 0x01,
        PlayerSaysChannel = 0x02,
        YouSay = 0x04,

        PlayerTellsYou = 0x08,
        YouTell = 0x10,

        NpcSays = 0x20,
        NpcTellsYou = 0x40,

        All = 0xFF,
    }

    /// <summary>True if the text was said by a person, envoy, npc, monster, etc.</summary>
    public static bool IsChat(string text, ChatFlags chatFlags = ChatFlags.All)
    {
        if ((chatFlags & ChatFlags.PlayerSaysLocal) == ChatFlags.PlayerSaysLocal
            && PlayerSaysLocal.IsMatch(text))
            return true;

        if ((chatFlags & ChatFlags.PlayerSaysChannel) == ChatFlags.PlayerSaysChannel
            && PlayerSaysChannel.IsMatch(text))
            return true;

        if ((chatFlags & ChatFlags.YouSay) == ChatFlags.YouSay && YouSay.IsMatch(text))
            return true;

        if ((chatFlags & ChatFlags.PlayerTellsYou) == ChatFlags.PlayerTellsYou
            && PlayerTellsYou.IsMatch(text))
            return true;

        if ((chatFlags & ChatFlags.YouTell) == ChatFlags.YouTell && YouTell.IsMatch(text))
            return true;

        if ((chatFlags & ChatFlags.NpcSays) == ChatFlags.NpcSays && NpcSays.IsMatch(text))
            return true;

        if ((chatFlags & ChatFlags.NpcTellsYou) == ChatFlags.NpcTellsYou
            && NpcTellsYou.IsMatch(text))
            return true;

        return false;
    }

    /// <summary>The name of the person/monster/npc of a chat message or tell.</summary>
    public static string GetSourceOfChat(string text)
    {
        bool isSays = IsChat(
            text, ChatFlags.NpcSays | ChatFlags.PlayerSaysChannel | ChatFlags.PlayerSaysLocal);
        bool isTell = IsChat(text, ChatFlags.NpcTellsYou | ChatFlags.PlayerTellsYou);

        if (isSays && isTell)
        {
            int indexOfSays = text.IndexOf(" says, \"", StringComparison.Ordinal);
            int indexOfTell = text.IndexOf(" tells you", StringComparison.Ordinal);

            if (indexOfSays <= indexOfTell)
                isTell = false;
            else
                isSays = false;
        }

        string source;
        if (isSays)
            source = text[..text.IndexOf(" says, \"", StringComparison.Ordinal)];
        else if (isTell)
            source = text[..text.IndexOf(" tells you", StringComparison.Ordinal)];
        else
            return string.Empty;

        source = source.Trim();

        if (source.Contains('>', StringComparison.Ordinal)
            && source.Contains('<', StringComparison.Ordinal))
        {
            source = source[(source.IndexOf('>', StringComparison.Ordinal) + 1)..];
            if (source.Contains('<', StringComparison.Ordinal))
                source = source[..source.IndexOf('<', StringComparison.Ordinal)];
        }

        return source;
    }

    [Flags]
    public enum ChatChannels : ushort
    {
        None = 0x0000,

        Area = 0x0001,
        Tells = 0x0002,

        Fellowship = 0x0004,
        Allegiance = 0x0008,
        General = 0x0010,
        Trade = 0x0020,
        LFG = 0x0040,
        Roleplay = 0x0080,
        Society = 0x0100,

        All = 0xFFFF,
    }

    public static ChatChannels GetChatChannel(string text)
    {
        if (IsChat(text, ChatFlags.PlayerSaysLocal | ChatFlags.YouSay | ChatFlags.NpcSays))
            return ChatChannels.Area;

        if (IsChat(text, ChatFlags.PlayerTellsYou | ChatFlags.YouTell | ChatFlags.NpcTellsYou))
            return ChatChannels.Tells;

        if (IsChat(text, ChatFlags.PlayerSaysChannel))
        {
            Match match = PlayerSaysChannel.Match(text);
            if (match.Success)
            {
                string channel = match.Groups["channel"].Value;
                return channel switch
                {
                    "Fellowship" => ChatChannels.Fellowship,
                    "Allegiance" => ChatChannels.Allegiance,
                    "General" => ChatChannels.General,
                    "Trade" => ChatChannels.Trade,
                    "LFG" => ChatChannels.LFG,
                    "Roleplay" => ChatChannels.Roleplay,
                    "Society" => ChatChannels.Society,
                    _ => ChatChannels.None,
                };
            }
        }

        return ChatChannels.None;
    }

    /// <summary>
    /// Converts <c>[Allegiance] &lt;Tell:IIDString:0:PlayerName&gt;PlayerName&lt;\Tell&gt; says, "kk"</c>
    /// to <c>[Allegiance] PlayerName says, "kk"</c>.
    /// </summary>
    public static string CleanMessage(string text)
    {
        string output = text;

        int ltIndex = output.IndexOf('<', StringComparison.Ordinal);
        int gtIndex = output.IndexOf('>', StringComparison.Ordinal);
        int cIndex = output.IndexOf(',', StringComparison.Ordinal);

        if (ltIndex != -1 && ltIndex < gtIndex && gtIndex < cIndex)
            output = output[..ltIndex] + output[(gtIndex + 1)..];

        ltIndex = output.IndexOf('<', StringComparison.Ordinal);
        gtIndex = output.IndexOf('>', StringComparison.Ordinal);
        cIndex = output.IndexOf(',', StringComparison.Ordinal);

        if (ltIndex != -1 && ltIndex < gtIndex && gtIndex < cIndex)
            output = output[..ltIndex] + output[(gtIndex + 1)..];

        return output;
    }

    // You say, "Zojak ...."
    private static readonly Regex YouSaySpellCast = new(
        "^You say, \"(Zojak|Malar|Puish|Cruath|Volae|Quavosh|Shurov|Boquar|Helkas|Equin|"
        + "Roiga|Malar|Jevak|Tugak|Slavu|Drostu|Traku|Yanoi|Drosta|Feazh) .*\"$",
        RegexOptions.Compiled);

    // Player says, "Zojak ...."
    private static readonly Regex PlayerSaysSpellCast = new(
        "^<Tell:IIDString:[0-9]+:(?<name>[\\w\\s'-]+)>[\\w\\s'-]+<\\\\Tell> says, \"(Zojak|Malar|"
        + "Puish|Cruath|Volae|Quavosh|Shurov|Boquar|Helkas|Equin|Roiga|Malar|Jevak|Tugak|Slavu|"
        + "Drostu|Traku|Yanoi|Drosta|Feazh) .*\"$",
        RegexOptions.Compiled);

    /// <summary>True for "You say, \"Zojak...\"" or "Somebody says, \"Zojak...\"".</summary>
    public static bool IsSpellCastingMessage(string text, bool isMine = true, bool isPlayer = true)
    {
        if (isMine && YouSaySpellCast.IsMatch(text))
            return true;

        if (isPlayer && PlayerSaysSpellCast.IsMatch(text))
            return true;

        return false;
    }
}
