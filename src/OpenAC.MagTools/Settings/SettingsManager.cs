using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenAC.MagTools.Settings;

/// <summary>
/// One setting reachable as <c>Group.Field</c>, which is what
/// <c>/mt opt list|get|set</c> speaks and what the Options and Filters pages
/// iterate.
/// </summary>
public sealed record SettingDescriptor(
    string GroupName,
    string FieldName,
    ISetting Setting)
{
    public string QualifiedName => GroupName + "." + FieldName;
}

/// <summary>
/// Every Mag-Tools setting, grouped the way the original grouped them.
/// </summary>
/// <remarks>
/// Five of the original's settings are deliberately absent because the
/// behaviour they controlled does not exist in OpenAC: RemoveWindowFrame,
/// WindowPositions, NoFocusFPS, MaxFPS and MaximizeChatOnLogin were all Win32
/// or blind-pixel hacks against the retail client's own window.
/// </remarks>
public sealed class SettingsManager
{
    public SettingsManager(SettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;

        ManaManagement = new ManaManagementSettings(file);
        AutoBuySell = new AutoBuySellSettings(file);
        AutoTradeAdd = new AutoTradeAddSettings(file);
        AutoTradeAccept = new AutoTradeAcceptSettings(file);
        Looting = new LootingSettings(file);
        Tinkering = new TinkeringSettings(file);
        InventoryManagement = new InventoryManagementSettings(file);
        ItemInfoOnIdent = new ItemInfoOnIdentSettings(file);
        CombatTracker = new CombatTrackerSettings(file);
        CorpseTracker = new CorpseTrackerSettings(file);
        PlayerTracker = new PlayerTrackerSettings(file);
        ChatLogger = new ChatLoggerSettings(file);
        Misc = new MiscSettings(file);
        Filters = new FilterSettings(file);
        Commands = new ScopedCommandStore(file);

        All = BuildDescriptors();
    }

    public SettingsFile File { get; }

    public ManaManagementSettings ManaManagement { get; }
    public AutoBuySellSettings AutoBuySell { get; }
    public AutoTradeAddSettings AutoTradeAdd { get; }
    public AutoTradeAcceptSettings AutoTradeAccept { get; }
    public LootingSettings Looting { get; }
    public TinkeringSettings Tinkering { get; }
    public InventoryManagementSettings InventoryManagement { get; }
    public ItemInfoOnIdentSettings ItemInfoOnIdent { get; }
    public CombatTrackerSettings CombatTracker { get; }
    public CorpseTrackerSettings CorpseTracker { get; }
    public PlayerTrackerSettings PlayerTracker { get; }
    public ChatLoggerSettings ChatLogger { get; }
    public MiscSettings Misc { get; }
    public FilterSettings Filters { get; }

    /// <summary>Per-account/server/character and per-server command lists.</summary>
    public ScopedCommandStore Commands { get; }

    /// <summary>Every setting, in declaration order, as <c>Group.Field</c>.</summary>
    public IReadOnlyList<SettingDescriptor> All { get; }

    /// <summary>
    /// Resolves <c>Group.Field</c>, case-insensitively, the way the original's
    /// reflection lookup did.
    /// </summary>
    public SettingDescriptor? Find(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return null;

        foreach (SettingDescriptor descriptor in All)
        {
            if (string.Equals(
                    descriptor.QualifiedName,
                    qualifiedName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                return descriptor;
        }

        return null;
    }

    private IReadOnlyList<SettingDescriptor> BuildDescriptors()
    {
        var descriptors = new List<SettingDescriptor>();

        foreach (PropertyInfo groupProperty in GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            object? group = groupProperty.GetValue(this);
            if (group is null || group is SettingsFile || group is ScopedCommandStore)
                continue;
            if (group is System.Collections.IEnumerable)
                continue;

            foreach (PropertyInfo settingProperty in group.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (settingProperty.GetValue(group) is not ISetting setting)
                    continue;
                descriptors.Add(new SettingDescriptor(
                    groupProperty.Name,
                    settingProperty.Name,
                    setting));
            }
        }

        return descriptors;
    }
}

public sealed class ManaManagementSettings
{
    public ManaManagementSettings(SettingsFile file)
        => AutoRecharge = new Setting<bool>(
            file, "ManaManagement/AutoRecharge", "Auto Recharge Mana", true);

    public Setting<bool> AutoRecharge { get; }
}

public sealed class AutoBuySellSettings
{
    public AutoBuySellSettings(SettingsFile file)
    {
        Enabled = new Setting<bool>(
            file, "AutoBuySell/Enabled", "Auto Buy/Sell Enabled", true);
        TestMode = new Setting<bool>(
            file, "AutoBuySell/TestMode", "- Auto Buy/Sell Test Mode");

        // Turning a child on turns its parent on, as in the original.
        TestMode.Changed += setting =>
        {
            if (setting is Setting<bool> { Value: true })
                Enabled.Value = true;
        };
    }

    public Setting<bool> Enabled { get; }
    public Setting<bool> TestMode { get; }
}

public sealed class AutoTradeAddSettings
{
    public AutoTradeAddSettings(SettingsFile file)
        => Enabled = new Setting<bool>(
            file, "AutoTradeAdd/Enabled", "Auto Add To Trade Enabled");

    public Setting<bool> Enabled { get; }
}

public sealed class AutoTradeAcceptSettings
{
    private const string WhitelistPath = "AutoTradeAccept/Whitelist";

    private readonly SettingsFile _file;

    public AutoTradeAcceptSettings(SettingsFile file)
    {
        _file = file;
        Enabled = new Setting<bool>(
            file, "AutoTradeAccept/Enabled", "Auto Trade Accept Enabled");
    }

    public Setting<bool> Enabled { get; }

    /// <summary>Whole-name patterns; the original anchors each one.</summary>
    public IReadOnlyList<Regex> Whitelist
    {
        get
        {
            var patterns = new List<Regex>();
            foreach (string text in _file.GetChildrenInnerTexts(WhitelistPath))
                patterns.Add(new Regex("^" + text + "$"));
            return patterns;
        }
    }

    public IReadOnlyList<string> WhitelistPatterns
        => _file.GetChildrenInnerTexts(WhitelistPath);

    public void SetWhitelistPatterns(IReadOnlyList<string> patterns)
        => _file.SetNodeChildren(WhitelistPath, "Name", patterns);
}

public sealed class LootingSettings
{
    public LootingSettings(SettingsFile file)
    {
        AutoLootChests = new Setting<bool>(
            file, "Looting/AutoLootChests", "Auto Loot Chests", true);
        AutoLootCorpses = new Setting<bool>(
            file, "Looting/AutoLootCorpses", "Auto Loot Corpses", true);
        // The element name is singular in the original file format.
        AutoLootMyCorpses = new Setting<bool>(
            file, "Looting/AutoLootMyCorpse", "Auto Loot My Corpses", true);
        LootSalvage = new Setting<bool>(
            file, "Looting/LootSalvage", "Auto Loot Salvage");
    }

    public Setting<bool> AutoLootChests { get; }
    public Setting<bool> AutoLootCorpses { get; }
    public Setting<bool> AutoLootMyCorpses { get; }
    public Setting<bool> LootSalvage { get; }
}

public sealed class TinkeringSettings
{
    public TinkeringSettings(SettingsFile file)
        => AutoClickYes = new Setting<bool>(
            file, "Tinkering/AutoClickYes", "Auto Click Yes on 100%");

    public Setting<bool> AutoClickYes { get; }
}

public sealed class InventoryManagementSettings
{
    public InventoryManagementSettings(SettingsFile file)
    {
        InventoryLogger = new Setting<bool>(
            file,
            "InventoryManagement/InventoryLogger",
            "Inventory Logger Enabled");
        AetheriaRevealer = new Setting<bool>(
            file,
            "InventoryManagement/AetheriaRevealer",
            "Auto Reveal Aetheria",
            true);
        HeartCarver = new Setting<bool>(
            file, "InventoryManagement/HeartCarver", "Auto Carve Hearts");
        ShatteredKeyFixer = new Setting<bool>(
            file,
            "InventoryManagement/ShatteredKeyFixer",
            "Auto Fix Shattered Keys");
        KeyRinger = new Setting<bool>(
            file, "InventoryManagement/KeyRinger", "Auto Ring Keys");
        KeyDeringer = new Setting<bool>(
            file, "InventoryManagement/KeyDeringer", "Auto Dering Keys");
    }

    public Setting<bool> InventoryLogger { get; }
    public Setting<bool> AetheriaRevealer { get; }
    public Setting<bool> HeartCarver { get; }
    public Setting<bool> ShatteredKeyFixer { get; }
    public Setting<bool> KeyRinger { get; }
    public Setting<bool> KeyDeringer { get; }
}

public sealed class ItemInfoOnIdentSettings
{
    public ItemInfoOnIdentSettings(SettingsFile file)
    {
        Enabled = new Setting<bool>(
            file, "ItemInfoOnIdent/Enabled", "Show Item Info On Ident", true);
        ShowBuffedValues = new Setting<bool>(
            file,
            "ItemInfoOnIdent/ShowBuffedValues",
            "- Show Item Info Buffed* Values",
            true);
        ShowValueAndBurden = new Setting<bool>(
            file,
            "ItemInfoOnIdent/ShowValueAndBurden",
            "- Show Value and Burden");
        LeftClickIdent = new Setting<bool>(
            file,
            "ItemInfoOnIdent/LeftClickIdent",
            "- Ident Items on Left Click");
        AutoClipboard = new Setting<bool>(
            file,
            "ItemInfoOnIdent/AutoClipboard",
            "- Clipboard Item Info On Ident");

        foreach (Setting<bool> child in
            new[] { ShowBuffedValues, ShowValueAndBurden, LeftClickIdent, AutoClipboard })
        {
            child.Changed += setting =>
            {
                if (setting is Setting<bool> { Value: true })
                    Enabled.Value = true;
            };
        }
    }

    public Setting<bool> Enabled { get; }
    public Setting<bool> ShowBuffedValues { get; }
    public Setting<bool> ShowValueAndBurden { get; }
    public Setting<bool> LeftClickIdent { get; }
    public Setting<bool> AutoClipboard { get; }
}

public sealed class CombatTrackerSettings
{
    public CombatTrackerSettings(SettingsFile file)
    {
        Persistent = new Setting<bool>(
            file, "CombatTracker/Persistent", "Keep Stats Persistent", true);
        ExportOnLogOff = new Setting<bool>(
            file, "CombatTracker/ExportOnLogOff", "Export Stats on LogOff");
        SortAlphabetically = new Setting<bool>(
            file, "CombatTracker/SortAlphabetically", "Sort Alphabetically");
    }

    public Setting<bool> Persistent { get; }
    public Setting<bool> ExportOnLogOff { get; }
    public Setting<bool> SortAlphabetically { get; }
}

public sealed class CorpseTrackerSettings
{
    public CorpseTrackerSettings(SettingsFile file)
    {
        Enabled = new Setting<bool>(
            file, "CorpseTracker/Enabled", "Corpse Tracker Enabled", true);
        Persistent = new Setting<bool>(
            file, "CorpseTracker/Persistent", "Keep Stats Persistent", true);
        TrackAllCorpses = new Setting<bool>(
            file, "CorpseTracker/TrackAllCorpses", "Track All Corpses");
        TrackFellowCorpses = new Setting<bool>(
            file, "CorpseTracker/TrackFellowCorpses", "Track Fellow Corpses");
        TrackPermittedCorpses = new Setting<bool>(
            file,
            "CorpseTracker/TrackPermittedCorpses",
            "Track Permitted Corpses",
            true);
    }

    public Setting<bool> Enabled { get; }
    public Setting<bool> Persistent { get; }
    public Setting<bool> TrackAllCorpses { get; }
    public Setting<bool> TrackFellowCorpses { get; }
    public Setting<bool> TrackPermittedCorpses { get; }
}

public sealed class PlayerTrackerSettings
{
    public PlayerTrackerSettings(SettingsFile file)
    {
        Enabled = new Setting<bool>(
            file, "PlayerTracker/Enabled", "Player Tracker Enabled");
        Persistent = new Setting<bool>(
            file, "PlayerTracker/Persistent", "Keep Stats Persistent");
    }

    public Setting<bool> Enabled { get; }
    public Setting<bool> Persistent { get; }
}

/// <summary>One of the two chat-logger channel groups.</summary>
public sealed class ChatLoggerGroupSettings
{
    public ChatLoggerGroupSettings(SettingsFile file, int groupNumber)
    {
        string prefix = "ChatLogger/Group"
            + groupNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "/";

        Area = new Setting<bool>(file, prefix + "Area", "Area");
        // Group 1 logs tells out of the box; group 2 starts empty.
        Tells = new Setting<bool>(
            file, prefix + "Tells", "Tells", groupNumber == 1);
        Fellowship = new Setting<bool>(file, prefix + "Fellowship", "Fellowship");
        General = new Setting<bool>(file, prefix + "General", "General");
        Trade = new Setting<bool>(file, prefix + "Trade", "Trade");
        Allegiance = new Setting<bool>(file, prefix + "Allegiance", "Allegiance");

        All = [Area, Tells, Fellowship, General, Trade, Allegiance];
    }

    public Setting<bool> Area { get; }
    public Setting<bool> Tells { get; }
    public Setting<bool> Fellowship { get; }
    public Setting<bool> General { get; }
    public Setting<bool> Trade { get; }
    public Setting<bool> Allegiance { get; }

    public IReadOnlyList<Setting<bool>> All { get; }
}

public sealed class ChatLoggerSettings
{
    public ChatLoggerSettings(SettingsFile file)
    {
        Persistent = new Setting<bool>(
            file, "ChatLogger/Persistent", "Keep Logs Persistent", true);
        Group1 = new ChatLoggerGroupSettings(file, 1);
        Group2 = new ChatLoggerGroupSettings(file, 2);
    }

    public Setting<bool> Persistent { get; }

    public ChatLoggerGroupSettings Group1 { get; }

    public ChatLoggerGroupSettings Group2 { get; }
}

public sealed class MiscSettings
{
    public MiscSettings(SettingsFile file)
    {
        OpenMainPackOnLogin = new Setting<bool>(
            file, "Misc/OpenMainPackOnLogin", "Open Main Pack On Login", true);
        LogOutOnDeath = new Setting<bool>(
            file, "Misc/LogOutOnDeath", "Log Out on Death");
        DebuggingEnabled = new Setting<bool>(
            file, "Misc/DebuggingEnabled", "Debugging Enabled", true);
        VerboseDebuggingEnabled = new Setting<bool>(
            file, "Misc/VerboseDebuggingEnabled", "Verbose Debugging Enabled");
        OutputTargetWindow = new Setting<int>(
            file, "Misc/OutputTargetWindow", "Output Window", 1);
    }

    public Setting<bool> OpenMainPackOnLogin { get; }
    public Setting<bool> LogOutOnDeath { get; }
    public Setting<bool> DebuggingEnabled { get; }
    public Setting<bool> VerboseDebuggingEnabled { get; }
    public Setting<int> OutputTargetWindow { get; }
}

public sealed class FilterSettings
{
    public FilterSettings(SettingsFile file)
    {
        AttackEvades = new Setting<bool>(
            file, "Filters/AttackEvades", "Attack Evades");
        DefenseEvades = new Setting<bool>(
            file, "Filters/DefenseEvades", "Defense Evades");
        AttackResists = new Setting<bool>(
            file, "Filters/AttackResists", "Attack Resists");
        DefenseResists = new Setting<bool>(
            file, "Filters/DefenseResists", "Defense Resists");
        NPKFails = new Setting<bool>(file, "Filters/NPKFails", "NPK Fails");
        DirtyFighting = new Setting<bool>(
            file, "Filters/DirtyFighting", "Dirty Fighting");
        MonsterDeaths = new Setting<bool>(
            file, "Filters/MonsterDeaths", "Monster Deaths");

        SpellCastingMine = new Setting<bool>(
            file, "Filters/SpellCastingMine", "Spell Casting - Mine");
        SpellCastingOthers = new Setting<bool>(
            file, "Filters/SpellCastingOthers", "Spell Casting - Others");
        SpellCastFizzles = new Setting<bool>(
            file, "Filters/SpellCastFizzles", "Spell Cast Fizzles");
        CompUsage = new Setting<bool>(file, "Filters/CompUsage", "Comp Usage");
        SpellExpires = new Setting<bool>(
            file, "Filters/SpellExpires", "Spell Expires");

        HealingKitSuccess = new Setting<bool>(
            file, "Filters/HealingKitSuccess", "Healing Kit Success");
        HealingKitFail = new Setting<bool>(
            file, "Filters/HealingKitFail", "Healing Kit Fail");
        Salvaging = new Setting<bool>(file, "Filters/Salvaging", "Salvaging");
        SalvagingFails = new Setting<bool>(
            file, "Filters/SalvagingFails", "Salvaging Fails");
        AuraOfCraftman = new Setting<bool>(
            file, "Filters/AuraOfCraftman", "Aura Of Craftman Spam");
        ManaStoneUsage = new Setting<bool>(
            file, "Filters/ManaStoneUsage", "Mana Stone Usage");

        TradeBuffBotSpam = new Setting<bool>(
            file, "Filters/TradeBuffBotSpam", "Trade/Buff Bot Spam");
        FailedAssess = new Setting<bool>(
            file, "Filters/FailedAssess", "Someone failed to assess you");

        KillTaskComplete = new Setting<bool>(
            file, "Filters/KillTaskComplete", "Kill Task Complete");
        VendorTells = new Setting<bool>(
            file, "Filters/VendorTells", "Vendor Tells");
        MonsterTell = new Setting<bool>(
            file, "Filters/MonsterTell", "Monster Tells");
        NpcChatter = new Setting<bool>(
            file, "Filters/NPCChatter", "NPC Chatter");
        MasterArbitratorSpam = new Setting<bool>(
            file, "Filters/MasterArbitratorSpam", "Master Arbitrator Spam");
        AllMasterArbitratorChat = new Setting<bool>(
            file,
            "Filters/AllMasterArbitratorChat",
            "All Master Arbitrator Chat");

        StatusTextYoureTooBusy = new Setting<bool>(
            file,
            "Filters/StatusTextYoureTooBusy",
            "Status Text: You're too busy!");
        StatusTextCasting = new Setting<bool>(
            file, "Filters/StatusTextCasting", "Status Text: Casting ...");
        StatusTextAll = new Setting<bool>(
            file, "Filters/StatusTextAll", "Status Text: All");
    }

    public Setting<bool> AttackEvades { get; }
    public Setting<bool> DefenseEvades { get; }
    public Setting<bool> AttackResists { get; }
    public Setting<bool> DefenseResists { get; }
    public Setting<bool> NPKFails { get; }
    public Setting<bool> DirtyFighting { get; }
    public Setting<bool> MonsterDeaths { get; }
    public Setting<bool> SpellCastingMine { get; }
    public Setting<bool> SpellCastingOthers { get; }
    public Setting<bool> SpellCastFizzles { get; }
    public Setting<bool> CompUsage { get; }
    public Setting<bool> SpellExpires { get; }
    public Setting<bool> HealingKitSuccess { get; }
    public Setting<bool> HealingKitFail { get; }
    public Setting<bool> Salvaging { get; }
    public Setting<bool> SalvagingFails { get; }
    public Setting<bool> AuraOfCraftman { get; }
    public Setting<bool> ManaStoneUsage { get; }
    public Setting<bool> TradeBuffBotSpam { get; }
    public Setting<bool> FailedAssess { get; }
    public Setting<bool> KillTaskComplete { get; }
    public Setting<bool> VendorTells { get; }
    public Setting<bool> MonsterTell { get; }
    public Setting<bool> NpcChatter { get; }
    public Setting<bool> MasterArbitratorSpam { get; }
    public Setting<bool> AllMasterArbitratorChat { get; }
    public Setting<bool> StatusTextYoureTooBusy { get; }
    public Setting<bool> StatusTextCasting { get; }
    public Setting<bool> StatusTextAll { get; }
}
