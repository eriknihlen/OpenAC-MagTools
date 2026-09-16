using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using ChatLine = OpenAC.MagTools.Chat.ChatClassifier.ChatLine;

namespace OpenAC.MagTools.Chat;

/// <summary>
/// Port of the original's <c>ChatFilter</c>: one <see cref="IPluginChat.RegisterFilter"/>
/// registration that runs every one of the 29 <c>Filters/*</c> rules, plus the
/// three status-text rules matched on <see cref="PluginChatMessage.StatusTextKind"/>.
/// </summary>
/// <remarks>
/// Filters are client-wide: this only ever suppresses what a rule specifically
/// says, never more. Every condition below is the original's condition
/// translated onto <see cref="ChatClassifier.ChatLine"/>'s structured fields
/// instead of the original's composed-text regexes, including the handful
/// that deliberately do NOT gate on <see cref="ChatLine.IsChat"/> (Monster
/// Deaths and both spell-casting rules match on their own dedicated shape
/// instead).
/// </remarks>
public sealed class ChatFilter
{
    private readonly IPluginHost _host;
    private readonly FilterSettings _settings;
    private IDisposable? _registration;

    /// <summary>
    /// Speaker name to resolved world-object class, built lazily and only
    /// while at least one of the three class-based rules is on
    /// (<see cref="NeedsSpeakerClassByName"/>). <see cref="ChatClassifier.Classify"/>
    /// only ever consults this for a Tell/LocalSpeech/RangedSpeech line whose
    /// <c>SenderObjectId</c> is 0 (a self-sent tell, or plain local/ranged
    /// speech with no id) — never a channel line, whose <c>SenderObjectId</c>
    /// is also always 0 but is, in practice, never an NPC. The common path
    /// (a real <c>SenderObjectId</c>) resolves a speaker in O(1) via
    /// <see cref="IWorldObjectAutomation.TryGet"/> and never touches this
    /// cache at all. Cleared on <see cref="OnLogoff"/> as well as
    /// <see cref="Disable"/>, since object ids and names from a previous
    /// session are not guaranteed to mean the same thing in a new one.
    /// </summary>
    private Dictionary<string, PluginObjectClass>? _speakerClassByName;

    public ChatFilter(IPluginHost host, SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _settings = settings.Filters;
    }

    public void Enable() => _registration ??= _host.Automation.Chat.RegisterFilter(ShouldSuppress);

    public void Disable()
    {
        _registration?.Dispose();
        _registration = null;
        _speakerClassByName = null;
    }

    /// <summary>
    /// Drops the speaker-class-by-name cache without touching the filter
    /// registration itself — called on session logoff so a name→class
    /// mapping from the session that just ended cannot leak into the next
    /// one (object ids and even names are not guaranteed stable across a
    /// reconnect).
    /// </summary>
    public void OnLogoff() => _speakerClassByName = null;

    private bool NeedsSpeakerClassByName =>
        _settings.VendorTells.Value || _settings.MonsterTell.Value || _settings.NpcChatter.Value;

    private bool ShouldSuppress(PluginChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Text))
            return false;

        if (message.Kind == PluginChatMessage.StatusTextKind)
            return SuppressStatusText(message.Text);

        ChatLine line = ChatClassifier.Classify(
            message,
            _host.Automation,
            NeedsSpeakerClassByName ? ResolveSpeakerClassByName : null);
        string text = line.Message;
        bool isChat = line.IsChat;

        if (_settings.AttackEvades.Value
            && !isChat && text.Contains(" evaded your attack.", StringComparison.Ordinal))
            return true;

        if (_settings.DefenseEvades.Value
            && !isChat && text.StartsWith("You evaded ", StringComparison.Ordinal))
            return true;

        if (_settings.AttackResists.Value
            && !isChat && text.Contains(" resists your spell", StringComparison.Ordinal))
            return true;

        if (_settings.DefenseResists.Value && !isChat)
        {
            if (text.StartsWith("You resist the spell cast by ", StringComparison.Ordinal))
                return true;
            if (text.StartsWith("You have no appropriate target", StringComparison.Ordinal)
                && text.Contains("spell", StringComparison.Ordinal))
                return true;
            if (text.StartsWith(
                "You are an invalid target for the spell", StringComparison.Ordinal))
                return true;
            if (text.Contains(
                "tried to cast a spell on you, but was too far away!", StringComparison.Ordinal))
                return true;
        }

        if (_settings.NPKFails.Value && !isChat)
        {
            if (text.StartsWith("You fail to affect ", StringComparison.Ordinal)
                && text.Contains(" you are not a player killer!", StringComparison.Ordinal))
                return true;
            if (text.Contains("fails to affect you", StringComparison.Ordinal)
                && text.Contains(" is not a player killer!", StringComparison.Ordinal))
                return true;
        }

        if (_settings.DirtyFighting.Value
            && !isChat
            && text.StartsWith("Dirty Fighting! ", StringComparison.Ordinal)
            && text.Contains(" delivers a ", StringComparison.Ordinal))
            return true;

        // Deliberately not gated on isChat: the original checked the kill
        // pattern unconditionally.
        if (_settings.MonsterDeaths.Value && CombatMessages.IsKilledByMeMessage(text))
            return true;

        if (_settings.SpellCastingMine.Value && line.IsSpellCast(mine: true, others: false))
            return true;

        if (_settings.SpellCastingOthers.Value && line.IsSpellCast(mine: false, others: true))
            return true;

        if (_settings.SpellCastFizzles.Value
            && !isChat && text.StartsWith("Your spell fizzled.", StringComparison.Ordinal))
            return true;

        if (_settings.CompUsage.Value
            && !isChat
            && text.StartsWith("The spell consumed the following components", StringComparison.Ordinal))
            return true;

        if (_settings.SpellExpires.Value
            && !isChat
            && !text.Contains("Brilliance", StringComparison.Ordinal)
            && !text.Contains("Prodigal", StringComparison.Ordinal)
            && !text.Contains("Spectral", StringComparison.Ordinal)
            && (text.Contains("has expired.", StringComparison.Ordinal)
                || text.Contains("have expired.", StringComparison.Ordinal)))
            return true;

        if (_settings.HealingKitSuccess.Value
            && !isChat
            && text.StartsWith("You ", StringComparison.Ordinal)
            && text.Contains(" heal yourself for ", StringComparison.Ordinal))
            return true;

        if (_settings.HealingKitFail.Value
            && !isChat
            && text.StartsWith("You fail to heal yourself. ", StringComparison.Ordinal))
            return true;

        if (_settings.Salvaging.Value
            && !isChat
            && text.StartsWith("You obtain ", StringComparison.Ordinal)
            && text.Contains(" using your knowledge of ", StringComparison.Ordinal))
            return true;

        if (_settings.SalvagingFails.Value && !isChat)
        {
            if (text.StartsWith("Salvaging Failed!", StringComparison.Ordinal))
                return true;
            if (text.Contains(
                "The following were not suitable for salvaging: ", StringComparison.Ordinal))
                return true;
        }

        if (_settings.AuraOfCraftman.Value
            && !isChat
            && text.StartsWith(
                "Your Aura of the Craftman augmentation increased your skill by 5!",
                StringComparison.Ordinal))
            return true;

        if (_settings.ManaStoneUsage.Value && !isChat)
        {
            if (text.StartsWith("The Mana Stone gives ", StringComparison.Ordinal))
                return true;
            if (text.StartsWith("You need ", StringComparison.Ordinal)
                && text.TrimEnd().EndsWith(
                    " more mana to fully charge your items.", StringComparison.Ordinal))
                return true;
            if (text.StartsWith("The Mana Stone drains ", StringComparison.Ordinal))
                return true;
            if (text.StartsWith("The ", StringComparison.Ordinal)
                && text.TrimEnd().EndsWith(" is destroyed.", StringComparison.Ordinal))
                return true;
        }

        if (_settings.TradeBuffBotSpam.Value)
        {
            if (line.IsMine && isChat
                && (text.TrimEnd().EndsWith("-t-", StringComparison.Ordinal)
                    || text.TrimEnd().EndsWith("-b-", StringComparison.Ordinal)))
                return true;

            if (!isChat
                && line.Kind is ChatMessageKind.Emote or ChatMessageKind.SoulEmote
                && (text.TrimEnd().EndsWith("-t-", StringComparison.Ordinal)
                    || text.TrimEnd().EndsWith("-b-", StringComparison.Ordinal)))
                return true;
        }

        if (_settings.FailedAssess.Value
            && !isChat
            && text.TrimEnd().EndsWith("tried and failed to assess you!", StringComparison.Ordinal))
            return true;

        if (_settings.KillTaskComplete.Value
            && !isChat
            && text.StartsWith("You have killed ", StringComparison.Ordinal)
            && text.TrimEnd().EndsWith("Your task is complete!", StringComparison.Ordinal))
            return true;

        if (_settings.VendorTells.Value
            && line.Kind == ChatMessageKind.Tell && !line.IsMine
            && line.SpeakerClass == PluginObjectClass.Vendor)
            return true;

        if (_settings.MonsterTell.Value
            && line.Kind == ChatMessageKind.Tell && !line.IsMine
            && line.SpeakerClass == PluginObjectClass.Monster)
            return true;

        if (_settings.NpcChatter.Value
            && line.Kind is ChatMessageKind.LocalSpeech or ChatMessageKind.RangedSpeech
            && !line.IsMine
            && line.SpeakerClass == PluginObjectClass.Npc)
            return true;

        if (_settings.MasterArbitratorSpam.Value || _settings.AllMasterArbitratorChat.Value)
        {
            if (isChat && line.Source == "Master Arbitrator")
            {
                if (_settings.MasterArbitratorSpam.Value)
                {
                    if (line.Kind == ChatMessageKind.Tell && !line.IsMine)
                    {
                        if (text.Contains("You shall be known", StringComparison.Ordinal)
                            || text.Contains("Take this knowledge", StringComparison.Ordinal)
                            || text.Contains("Use the the key", StringComparison.Ordinal))
                            return true;
                    }
                    else if (line.Kind == ChatMessageKind.Channel)
                    {
                        if (text.Contains("Your fellowship", StringComparison.Ordinal)
                            || text.Contains("Use one of the", StringComparison.Ordinal)
                            || text.Contains("Don't forget", StringComparison.Ordinal))
                            return true;
                    }
                }

                if (_settings.AllMasterArbitratorChat.Value)
                    return true;
            }

            if (_settings.AllMasterArbitratorChat.Value
                && text.StartsWith("Your fellowship is now locked.", StringComparison.Ordinal)
                && text.Contains("you have 15 minutes to be recruited", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private bool SuppressStatusText(string text)
    {
        // You're too busy!
        if (_settings.StatusTextYoureTooBusy.Value
            && string.Equals(text, "You're too busy!", StringComparison.Ordinal))
            return true;

        // Casting .....
        if (_settings.StatusTextCasting.Value
            && text.StartsWith("Casting ", StringComparison.Ordinal))
            return true;

        if (_settings.StatusTextAll.Value)
            return true;

        return false;
    }

    /// <summary>
    /// Fallback speaker-class lookup for a line whose <c>SenderObjectId</c>
    /// is 0 (a legacy/self-sent shape). Built once, on first miss, from
    /// <see cref="IWorldObjectAutomation.CaptureObjects"/> and kept for the
    /// filter's lifetime — a bulk build costs at most once per session
    /// instead of once per chat line, since <see cref="Enable"/> only wires
    /// this in when a class-based rule is actually on.
    /// </summary>
    private PluginObjectClass? ResolveSpeakerClassByName(string name)
    {
        _speakerClassByName ??= BuildSpeakerClassByName();
        return _speakerClassByName.TryGetValue(name, out PluginObjectClass found)
            ? found
            : null;
    }

    private Dictionary<string, PluginObjectClass> BuildSpeakerClassByName()
    {
        var byName = new Dictionary<string, PluginObjectClass>(StringComparer.OrdinalIgnoreCase);
        foreach (PluginWorldObject candidate in _host.Automation.Objects.CaptureObjects())
            byName.TryAdd(candidate.Name, candidate.ObjectClass);
        return byName;
    }
}
