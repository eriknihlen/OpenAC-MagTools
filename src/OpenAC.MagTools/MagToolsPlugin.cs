using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Commands;
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
    private Action<double>? _tick;
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
        _main = new MainViewModel(_settings);
        _hud = new HudViewModel(host);
        _router = new MtCommandRouter(host, _chat, _settings);
        _session = new SessionContext(host, _chat);

        host.Log.Info("Mag-Tools initialized");
    }

    public void Enable()
    {
        if (_host is null || _chat is null || _settingsFile is null
            || _main is null || _hud is null || _router is null || _session is null)
            return;

        _scheduler = new TickScheduler(_host.Events, _chat);

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

        _scheduler?.Dispose();
        _scheduler = null;

        // Anything the debounce still owes is written before we go.
        _settingsFile?.Flush();

        // The session survives Initialize (it is built once), so without
        // this an Enable that follows a Disable would still see the previous
        // in-world state and never re-fire the login edge.
        _session?.Reset();

        _host?.Log.Info("Mag-Tools disabled");
    }

    private void OnTick(double elapsedSeconds)
    {
        try
        {
            _session?.Poll();
            _settingsFile?.Tick(elapsedSeconds);
        }
        catch (Exception exception)
        {
            _chat?.WriteException(exception);
        }
    }
}
