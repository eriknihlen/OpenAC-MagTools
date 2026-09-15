using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Chat;

/// <summary>
/// Port of the original's <c>ChatFilter</c>: one <see cref="IPluginChat.RegisterFilter"/>
/// registration that runs every one of the 29 <c>Filters/*</c> rules, plus the
/// three status-text rules matched on <see cref="PluginChatMessage.StatusTextKind"/>.
/// </summary>
/// <remarks>
/// Filters are client-wide: this only ever suppresses what a rule specifically
/// says, never more. Every condition below is the original's condition,
/// verbatim, including the handful that deliberately do NOT gate on
/// <see cref="ChatClassifier.IsChat"/> (Monster Deaths and both spell-casting
/// rules match on their own dedicated pattern instead).
/// </remarks>
public sealed class ChatFilter
{
    private readonly IPluginHost _host;
    private readonly FilterSettings _settings;
    private IDisposable? _registration;

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
    }

    private bool ShouldSuppress(PluginChatMessage message)
    {
        string text = message.Text;
        if (string.IsNullOrEmpty(text))
            return false;

        if (message.Kind == PluginChatMessage.StatusTextKind)
            return SuppressStatusText(text);

        // A per-message lookup cache: several rules below (VendorTells,
        // MonsterTell, NpcChatter) resolve the same speaker name to a world
        // object class, so this avoids repeating CaptureObjects() for each one
        // while evaluating a single line.
        var speakerClassCache = new Dictionary<string, PluginObjectClass>(
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<PluginWorldObject>? objectsCache = null;

        bool isChat = ChatClassifier.IsChat(text);

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

        // Deliberately not gated on isChat: You say/X says IS chat, but the
        // original matched the spell-casting shape directly.
        if (_settings.SpellCastingMine.Value
            && ChatClassifier.IsSpellCastingMessage(text, isMine: true, isPlayer: false))
            return true;

        if (_settings.SpellCastingOthers.Value
            && ChatClassifier.IsSpellCastingMessage(text, isMine: false))
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
            if (ChatClassifier.IsChat(text, ChatClassifier.ChatFlags.PlayerSaysLocal)
                && (text.TrimEnd().EndsWith("-t-\"", StringComparison.Ordinal)
                    || text.TrimEnd().EndsWith("-b-\"", StringComparison.Ordinal)))
                return true;

            if (!isChat
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
            && ChatClassifier.IsChat(text, ChatClassifier.ChatFlags.NpcTellsYou)
            && ResolveSpeakerClass(ChatClassifier.GetSourceOfChat(text)) == PluginObjectClass.Vendor)
            return true;

        if (_settings.MonsterTell.Value
            && ChatClassifier.IsChat(text, ChatClassifier.ChatFlags.NpcTellsYou)
            && ResolveSpeakerClass(ChatClassifier.GetSourceOfChat(text)) == PluginObjectClass.Monster)
            return true;

        if (_settings.NpcChatter.Value
            && ChatClassifier.IsChat(text, ChatClassifier.ChatFlags.NpcSays)
            && ResolveSpeakerClass(ChatClassifier.GetSourceOfChat(text)) == PluginObjectClass.Npc)
            return true;

        if (_settings.MasterArbitratorSpam.Value || _settings.AllMasterArbitratorChat.Value)
        {
            if (isChat && ChatClassifier.GetSourceOfChat(text) == "Master Arbitrator")
            {
                if (_settings.MasterArbitratorSpam.Value)
                {
                    if (ChatClassifier.IsChat(text, ChatClassifier.ChatFlags.NpcTellsYou))
                    {
                        if (text.Contains("\"You shall be known", StringComparison.Ordinal)
                            || text.Contains("\"Take this knowledge", StringComparison.Ordinal)
                            || text.Contains("\"Use the the key", StringComparison.Ordinal))
                            return true;
                    }
                    else if (ChatClassifier.IsChat(
                        text, ChatClassifier.ChatFlags.PlayerSaysChannel))
                    {
                        if (text.Contains("\"Your fellowship", StringComparison.Ordinal)
                            || text.Contains("\"Use one of the", StringComparison.Ordinal)
                            || text.Contains("\"Don't forget", StringComparison.Ordinal))
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

        PluginObjectClass? ResolveSpeakerClass(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (speakerClassCache.TryGetValue(name, out PluginObjectClass cached))
                return cached;

            objectsCache ??= _host.Automation.Objects.CaptureObjects();
            foreach (PluginWorldObject candidate in objectsCache)
            {
                if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                speakerClassCache[name] = candidate.ObjectClass;
                return candidate.ObjectClass;
            }

            return null;
        }
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
}
