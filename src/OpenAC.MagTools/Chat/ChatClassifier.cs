using System.Text.RegularExpressions;
using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Chat;

/// <summary>
/// The <see cref="ChatMessageKind"/> a <see cref="PluginChatMessage.Kind"/>
/// carries, named per the table in <c>docs/plugin-api.md</c>. Casting the raw
/// int to this enum is safe because that table is a documented, host-stable
/// contract — see <see cref="PluginChatMessage.StatusTextKind"/> for the one
/// value (100) that sits outside it.
/// </summary>
public enum ChatMessageKind
{
    LocalSpeech = 0,
    RangedSpeech = 1,
    Channel = 2,
    Tell = 3,
    System = 4,
    Popup = 5,
    Emote = 6,
    SoulEmote = 7,
    Combat = 8,
}

/// <summary>
/// Classifies a <see cref="PluginChatMessage"/> from its structured fields —
/// <see cref="PluginChatMessage.Kind"/>, <see cref="PluginChatMessage.Sender"/>,
/// <see cref="PluginChatMessage.SenderObjectId"/> and
/// <see cref="PluginChatMessage.ChannelName"/> — instead of parsing retail's
/// composed display text. The original Decal plugin only ever saw that
/// composed text (<c>You say, "..."</c>, <c>&lt;Tell:IIDString:..&gt;Name...
/// tells you, "..."</c>) and had to regex it back apart; the host already
/// hands a plugin the parts, so <see cref="Classify"/> reads them directly.
/// </summary>
public static class ChatClassifier
{
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

    // Spell words a cast's incantation starts with — retail's "Zojak
    // arwreth ..." shape. A word must be followed by a space, so "Zojakx"
    // does not match.
    private static readonly string[] SpellWords =
    [
        "Zojak", "Malar", "Puish", "Cruath", "Volae", "Quavosh", "Shurov",
        "Boquar", "Helkas", "Equin", "Roiga", "Jevak", "Tugak", "Slavu",
        "Drostu", "Traku", "Yanoi", "Drosta", "Feazh",
    ];

    /// <summary>
    /// One classified chat line: what the original derived by regexing
    /// retail's composed display text, computed instead from the structured
    /// fields the host hands a plugin.
    /// </summary>
    /// <param name="Kind">The line's wire kind.</param>
    /// <param name="Source">
    /// The speaker's name, taken from <see cref="PluginChatMessage.Sender"/>
    /// verbatim. For a tell the local player sent, the host's own
    /// <c>Sender</c> field holds the recipient's name instead (see
    /// <c>ChatLog.OnSelfSent</c>) — <see cref="IsMine"/> already accounts for
    /// that quirk on <see cref="ChatMessageKind.Tell"/>, so a caller checking
    /// <see cref="IsMine"/> first never misreads a recipient as a speaker.
    /// </param>
    /// <param name="Channel">
    /// <see cref="ChatChannels.Area"/> for local/ranged speech,
    /// <see cref="ChatChannels.Tells"/> for a tell either direction, the
    /// named channel for <see cref="ChatMessageKind.Channel"/>, or
    /// <see cref="ChatChannels.None"/> for anything else.
    /// </param>
    /// <param name="IsChat">
    /// True for player/NPC speech of any kind — local say, channel say, a
    /// tell either direction. False for system text, combat lines, emotes,
    /// and status notices, exactly as the original's <c>isChat</c> local
    /// gated everything that was not a "says"/"tells you" line.
    /// </param>
    /// <param name="IsMine">The local player is the speaker.</param>
    /// <param name="SpeakerClass">
    /// The speaker's resolved world-object class, or null when it could not
    /// be resolved. <see cref="IsNpc"/> is the three classes retail chatter
    /// cares about; a rule that needs to tell a vendor from a monster reads
    /// this directly.
    /// </param>
    /// <param name="Message">The bare message body — no markup, no wrapper.</param>
    public readonly record struct ChatLine(
        ChatMessageKind Kind,
        string Source,
        ChatChannels Channel,
        bool IsChat,
        bool IsMine,
        PluginObjectClass? SpeakerClass,
        string Message,
        uint SenderObjectId,
        int LogTextType,
        int CombatKind)
    {
        /// <summary>The speaker is a vendor, monster, or plain NPC.</summary>
        public bool IsNpc => SpeakerClass is PluginObjectClass.Npc
            or PluginObjectClass.Vendor
            or PluginObjectClass.Monster;

        /// <summary>
        /// True for a chat line whose body opens with a spell incantation
        /// word — the original's "You say/X says, &quot;Zojak ...&quot;"
        /// shape — restricted by <paramref name="mine"/> /
        /// <paramref name="others"/> to which speaker it should match for.
        /// </summary>
        public bool IsSpellCast(bool mine, bool others)
        {
            if (!IsChat)
                return false;
            if (!(IsMine ? mine : others))
                return false;

            return StartsWithSpellWord(Message);
        }

        private static bool StartsWithSpellWord(string message)
        {
            foreach (string word in SpellWords)
            {
                if (message.Length > word.Length
                    && message[word.Length] == ' '
                    && message.StartsWith(word, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Classifies <paramref name="message"/> from its structured fields.
    /// <paramref name="resolveSpeakerByName"/> is consulted only when
    /// <see cref="PluginChatMessage.SenderObjectId"/> is 0 (a self-sent tell,
    /// or the rare System-shaped fallback below); a caller that has no class
    /// rule enabled should pass null so no name-based lookup ever runs.
    /// </summary>
    public static ChatLine Classify(
        in PluginChatMessage message,
        IAutomationSurface automation,
        Func<string, PluginObjectClass?>? resolveSpeakerByName = null)
    {
        ArgumentNullException.ThrowIfNull(automation);

        var kind = (ChatMessageKind)message.Kind;
        string source = message.Sender;
        string text = message.Text;
        bool isChat = IsChatKind(kind);
        ChatChannels channel = ChannelFor(kind, message.ChannelName);
        bool isMine = IsMineFor(kind, message.Sender, message.SenderObjectId, automation);

        // Rare host edge case: a handful of server-composed lines (scripted
        // NPC dialogue, some quest text) arrive with Kind == System instead
        // of riding the structured speech path, but are still shaped exactly
        // like retail's own "X says, ..." / "X tells you, ..." display text.
        // Recognize that shape with the legacy regexes so a filter written
        // against NPC chatter still catches it — this is the only production
        // use of those regexes; see their remarks for the other one (a
        // hand-authored legacy import file using the old decorated-text
        // shape rather than the current comma-separated log format).
        if (kind == ChatMessageKind.System && IsChat(text))
        {
            isChat = true;
            channel = GetChatChannel(text);
            source = GetSourceOfChat(text);
            isMine = string.IsNullOrEmpty(source);
        }

        PluginObjectClass? speakerClass = ResolveSpeakerClass(
            message.SenderObjectId, source, automation, resolveSpeakerByName);

        return new ChatLine(
            kind,
            source,
            channel,
            isChat,
            isMine,
            speakerClass,
            text,
            message.SenderObjectId,
            message.LogTextType,
            message.CombatKind);
    }

    private static bool IsChatKind(ChatMessageKind kind) => kind
        is ChatMessageKind.LocalSpeech
        or ChatMessageKind.RangedSpeech
        or ChatMessageKind.Channel
        or ChatMessageKind.Tell;

    private static ChatChannels ChannelFor(ChatMessageKind kind, string channelName) => kind switch
    {
        ChatMessageKind.LocalSpeech or ChatMessageKind.RangedSpeech => ChatChannels.Area,
        ChatMessageKind.Tell => ChatChannels.Tells,
        ChatMessageKind.Channel => ResolveNamedChannel(channelName),
        _ => ChatChannels.None,
    };

    private static ChatChannels ResolveNamedChannel(string channelName) => channelName switch
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

    /// <summary>
    /// Mirrors <c>ChatLog</c>'s own "who is speaking" rules per kind: local
    /// and ranged speech (and the two emote kinds, which share the same
    /// adapter shape) carry the real speaker guid and rewrite
    /// <c>Sender</c> to "You" (or leave it empty) for the local player's own
    /// echo; a received channel broadcast never carries a sender guid at
    /// all, so self is told apart by name; a tell's <c>SenderObjectId</c> is
    /// 0 only for the local player's own self-sent line (the host's
    /// <c>OnSelfSent</c> puts the recipient's name in <c>Sender</c> there,
    /// which is why <see cref="ChatLine.Source"/> is not "speaker name" in
    /// that one case).
    /// </summary>
    private static bool IsMineFor(
        ChatMessageKind kind,
        string sender,
        uint senderObjectId,
        IAutomationSurface automation) => kind switch
    {
        ChatMessageKind.Tell => senderObjectId == 0,
        ChatMessageKind.LocalSpeech
            or ChatMessageKind.RangedSpeech
            or ChatMessageKind.Emote
            or ChatMessageKind.SoulEmote =>
            IsOwnSpeakerName(sender)
                || (senderObjectId != 0 && senderObjectId == automation.Character.ObjectId),
        ChatMessageKind.Channel =>
            IsOwnSpeakerName(sender)
                || string.Equals(
                    sender, automation.Character.Name, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool IsOwnSpeakerName(string sender) =>
        string.IsNullOrEmpty(sender) || string.Equals(sender, "You", StringComparison.Ordinal);

    private static PluginObjectClass? ResolveSpeakerClass(
        uint senderObjectId,
        string source,
        IAutomationSurface automation,
        Func<string, PluginObjectClass?>? resolveSpeakerByName)
    {
        if (senderObjectId != 0)
        {
            return automation.Objects.TryGet(senderObjectId, out PluginWorldObject worldObject)
                ? worldObject.ObjectClass
                : null;
        }

        if (resolveSpeakerByName is null || string.IsNullOrEmpty(source))
            return null;

        return resolveSpeakerByName(source);
    }

    // ── Legacy text-shape regexes ───────────────────────────────────────────
    //
    // These parse retail's COMPOSED display text (the shape the original
    // Decal plugin's Current_ChatBoxMessage handler saw, and the shape a
    // hand-authored legacy import file still uses). Production classification
    // goes through Classify() above and never touches these except for the
    // one System-shaped fallback documented there. Kept for:
    //   (a) importing an old, hand-edited legacy chat log that predates the
    //       current comma-separated LoggedChatEntry format, and
    //   (b) that System-kind fallback in Classify().

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

    /// <summary>
    /// Legacy import/fallback only (see the remarks above) — true if
    /// retail's COMPOSED text was said by a person, envoy, npc, monster, etc.
    /// </summary>
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

    /// <summary>Legacy import/fallback only — the name of the speaker of a COMPOSED chat line.</summary>
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

    /// <summary>Legacy import/fallback only — the channel of a COMPOSED chat line.</summary>
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
    /// Legacy import only. Converts
    /// <c>[Allegiance] &lt;Tell:IIDString:0:PlayerName&gt;PlayerName&lt;\Tell&gt; says, "kk"</c>
    /// to <c>[Allegiance] PlayerName says, "kk"</c>. Still used by
    /// <c>ChatLogFileStore</c>/<c>LoggerPageViewModels</c> to clean the
    /// (already-bare, markup-free) current-format message for display —
    /// harmless there since a markup-free line has nothing to strip.
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

    /// <summary>
    /// Legacy import/fallback only — true for COMPOSED text shaped like
    /// "You say, \"Zojak...\"" or "Somebody says, \"Zojak...\"". Production
    /// spell-cast detection is <see cref="ChatLine.IsSpellCast"/>.
    /// </summary>
    public static bool IsSpellCastingMessage(string text, bool isMine = true, bool isPlayer = true)
    {
        if (isMine && YouSaySpellCast.IsMatch(text))
            return true;

        if (isPlayer && PlayerSaysSpellCast.IsMatch(text))
            return true;

        return false;
    }
}
