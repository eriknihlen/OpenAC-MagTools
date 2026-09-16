using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using ChatLoggerFeature = OpenAC.MagTools.Loggers.Chat.ChatLogger;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// The single binding object for <c>magtools.xml</c>.
/// </summary>
/// <remarks>
/// <para>
/// Markup bindings resolve to public properties of one object and there are no
/// dotted paths, so the page view-models keep the state and this class projects
/// it. That keeps each page's logic in its own class while still handing the
/// host the flat surface it needs.
/// </para>
/// <para>
/// Tabs are tab buttons plus visibility-bound page groups: three rows of
/// buttons (top level, sub-notebook, and the Combat/Corpse/Player inner
/// notebook) with every page built once and only its visibility flipped.
/// </para>
/// </remarks>
public sealed class MainViewModel : IDisposable
{
    private bool _disposed;
    private TopTab _top = TopTab.Trackers;
    private TrackerTab _tracker = TrackerTab.Mana;
    private LoggerTab _logger = LoggerTab.Group1;
    private ToolTab _tool = ToolTab.Inventory;
    private MiscTab _misc = MiscTab.Options;
    private CombatTab _combat = CombatTab.Current;
    private bool _corpseOptions;
    private bool _playerOptions;

    public MainViewModel(
        SettingsManager settings,
        ChatLoggerFeature chatLogger,
        IPluginHost? host = null,
        Trackers.Combat.CombatTrackerHost? combatTrackerHost = null,
        Trackers.Equipment.EquipmentTrackerHost? equipmentTrackerHost = null,
        Trackers.Inventory.InventoryTrackerHost? inventoryTrackerHost = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(chatLogger);

        Mana = new ManaPageViewModel(settings, equipmentTrackerHost, host);
        Combat = new CombatPageViewModel(settings, combatTrackerHost, host);
        Corpse = new CorpsePageViewModel(settings);
        Player = new PlayerPageViewModel(settings);
        InventoryItems = new InventoryItemsPageViewModel(inventoryTrackerHost);
        ChatLogger = new ChatLoggerPageViewModel(settings, chatLogger);
        InventoryTools = new InventoryToolsPageViewModel(
            host, settings.ItemInfoOnIdent, host?.Automation.Spells);
        Tinkering = new TinkeringPageViewModel();
        CharacterCommands = new ScopedCommandsPageViewModel();
        ServerCommands = new ScopedCommandsPageViewModel();
        MiscOptions = new MiscOptionsPageViewModel(settings);
        Filters = new FiltersPageViewModel(settings);
        About = new AboutPageViewModel();

        ShowTrackers = () => _top = TopTab.Trackers;
        ShowLoggers = () => _top = TopTab.Loggers;
        ShowTools = () => _top = TopTab.Tools;
        ShowMisc = () => _top = TopTab.Misc;

        ShowMana = () => _tracker = TrackerTab.Mana;
        ShowCombat = () => _tracker = TrackerTab.Combat;
        ShowCorpse = () => _tracker = TrackerTab.Corpse;
        ShowPlayer = () => _tracker = TrackerTab.Player;
        ShowInventoryItems = () => _tracker = TrackerTab.InventoryItems;

        ShowChatGroup1 = () => _logger = LoggerTab.Group1;
        ShowChatGroup2 = () => _logger = LoggerTab.Group2;
        ShowLoggerOptions = () => _logger = LoggerTab.Options;

        ShowInventoryTools = () => _tool = ToolTab.Inventory;
        ShowTinkering = () => _tool = ToolTab.Tinkering;
        ShowCharacter = () => _tool = ToolTab.Character;
        ShowServer = () => _tool = ToolTab.Server;

        ShowMiscOptions = () => _misc = MiscTab.Options;
        ShowFilters = () => _misc = MiscTab.Filters;
        ShowAbout = () => _misc = MiscTab.About;

        ShowCombatCurrent = () => _combat = CombatTab.Current;
        ShowCombatPersistent = () => _combat = CombatTab.Persistent;
        ShowCombatOptions = () => _combat = CombatTab.Options;

        ShowCorpseList = () => _corpseOptions = false;
        ShowCorpseOptions = () => _corpseOptions = true;
        ShowPlayerList = () => _playerOptions = false;
        ShowPlayerOptions = () => _playerOptions = true;
    }

    /// <summary>
    /// Disposes the four <see cref="OptionListViewModel"/> instances this
    /// view-model owns (Options, Filters, and the chat logger's two group
    /// checkbox lists), so their <see cref="ISetting.Changed"/> subscriptions
    /// do not outlive a plugin <c>Disable()</c>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        MiscOptions.Options.Dispose();
        Filters.Options.Dispose();
        ChatLogger.Dispose();
        Combat.Dispose();
        Mana.Dispose();
        InventoryItems.Dispose();
    }

    /// <summary>Reverses <see cref="Dispose"/> for a Disable()/Enable() cycle.</summary>
    public void Resubscribe()
    {
        if (!_disposed)
            return;
        _disposed = false;

        MiscOptions.Options.Resubscribe();
        Filters.Options.Resubscribe();
        ChatLogger.Resubscribe();
        Combat.Resubscribe();
        Mana.Resubscribe();
        InventoryItems.Resubscribe();
    }

    private enum TopTab { Trackers, Loggers, Tools, Misc }
    private enum TrackerTab { Mana, Combat, Corpse, Player, InventoryItems }
    private enum LoggerTab { Group1, Group2, Options }
    private enum ToolTab { Inventory, Tinkering, Character, Server }
    private enum MiscTab { Options, Filters, About }
    private enum CombatTab { Current, Persistent, Options }

    // ---- pages ---------------------------------------------------------------

    public ManaPageViewModel Mana { get; }
    public CombatPageViewModel Combat { get; }
    public CorpsePageViewModel Corpse { get; }
    public PlayerPageViewModel Player { get; }
    public InventoryItemsPageViewModel InventoryItems { get; }
    public ChatLoggerPageViewModel ChatLogger { get; }
    public InventoryToolsPageViewModel InventoryTools { get; }
    public TinkeringPageViewModel Tinkering { get; }
    public ScopedCommandsPageViewModel CharacterCommands { get; }
    public ScopedCommandsPageViewModel ServerCommands { get; }
    public MiscOptionsPageViewModel MiscOptions { get; }
    public FiltersPageViewModel Filters { get; }
    public AboutPageViewModel About { get; }

    // ---- window --------------------------------------------------------------

    /// <summary>
    /// The root <c>visible</c> binding is an availability gate, not the
    /// current shown/hidden state (the host tracks that itself and never
    /// reads this back). The main window's Options page has to be reachable
    /// before login, so it is always available; only the shelf button and the
    /// panel's own start-hidden default govern whether it is actually on
    /// screen.
    /// </summary>
    public bool WindowAvailable => true;

    // ---- top row -------------------------------------------------------------

    public bool TrackersSelected => _top == TopTab.Trackers;
    public bool LoggersSelected => _top == TopTab.Loggers;
    public bool ToolsSelected => _top == TopTab.Tools;
    public bool MiscSelected => _top == TopTab.Misc;

    public Action ShowTrackers { get; }
    public Action ShowLoggers { get; }
    public Action ShowTools { get; }
    public Action ShowMisc { get; }

    // ---- second row ----------------------------------------------------------

    public bool TrackersTabsVisible => TrackersSelected;
    public bool LoggersTabsVisible => LoggersSelected;
    public bool ToolsTabsVisible => ToolsSelected;
    public bool MiscTabsVisible => MiscSelected;

    public bool ManaSelected => _tracker == TrackerTab.Mana;
    public bool CombatSelected => _tracker == TrackerTab.Combat;
    public bool CorpseSelected => _tracker == TrackerTab.Corpse;
    public bool PlayerSelected => _tracker == TrackerTab.Player;
    public bool InventoryItemsSelected => _tracker == TrackerTab.InventoryItems;

    public Action ShowMana { get; }
    public Action ShowCombat { get; }
    public Action ShowCorpse { get; }
    public Action ShowPlayer { get; }
    public Action ShowInventoryItems { get; }

    public bool ChatGroup1Selected => _logger == LoggerTab.Group1;
    public bool ChatGroup2Selected => _logger == LoggerTab.Group2;
    public bool LoggerOptionsSelected => _logger == LoggerTab.Options;

    public Action ShowChatGroup1 { get; }
    public Action ShowChatGroup2 { get; }
    public Action ShowLoggerOptions { get; }

    public bool InventoryToolsSelected => _tool == ToolTab.Inventory;
    public bool TinkeringSelected => _tool == ToolTab.Tinkering;
    public bool CharacterSelected => _tool == ToolTab.Character;
    public bool ServerSelected => _tool == ToolTab.Server;

    public Action ShowInventoryTools { get; }
    public Action ShowTinkering { get; }
    public Action ShowCharacter { get; }
    public Action ShowServer { get; }

    public bool MiscOptionsSelected => _misc == MiscTab.Options;
    public bool FiltersSelected => _misc == MiscTab.Filters;
    public bool AboutSelected => _misc == MiscTab.About;

    public Action ShowMiscOptions { get; }
    public Action ShowFilters { get; }
    public Action ShowAbout { get; }

    // ---- third row -----------------------------------------------------------

    public bool CombatTabsVisible => TrackersSelected && CombatSelected;
    public bool CorpseTabsVisible => TrackersSelected && CorpseSelected;
    public bool PlayerTabsVisible => TrackersSelected && PlayerSelected;

    public bool CombatCurrentSelected => _combat == CombatTab.Current;
    public bool CombatPersistentSelected => _combat == CombatTab.Persistent;
    public bool CombatOptionsSelected => _combat == CombatTab.Options;

    public Action ShowCombatCurrent { get; }
    public Action ShowCombatPersistent { get; }
    public Action ShowCombatOptions { get; }

    public bool CorpseListSelected => !_corpseOptions;
    public bool CorpseOptionsSelected => _corpseOptions;

    public Action ShowCorpseList { get; }
    public Action ShowCorpseOptions { get; }

    public bool PlayerListSelected => !_playerOptions;
    public bool PlayerOptionsSelected => _playerOptions;

    public Action ShowPlayerList { get; }
    public Action ShowPlayerOptions { get; }

    // ---- page visibility -----------------------------------------------------

    public bool ManaVisible => TrackersSelected && ManaSelected;
    public bool CombatCurrentVisible => CombatTabsVisible && CombatCurrentSelected;
    public bool CombatPersistentVisible =>
        CombatTabsVisible && CombatPersistentSelected;
    public bool CombatOptionsVisible => CombatTabsVisible && CombatOptionsSelected;
    public bool CorpseListVisible => CorpseTabsVisible && CorpseListSelected;
    public bool CorpseOptionsVisible => CorpseTabsVisible && CorpseOptionsSelected;
    public bool PlayerListVisible => PlayerTabsVisible && PlayerListSelected;
    public bool PlayerOptionsVisible => PlayerTabsVisible && PlayerOptionsSelected;
    public bool InventoryItemsVisible => TrackersSelected && InventoryItemsSelected;

    public bool ChatGroup1Visible => LoggersSelected && ChatGroup1Selected;
    public bool ChatGroup2Visible => LoggersSelected && ChatGroup2Selected;
    public bool LoggerOptionsVisible => LoggersSelected && LoggerOptionsSelected;

    public bool InventoryToolsVisible => ToolsSelected && InventoryToolsSelected;
    public bool TinkeringVisible => ToolsSelected && TinkeringSelected;
    public bool CharacterVisible => ToolsSelected && CharacterSelected;
    public bool ServerVisible => ToolsSelected && ServerSelected;

    public bool MiscOptionsVisible => MiscSelected && MiscOptionsSelected;
    public bool FiltersVisible => MiscSelected && FiltersSelected;
    public bool AboutVisible => MiscSelected && AboutSelected;

    // ---- Trackers -> Mana ----------------------------------------------------

    public IReadOnlyList<uint> ManaItemIcons => Mana.ItemIcons;
    public IReadOnlyList<string> ManaItemNames => Mana.ItemNames;
    public IReadOnlyList<uint> ManaStateIcons => Mana.StateIcons;
    public IReadOnlyList<string> ManaItemMana => Mana.ItemMana;
    public IReadOnlyList<string> ManaItemTime => Mana.ItemTime;
    public Action<int> ManaIconClicked => Mana.RowIconClicked;
    public int ManaSelectedRow => Mana.SelectedRow;
    public Action<int> SelectManaRow => Mana.Select;
    public string ManaTotalText => Mana.TotalText;
    public string ManaUnretainedTotalText => Mana.UnretainedTotalText;
    public bool ManaRechargeEnabled => Mana.RechargeEnabled;
    public Action ToggleManaRecharge => Mana.ToggleRecharge;

    // ---- Trackers -> Combat --------------------------------------------------

    public IReadOnlyList<string> CombatCurrentMonsterNames => Combat.CurrentMonsterNames;
    public IReadOnlyList<string> CombatCurrentMonsterKillingBlows => Combat.CurrentMonsterKillingBlows;
    public IReadOnlyList<string> CombatCurrentMonsterDamageReceived => Combat.CurrentMonsterDamageReceived;
    public IReadOnlyList<string> CombatCurrentMonsterDamageGiven => Combat.CurrentMonsterDamageGiven;
    public int CombatCurrentMonsterSelectedRow => Combat.CurrentMonsterSelectedRow;
    public Action<int> SelectCombatCurrentMonster => Combat.SelectCurrentMonster;

    public IReadOnlyList<string> CombatCurrentDamageLabels => Combat.CurrentDamageLabels;
    public IReadOnlyList<string> CombatCurrentDamageMeleeMissile => Combat.CurrentDamageMeleeMissile;
    public IReadOnlyList<string> CombatCurrentDamageMagic => Combat.CurrentDamageMagic;
    public IReadOnlyList<string> CombatCurrentDamageStatLabels => Combat.CurrentDamageStatLabels;
    public IReadOnlyList<string> CombatCurrentDamageStatValues => Combat.CurrentDamageStatValues;
    public int CombatCurrentDamageSelectedRow => Combat.CurrentDamageSelectedRow;
    public Action<int> SelectCombatCurrentDamage => Combat.SelectCurrentDamage;

    public IReadOnlyList<string> CombatPersistentMonsterNames => Combat.PersistentMonsterNames;
    public IReadOnlyList<string> CombatPersistentMonsterKillingBlows => Combat.PersistentMonsterKillingBlows;
    public IReadOnlyList<string> CombatPersistentMonsterDamageReceived => Combat.PersistentMonsterDamageReceived;
    public IReadOnlyList<string> CombatPersistentMonsterDamageGiven => Combat.PersistentMonsterDamageGiven;
    public int CombatPersistentMonsterSelectedRow =>
        Combat.PersistentMonsterSelectedRow;
    public Action<int> SelectCombatPersistentMonster =>
        Combat.SelectPersistentMonster;

    public IReadOnlyList<string> CombatPersistentDamageLabels => Combat.PersistentDamageLabels;
    public IReadOnlyList<string> CombatPersistentDamageMeleeMissile => Combat.PersistentDamageMeleeMissile;
    public IReadOnlyList<string> CombatPersistentDamageMagic => Combat.PersistentDamageMagic;
    public IReadOnlyList<string> CombatPersistentDamageStatLabels => Combat.PersistentDamageStatLabels;
    public IReadOnlyList<string> CombatPersistentDamageStatValues => Combat.PersistentDamageStatValues;
    public int CombatPersistentDamageSelectedRow =>
        Combat.PersistentDamageSelectedRow;
    public Action<int> SelectCombatPersistentDamage => Combat.SelectPersistentDamage;

    public Action ClearCombatCurrentStats => Combat.ClearCurrentStats;
    public Action ExportCombatCurrentStats => Combat.ExportCurrentStats;
    public Action ClearCombatPersistentStats => Combat.ClearPersistentStats;
    public bool CombatExportOnLogOff => Combat.ExportOnLogOff;
    public Action ToggleCombatExportOnLogOff => Combat.ToggleExportOnLogOff;
    public bool CombatPersistent => Combat.Persistent;
    public Action ToggleCombatPersistent => Combat.TogglePersistent;
    public bool CombatSortAlphabetically => Combat.SortAlphabetically;
    public Action ToggleCombatSortAlphabetically => Combat.ToggleSortAlphabetically;

    // ---- Trackers -> Corpse --------------------------------------------------

    public IReadOnlyList<string> CorpseTimes => Corpse.Times;
    public IReadOnlyList<string> CorpseNames => Corpse.Names;
    public IReadOnlyList<string> CorpseCoordinates => Corpse.Coordinates;
    public int CorpseSelectedRow => Corpse.SelectedRow;
    public Action<int> SelectCorpseRow => Corpse.Select;
    public Action ClearCorpseHistory => Corpse.ClearHistory;
    public bool CorpseEnabled => Corpse.Enabled;
    public Action ToggleCorpseEnabled => Corpse.ToggleEnabled;
    public bool CorpsePersistent => Corpse.Persistent;
    public Action ToggleCorpsePersistent => Corpse.TogglePersistent;
    public bool CorpseTrackAll => Corpse.TrackAll;
    public Action ToggleCorpseTrackAll => Corpse.ToggleTrackAll;
    public bool CorpseTrackFellow => Corpse.TrackFellow;
    public Action ToggleCorpseTrackFellow => Corpse.ToggleTrackFellow;
    public bool CorpseTrackPermitted => Corpse.TrackPermitted;
    public Action ToggleCorpseTrackPermitted => Corpse.ToggleTrackPermitted;

    // ---- Trackers -> Player --------------------------------------------------

    public IReadOnlyList<string> PlayerTimes => Player.Times;
    public IReadOnlyList<string> PlayerNames => Player.Names;
    public IReadOnlyList<string> PlayerCoordinates => Player.Coordinates;
    public int PlayerSelectedRow => Player.SelectedRow;
    public Action<int> SelectPlayerRow => Player.Select;
    public Action ClearPlayerHistory => Player.ClearHistory;
    public bool PlayerEnabled => Player.Enabled;
    public Action TogglePlayerEnabled => Player.ToggleEnabled;
    public bool PlayerPersistent => Player.Persistent;
    public Action TogglePlayerPersistent => Player.TogglePersistent;

    // ---- Trackers -> Inv. Items ----------------------------------------------

    public IReadOnlyList<uint> InventoryItemIcons => InventoryItems.Icons;
    public Action<int> InventoryItemIconClicked => InventoryItems.RowIconClicked;
    public IReadOnlyList<string> InventoryItemNames => InventoryItems.Names;
    public IReadOnlyList<string> InventoryItemCounts => InventoryItems.Counts;
    public IReadOnlyList<string> InventoryItemAverage5m => InventoryItems.Average5m;
    public IReadOnlyList<string> InventoryItemAverage1h => InventoryItems.Average1h;
    public IReadOnlyList<string> InventoryItemHours => InventoryItems.Hours;
    public int InventoryItemSelectedRow => InventoryItems.SelectedRow;
    public Action<int> SelectInventoryItemRow => InventoryItems.Select;

    // ---- Loggers -------------------------------------------------------------

    public IReadOnlyList<string> ChatGroup1Times => ChatLogger.Group1Times;
    public IReadOnlyList<string> ChatGroup1Messages => ChatLogger.Group1Messages;
    public int ChatGroup1SelectedRow => ChatLogger.Group1SelectedRow;
    public Action<int> SelectChatGroup1Row => ChatLogger.SelectGroup1Row;

    public IReadOnlyList<string> ChatGroup2Times => ChatLogger.Group2Times;
    public IReadOnlyList<string> ChatGroup2Messages => ChatLogger.Group2Messages;
    public int ChatGroup2SelectedRow => ChatLogger.Group2SelectedRow;
    public Action<int> SelectChatGroup2Row => ChatLogger.SelectGroup2Row;

    public Action ClearChatLoggerHistory => ChatLogger.ClearHistory;
    public bool ChatLoggerPersistent => ChatLogger.Persistent;
    public Action ToggleChatLoggerPersistent => ChatLogger.TogglePersistent;

    public IReadOnlyList<bool> ChatGroup1OptionChecks => ChatLogger.Group1Options.Checks;
    public Action<int> ToggleChatGroup1Option => ChatLogger.Group1Options.Toggle;
    public IReadOnlyList<string> ChatGroup1OptionCaptions =>
        ChatLogger.Group1Options.Captions;
    public int ChatGroup1OptionSelectedRow => ChatLogger.Group1Options.SelectedRow;
    public Action<int> SelectChatGroup1Option => ChatLogger.Group1Options.Select;

    public IReadOnlyList<bool> ChatGroup2OptionChecks => ChatLogger.Group2Options.Checks;
    public Action<int> ToggleChatGroup2Option => ChatLogger.Group2Options.Toggle;
    public IReadOnlyList<string> ChatGroup2OptionCaptions =>
        ChatLogger.Group2Options.Captions;
    public int ChatGroup2OptionSelectedRow => ChatLogger.Group2Options.SelectedRow;
    public Action<int> SelectChatGroup2Option => ChatLogger.Group2Options.Select;

    // ---- Tools -> Inventory --------------------------------------------------

    public Action ClipboardWornEquipment => InventoryTools.ClipboardWornEquipment;
    public Action ClipboardInventoryInfo => InventoryTools.ClipboardInventoryInfo;
    public string InventorySearchText => InventoryTools.SearchText;
    public Action<string> SetInventorySearchText => InventoryTools.SetSearchText;
    public IReadOnlyList<uint> InventorySearchIcons => InventoryTools.ResultIcons;
    public Action<int> InventorySearchIconClicked => InventoryTools.RowIconClicked;
    public IReadOnlyList<string> InventorySearchNames => InventoryTools.ResultNames;
    public int InventorySearchSelectedRow => InventoryTools.SelectedRow;
    public Action<int> SelectInventorySearchRow => InventoryTools.SelectResultRow;
    public IReadOnlyList<string> InventoryItemInfoLines => InventoryTools.ItemInfoLines;
    public int InventoryItemInfoSelectedRow => InventoryTools.ItemInfoSelectedRow;
    public Action<int> SelectInventoryItemInfoLine => InventoryTools.SelectItemInfoLine;

    // ---- Tools -> Tinkering --------------------------------------------------

    public Action AddSelectedTinkerItem => Tinkering.AddSelectedItem;
    public IReadOnlyList<string> TinkeringMaterials => Tinkering.Materials;
    public string SelectedTinkeringMaterial => Tinkering.SelectedMaterial;
    public Action<string> SelectTinkeringMaterial => Tinkering.SelectMaterial;
    public string TinkeringMinimumPercentText => Tinkering.MinimumPercentText;
    public Action<string> SetTinkeringMinimumPercent => Tinkering.SetMinimumPercent;
    public string TinkeringTargetTotalTinksText => Tinkering.TargetTotalTinksText;
    public Action<string> SetTinkeringTargetTotalTinks =>
        Tinkering.SetTargetTotalTinks;
    public Action StartTinkering => Tinkering.Start;
    public Action StopTinkering => Tinkering.Stop;
    public IReadOnlyList<string> TinkeringCurrentMarks => Tinkering.CurrentMarks;
    public IReadOnlyList<uint> TinkeringIcons => Tinkering.Icons;
    public Action<int> TinkeringIconClicked => Tinkering.RowIconClicked;
    public IReadOnlyList<string> TinkeringNames => Tinkering.Names;
    public IReadOnlyList<string> TinkeringWork => Tinkering.Work;
    public IReadOnlyList<string> TinkeringTinks => Tinkering.Tinks;
    public IReadOnlyList<uint> TinkeringDeleteIcons => Tinkering.DeleteIcons;
    public Action<int> DeleteTinkeringRow => Tinkering.DeleteRow;
    public int TinkeringSelectedRow => Tinkering.SelectedRow;
    public Action<int> SelectTinkeringRow => Tinkering.Select;

    // ---- Tools -> Character --------------------------------------------------

    public string CharacterLoginText => CharacterCommands.LoginText;
    public Action<string> SetCharacterLoginText => CharacterCommands.SetLoginText;
    public Action AddCharacterLoginCommand => CharacterCommands.AddLoginCommand;
    public IReadOnlyList<string> CharacterLoginCommands =>
        CharacterCommands.LoginCommands;
    public IReadOnlyList<uint> CharacterLoginUpIcons => CharacterCommands.LoginUpIcons;
    public Action<int> MoveCharacterLoginCommandUp =>
        CharacterCommands.MoveLoginCommandUp;
    public IReadOnlyList<uint> CharacterLoginDownIcons =>
        CharacterCommands.LoginDownIcons;
    public Action<int> MoveCharacterLoginCommandDown =>
        CharacterCommands.MoveLoginCommandDown;
    public IReadOnlyList<uint> CharacterLoginDeleteIcons =>
        CharacterCommands.LoginDeleteIcons;
    public Action<int> DeleteCharacterLoginCommand =>
        CharacterCommands.DeleteLoginCommand;
    public int CharacterLoginSelectedRow => CharacterCommands.LoginSelectedRow;
    public Action<int> SelectCharacterLoginRow => CharacterCommands.SelectLoginRow;

    public string CharacterLoginCompleteText => CharacterCommands.LoginCompleteText;
    public Action<string> SetCharacterLoginCompleteText =>
        CharacterCommands.SetLoginCompleteText;
    public Action AddCharacterLoginCompleteCommand =>
        CharacterCommands.AddLoginCompleteCommand;
    public IReadOnlyList<string> CharacterLoginCompleteCommands =>
        CharacterCommands.LoginCompleteCommands;
    public IReadOnlyList<uint> CharacterLoginCompleteUpIcons =>
        CharacterCommands.LoginCompleteUpIcons;
    public Action<int> MoveCharacterLoginCompleteCommandUp =>
        CharacterCommands.MoveLoginCompleteCommandUp;
    public IReadOnlyList<uint> CharacterLoginCompleteDownIcons =>
        CharacterCommands.LoginCompleteDownIcons;
    public Action<int> MoveCharacterLoginCompleteCommandDown =>
        CharacterCommands.MoveLoginCompleteCommandDown;
    public IReadOnlyList<uint> CharacterLoginCompleteDeleteIcons =>
        CharacterCommands.LoginCompleteDeleteIcons;
    public Action<int> DeleteCharacterLoginCompleteCommand =>
        CharacterCommands.DeleteLoginCompleteCommand;
    public int CharacterLoginCompleteSelectedRow =>
        CharacterCommands.LoginCompleteSelectedRow;
    public Action<int> SelectCharacterLoginCompleteRow =>
        CharacterCommands.SelectLoginCompleteRow;

    public string CharacterPeriodicText => CharacterCommands.PeriodicText;
    public Action<string> SetCharacterPeriodicText =>
        CharacterCommands.SetPeriodicText;
    public string CharacterPeriodicIntervalText =>
        CharacterCommands.PeriodicIntervalText;
    public Action<string> SetCharacterPeriodicInterval =>
        CharacterCommands.SetPeriodicInterval;
    public string CharacterPeriodicOffsetText =>
        CharacterCommands.PeriodicOffsetText;
    public Action<string> SetCharacterPeriodicOffset =>
        CharacterCommands.SetPeriodicOffset;
    public Action AddCharacterPeriodicCommand =>
        CharacterCommands.AddPeriodicCommand;
    public IReadOnlyList<string> CharacterPeriodicCommands =>
        CharacterCommands.PeriodicCommands;
    public IReadOnlyList<string> CharacterPeriodicIntervals =>
        CharacterCommands.PeriodicIntervals;
    public IReadOnlyList<string> CharacterPeriodicOffsets =>
        CharacterCommands.PeriodicOffsets;
    public IReadOnlyList<uint> CharacterPeriodicDeleteIcons =>
        CharacterCommands.PeriodicDeleteIcons;
    public Action<int> DeleteCharacterPeriodicCommand =>
        CharacterCommands.DeletePeriodicCommand;
    public int CharacterPeriodicSelectedRow =>
        CharacterCommands.PeriodicSelectedRow;
    public Action<int> SelectCharacterPeriodicRow =>
        CharacterCommands.SelectPeriodicRow;

    // ---- Tools -> Server -----------------------------------------------------

    public string ServerLoginText => ServerCommands.LoginText;
    public Action<string> SetServerLoginText => ServerCommands.SetLoginText;
    public Action AddServerLoginCommand => ServerCommands.AddLoginCommand;
    public IReadOnlyList<string> ServerLoginCommands => ServerCommands.LoginCommands;
    public IReadOnlyList<uint> ServerLoginUpIcons => ServerCommands.LoginUpIcons;
    public Action<int> MoveServerLoginCommandUp => ServerCommands.MoveLoginCommandUp;
    public IReadOnlyList<uint> ServerLoginDownIcons => ServerCommands.LoginDownIcons;
    public Action<int> MoveServerLoginCommandDown =>
        ServerCommands.MoveLoginCommandDown;
    public IReadOnlyList<uint> ServerLoginDeleteIcons =>
        ServerCommands.LoginDeleteIcons;
    public Action<int> DeleteServerLoginCommand => ServerCommands.DeleteLoginCommand;
    public int ServerLoginSelectedRow => ServerCommands.LoginSelectedRow;
    public Action<int> SelectServerLoginRow => ServerCommands.SelectLoginRow;

    public string ServerLoginCompleteText => ServerCommands.LoginCompleteText;
    public Action<string> SetServerLoginCompleteText =>
        ServerCommands.SetLoginCompleteText;
    public Action AddServerLoginCompleteCommand =>
        ServerCommands.AddLoginCompleteCommand;
    public IReadOnlyList<string> ServerLoginCompleteCommands =>
        ServerCommands.LoginCompleteCommands;
    public IReadOnlyList<uint> ServerLoginCompleteUpIcons =>
        ServerCommands.LoginCompleteUpIcons;
    public Action<int> MoveServerLoginCompleteCommandUp =>
        ServerCommands.MoveLoginCompleteCommandUp;
    public IReadOnlyList<uint> ServerLoginCompleteDownIcons =>
        ServerCommands.LoginCompleteDownIcons;
    public Action<int> MoveServerLoginCompleteCommandDown =>
        ServerCommands.MoveLoginCompleteCommandDown;
    public IReadOnlyList<uint> ServerLoginCompleteDeleteIcons =>
        ServerCommands.LoginCompleteDeleteIcons;
    public Action<int> DeleteServerLoginCompleteCommand =>
        ServerCommands.DeleteLoginCompleteCommand;
    public int ServerLoginCompleteSelectedRow =>
        ServerCommands.LoginCompleteSelectedRow;
    public Action<int> SelectServerLoginCompleteRow =>
        ServerCommands.SelectLoginCompleteRow;

    public string ServerPeriodicText => ServerCommands.PeriodicText;
    public Action<string> SetServerPeriodicText => ServerCommands.SetPeriodicText;
    public string ServerPeriodicIntervalText => ServerCommands.PeriodicIntervalText;
    public Action<string> SetServerPeriodicInterval =>
        ServerCommands.SetPeriodicInterval;
    public string ServerPeriodicOffsetText => ServerCommands.PeriodicOffsetText;
    public Action<string> SetServerPeriodicOffset =>
        ServerCommands.SetPeriodicOffset;
    public Action AddServerPeriodicCommand => ServerCommands.AddPeriodicCommand;
    public IReadOnlyList<string> ServerPeriodicCommands =>
        ServerCommands.PeriodicCommands;
    public IReadOnlyList<string> ServerPeriodicIntervals =>
        ServerCommands.PeriodicIntervals;
    public IReadOnlyList<string> ServerPeriodicOffsets =>
        ServerCommands.PeriodicOffsets;
    public IReadOnlyList<uint> ServerPeriodicDeleteIcons =>
        ServerCommands.PeriodicDeleteIcons;
    public Action<int> DeleteServerPeriodicCommand =>
        ServerCommands.DeletePeriodicCommand;
    public int ServerPeriodicSelectedRow => ServerCommands.PeriodicSelectedRow;
    public Action<int> SelectServerPeriodicRow => ServerCommands.SelectPeriodicRow;

    // ---- Misc ----------------------------------------------------------------

    public IReadOnlyList<bool> OptionChecks => MiscOptions.Options.Checks;
    public Action<int> ToggleOption => MiscOptions.Options.Toggle;
    public IReadOnlyList<string> OptionCaptions => MiscOptions.Options.Captions;
    public int OptionSelectedRow => MiscOptions.Options.SelectedRow;
    public Action<int> SelectOption => MiscOptions.Options.Select;
    public string OutputWindowText => MiscOptions.OutputWindowText;
    public Action<string> SetOutputWindow => MiscOptions.SetOutputWindow;

    public IReadOnlyList<bool> FilterChecks => Filters.Options.Checks;
    public Action<int> ToggleFilter => Filters.Options.Toggle;
    public IReadOnlyList<string> FilterCaptions => Filters.Options.Captions;
    public int FilterSelectedRow => Filters.Options.SelectedRow;
    public Action<int> SelectFilter => Filters.Options.Select;

    public string AboutThanksText => About.ThanksText;
    public string AboutThanksUrl => About.ThanksUrl;
    public string AboutWebsite => About.Website;
    public string AboutVersionText => About.VersionText;
}
