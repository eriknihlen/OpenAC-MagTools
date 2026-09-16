using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Chat;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Chat;

/// <summary>
/// Builds <see cref="PluginChatMessage"/>s in the host's actual shape —
/// explicit <c>Kind</c>/<c>Sender</c>/<c>SenderObjectId</c>/<c>ChannelName</c>
/// — instead of retail's composed display text, mirroring exactly what
/// <c>ChatLog</c>'s adapters (<c>OnLocalSpeech</c>, <c>OnTellReceived</c>,
/// <c>OnSelfSent</c>, <c>OnChannelBroadcast</c>, <c>OnEmote</c>/
/// <c>OnSoulEmote</c>) put on the wire. Keep the Decal-shaped composed
/// strings only in the legacy-import tests.
/// </summary>
internal static class ChatFixtures
{
    /// <summary>
    /// A stand-in world-object id in the player range (see
    /// <c>ChatVM.FirstPlayerObjectId</c>/<c>LastPlayerObjectId</c>) — any
    /// NPC test that needs a specific class should register its own object
    /// via <c>FakeObjects.Objects</c> and pass that id instead.
    /// </summary>
    public const uint DefaultSpeakerObjectId = 0x50000101u;

    /// <summary>A tell someone else sent the local player.</summary>
    public static PluginChatMessage Tell(
        this FakeHost host, string from, string text, uint fromObjectId = DefaultSpeakerObjectId)
        => host.Automation.Chat.Deliver(
            text,
            kind: (int)ChatMessageKind.Tell,
            sender: from,
            senderObjectId: fromObjectId);

    /// <summary>
    /// A tell the local player sent. <c>ChatLog.OnSelfSent</c> puts the
    /// RECIPIENT's name in <c>Sender</c> here (with <c>SenderObjectId</c> 0)
    /// — that quirk is exactly what <c>ChatLine.IsMine</c> exists to hide.
    /// </summary>
    public static PluginChatMessage YouTell(this FakeHost host, string to, string text)
        => host.Automation.Chat.Deliver(
            text, kind: (int)ChatMessageKind.Tell, sender: to, senderObjectId: 0u);

    /// <summary>
    /// Local (or, with <paramref name="ranged"/>, shouted) speech.
    /// <paramref name="mine"/> mirrors <c>ChatLog.OnLocalSpeech</c>'s own
    /// echo: only <c>Sender</c> becomes "You" — <c>SenderObjectId</c> still
    /// carries the real speaker guid either way (<c>OnLocalSpeech</c> never
    /// zeroes it, even for the local player's own echo).
    /// </summary>
    public static PluginChatMessage Say(
        this FakeHost host,
        string from,
        string text,
        bool mine = false,
        uint fromObjectId = DefaultSpeakerObjectId,
        bool ranged = false)
        => host.Automation.Chat.Deliver(
            text,
            kind: (int)(ranged ? ChatMessageKind.RangedSpeech : ChatMessageKind.LocalSpeech),
            sender: mine ? "You" : from,
            senderObjectId: fromObjectId);

    /// <summary>
    /// A named-channel line. A received broadcast never carries a sender
    /// guid (<c>ChatLog.OnChannelBroadcast</c> hardcodes
    /// <c>SenderGuid: 0</c>), and the local player's own non-echoed send
    /// (<c>OnSelfSent</c>) leaves <c>Sender</c> empty — both match
    /// <paramref name="mine"/>'s two shapes here.
    /// </summary>
    public static PluginChatMessage ChannelSay(
        this FakeHost host, string channel, string from, string text, bool mine = false)
        => host.Automation.Chat.Deliver(
            text,
            kind: (int)ChatMessageKind.Channel,
            sender: mine ? string.Empty : from,
            channelName: channel);

    /// <summary>A server system-text line (never chat).</summary>
    public static PluginChatMessage System(this FakeHost host, string text)
        => host.Automation.Chat.Deliver(text, kind: (int)ChatMessageKind.System);

    /// <summary>A combat-tracker line (never chat).</summary>
    public static PluginChatMessage Combat(this FakeHost host, string text)
        => host.Automation.Chat.Deliver(text, kind: (int)ChatMessageKind.Combat);

    /// <summary>A third-person emote (never chat, even the local player's own).</summary>
    public static PluginChatMessage Emote(
        this FakeHost host,
        string from,
        string text,
        bool mine = false,
        uint fromObjectId = DefaultSpeakerObjectId)
        => host.Automation.Chat.Deliver(
            text,
            kind: (int)ChatMessageKind.Emote,
            sender: mine ? "You" : from,
            senderObjectId: mine ? 0u : fromObjectId);

    /// <summary>A short status notice (<see cref="PluginChatMessage.StatusTextKind"/>).</summary>
    public static PluginChatMessage Status(this FakeHost host, string text)
        => host.Automation.Chat.Deliver(text, kind: PluginChatMessage.StatusTextKind);
}
