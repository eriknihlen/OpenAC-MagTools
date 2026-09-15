using System.Globalization;
using System.Reflection;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Ui;

/// <summary>Misc → Options: the generated setting checkbox list.</summary>
public sealed class MiscOptionsPageViewModel
{
    private readonly Setting<int> _outputWindow;

    public MiscOptionsPageViewModel(SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _outputWindow = settings.Misc.OutputTargetWindow;

        // The original's Options page order.
        Options = new OptionListViewModel(
        [
            settings.ItemInfoOnIdent.Enabled,
            settings.ItemInfoOnIdent.ShowBuffedValues,
            settings.ItemInfoOnIdent.ShowValueAndBurden,
            settings.ItemInfoOnIdent.LeftClickIdent,
            settings.ItemInfoOnIdent.AutoClipboard,
            settings.AutoBuySell.Enabled,
            settings.AutoBuySell.TestMode,
            settings.AutoTradeAdd.Enabled,
            settings.AutoTradeAccept.Enabled,
            settings.Looting.AutoLootChests,
            settings.Looting.AutoLootCorpses,
            settings.Looting.AutoLootMyCorpses,
            settings.Looting.LootSalvage,
            settings.Tinkering.AutoClickYes,
            settings.InventoryManagement.InventoryLogger,
            settings.InventoryManagement.AetheriaRevealer,
            settings.InventoryManagement.HeartCarver,
            settings.InventoryManagement.ShatteredKeyFixer,
            settings.InventoryManagement.KeyRinger,
            settings.InventoryManagement.KeyDeringer,
            settings.Misc.OpenMainPackOnLogin,
            settings.Misc.LogOutOnDeath,
            settings.Misc.DebuggingEnabled,
            settings.Misc.VerboseDebuggingEnabled,
        ]);

        SetOutputWindow = text =>
        {
            if (int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int window))
                _outputWindow.Value = window;
        };
    }

    public OptionListViewModel Options { get; }

    public string OutputWindowText =>
        _outputWindow.Value.ToString(CultureInfo.InvariantCulture);

    public Action<string> SetOutputWindow { get; }
}

/// <summary>Misc → Filters: the generated chat-filter checkbox list.</summary>
public sealed class FiltersPageViewModel
{
    public FiltersPageViewModel(SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        FilterSettings filters = settings.Filters;

        Options = new OptionListViewModel(
        [
            filters.AttackEvades,
            filters.DefenseEvades,
            filters.AttackResists,
            filters.DefenseResists,
            filters.NPKFails,
            filters.DirtyFighting,
            filters.MonsterDeaths,
            filters.SpellCastingMine,
            filters.SpellCastingOthers,
            filters.SpellCastFizzles,
            filters.CompUsage,
            filters.SpellExpires,
            filters.HealingKitSuccess,
            filters.HealingKitFail,
            filters.Salvaging,
            filters.SalvagingFails,
            filters.AuraOfCraftman,
            filters.ManaStoneUsage,
            filters.TradeBuffBotSpam,
            filters.FailedAssess,
            filters.KillTaskComplete,
            filters.VendorTells,
            filters.MonsterTell,
            filters.NpcChatter,
            filters.MasterArbitratorSpam,
            filters.AllMasterArbitratorChat,
            filters.StatusTextYoureTooBusy,
            filters.StatusTextCasting,
            filters.StatusTextAll,
        ]);
    }

    public OptionListViewModel Options { get; }
}

/// <summary>Misc → About.</summary>
public sealed class AboutPageViewModel
{
    public string ThanksText { get; } =
        "Parts of this plugin came from the ideas of Squire and ManaMinder "
        + "plugins. Squire was created by Flynn:";

    public string ThanksUrl { get; } = "http://www.flynn1179.net/ac/";

    public string Website { get; } = "http://magtools.codeplex.com/";

    public string VersionText { get; } = "Version: " + ReadVersion();

    private static string ReadVersion()
    {
        Assembly assembly = typeof(AboutPageViewModel).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            // Strip the source-revision suffix a deterministic build appends.
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
