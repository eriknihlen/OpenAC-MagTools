using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Chat;
using OpenAC.MagTools.Commands;
using OpenAC.MagTools.Inventory;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Ui;

namespace OpenAC.MagTools;

/// <summary>
/// The composition root. It builds the settings document, the chat sink, the
/// tick scheduler and the session-edge detector, registers the <c>/mt</c> verb
/// and mounts the two windows.
/// </summary>
public sealed class MagToolsPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private SettingsFile? _settingsFile;
    private SettingsManager? _settings;
    private ChatOutput? _chat;
    private MainViewModel? _main;
    private HudViewModel? _hud;
    private MtCommandRouter? _router;
    private TickScheduler? _scheduler;
    private SessionContext? _session;
    private ChatFilter? _chatFilter;
    private Loggers.Chat.ChatLogger? _chatLogger;
    private LootRuleProcessor? _lootRules;
    private ItemInfoPrinter? _itemInfoPrinter;
    private UserIdentDetector? _userIdentDetector;
    private ContainerIdentDetector? _containerIdentDetector;
    private InventoryExporter? _inventoryExporter;
    private Action<ItemIdentArgs>? _onUserItemIdentified;
    private Action<ItemIdentArgs>? _onContainerItemIdentified;
    private Action<double>? _tick;
    private Action? _onSessionLoginComplete;
    private Action? _onSessionLogoff;
    private IDisposable? _commandRegistration;
    private IDisposable? _mainPanelRegistration;
    private IDisposable? _hudPanelRegistration;

    public void Initialize(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _chat = new ChatOutput(host);
        _settingsFile = new SettingsFile(host.Storage);
        _settings = new SettingsManager(_settingsFile);
        _chatFilter = new ChatFilter(host, _settings);
        _chatLogger = new Loggers.Chat.ChatLogger(host, _settings);
        _main = new MainViewModel(_settings, _chatLogger, host);
        _hud = new HudViewModel(host);
        _router = new MtCommandRouter(host, _chat, _settings);
        _session = new SessionContext(host, _chat);
        _lootRules = new LootRuleProcessor(host.LootClassifiers);
        _itemInfoPrinter = new ItemInfoPrinter(host, _chat, _settings.ItemInfoOnIdent, _lootRules);

        host.Log.Info("Mag-Tools initialized");
    }

    public void Enable()
    {
        if (_host is null || _chat is null || _settingsFile is null || _settings is null
            || _main is null || _hud is null || _router is null || _session is null)
            return;

        _scheduler = new TickScheduler(_host.Events, _chat);

        _inventoryExporter = new InventoryExporter(
            _host, _chat, _settings.ItemInfoOnIdent, _host.Automation.Spells, _scheduler);
        _main.InventoryTools.Exporter = _inventoryExporter;

        if (_itemInfoPrinter is not null)
        {
            _userIdentDetector = new UserIdentDetector(_host, _settings.ItemInfoOnIdent);
            _onUserItemIdentified = args => _itemInfoPrinter.Print(args);
            _userIdentDetector.ItemIdentified += _onUserItemIdentified;
            _userIdentDetector.Start();

            _containerIdentDetector = new ContainerIdentDetector(
                _host, _settings.ItemInfoOnIdent, _scheduler);
            _onContainerItemIdentified = args => _itemInfoPrinter.Print(args);
            _containerIdentDetector.ItemIdentified += _onContainerItemIdentified;
            _containerIdentDetector.Start();
        }

        // Reverses the unsubscribe a previous Disable() did, for a
        // Disable()/Enable() cycle that reuses this same MainViewModel
        // instance (Initialize() only builds it once). A no-op the first
        // time Enable() runs, since nothing has been disposed yet.
        _main.Resubscribe();

        // Markup ships beside the plugin assembly, which is not the host's
        // working directory.
        string directory =
            Path.GetDirectoryName(typeof(MagToolsPlugin).Assembly.Location) ?? ".";

        if (_host.HasUi)
        {
            _mainPanelRegistration = _host.Ui.RegisterPanel(
                new PluginPanelDescriptor("main", "Mag-Tools")
                {
                    IconText = "MT",
                    StartVisible = false,
                    ShowInSidePanel = true,
                },
                Path.Combine(directory, "magtools.xml"),
                _main);

            _hudPanelRegistration = _host.Ui.RegisterPanel(
                new PluginPanelDescriptor("hud", "Mag-Tools HUD")
                {
                    IconText = "MH",
                    StartVisible = false,
                    ShowInSidePanel = true,
                },
                Path.Combine(directory, "magtools-hud.xml"),
                _hud);
        }

        _commandRegistration = _host.Commands.Register("mt", _router.Execute);

        _tick = OnTick;
        _host.Events.Tick += _tick;

        _chatFilter?.Enable();

        _onSessionLoginComplete = OnSessionLoginComplete;
        _onSessionLogoff = OnSessionLogoff;
        _session.LoginComplete += _onSessionLoginComplete;
        _session.Logoff += _onSessionLogoff;
        _session.Subscribe();

        _host.Log.Info(
            _host.Automation.IsAvailable
                ? "Mag-Tools enabled"
                : "Mag-Tools enabled (no live session yet)");
    }

    public void Disable()
    {
        if (_host is not null && _tick is not null)
            _host.Events.Tick -= _tick;
        _tick = null;

        _commandRegistration?.Dispose();
        _commandRegistration = null;

        _mainPanelRegistration?.Dispose();
        _mainPanelRegistration = null;
        _hudPanelRegistration?.Dispose();
        _hudPanelRegistration = null;

        if (_userIdentDetector is not null)
        {
            if (_onUserItemIdentified is not null)
                _userIdentDetector.ItemIdentified -= _onUserItemIdentified;
            _userIdentDetector.Dispose();
            _userIdentDetector = null;
        }
        _onUserItemIdentified = null;

        if (_containerIdentDetector is not null)
        {
            if (_onContainerItemIdentified is not null)
                _containerIdentDetector.ItemIdentified -= _onContainerItemIdentified;
            _containerIdentDetector.Dispose();
            _containerIdentDetector = null;
        }
        _onContainerItemIdentified = null;

        if (_main is not null)
            _main.InventoryTools.Exporter = null;
        _inventoryExporter = null;

        _scheduler?.Dispose();
        _scheduler = null;

        _chatFilter?.Disable();

        // In case we are still mid-session when the plugin is disabled: stop
        // the logger and flush what it has before we go, same as a real
        // logoff would.
        _chatLogger?.Stop();

        if (_session is not null)
        {
            if (_onSessionLoginComplete is not null)
                _session.LoginComplete -= _onSessionLoginComplete;
            if (_onSessionLogoff is not null)
                _session.Logoff -= _onSessionLogoff;
        }
        _onSessionLoginComplete = null;
        _onSessionLogoff = null;

        // Anything the debounce still owes is written before we go.
        _settingsFile?.Flush();

        // Unsubscribes the four OptionListViewModel instances' Setting.Changed
        // handlers, so a Disable that outlives the panel does not leak them.
        _main?.Dispose();

        // The session survives Initialize (it is built once), so without
        // this an Enable that follows a Disable would still see the previous
        // in-world state and never re-fire the login edge.
        _session?.Reset();

        _reportedTickExceptions.Clear();

        _host?.Log.Info("Mag-Tools disabled");
    }

    private void OnSessionLoginComplete()
    {
        if (_session is null || _scheduler is null)
            return;
        _chatLogger?.Start(_scheduler, _session.WorldName, _session.CharacterName);
    }

    private void OnSessionLogoff()
    {
        _chatLogger?.Stop();
        _chatFilter?.OnLogoff();
    }

    /// <summary>
    /// Caps <see cref="_reportedTickExceptions"/> so a tick loop that throws
    /// many DIFFERENT exception shapes (varying data in the message, say)
    /// cannot grow this set without bound for the life of the session.
    /// </summary>
    private const int MaxReportedTickExceptionShapes = 32;

    private readonly HashSet<string> _reportedTickExceptions = new(StringComparer.Ordinal);

    private void OnTick(double elapsedSeconds)
    {
        try
        {
            _settingsFile?.Tick(elapsedSeconds);
        }
        catch (Exception exception)
        {
            // A tick fires many times a second, so an exception that recurs
            // every frame (a broken invariant, not a one-off) must not flood
            // the chat window with the same report on every single frame. The
            // log gets every occurrence — that is where a real investigation
            // belongs — but chat gets the report only once per distinct
            // exception shape.
            _host?.Log.Error("Mag-Tools tick failed: " + exception.Message, exception);

            string key = exception.GetType().FullName + "|" + exception.Message;
            bool alreadyTracked = _reportedTickExceptions.Contains(key);
            if (alreadyTracked)
            {
                // Already reported once for this exact shape — stay quiet.
            }
            else if (_reportedTickExceptions.Count < MaxReportedTickExceptionShapes)
            {
                _reportedTickExceptions.Add(key);
                _chat?.WriteException(exception);
            }
            else
            {
                // At the cap: a tick loop throwing this many DIFFERENT
                // exception shapes is itself the flood the dedup exists to
                // stop — the log above already has every occurrence, so
                // this stays log-only rather than reporting every further
                // distinct shape to chat once per tick.
            }
        }
    }
}
