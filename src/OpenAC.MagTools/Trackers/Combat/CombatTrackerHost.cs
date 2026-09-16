using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Trackers.Combat.Aetheria;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;
using ChatMessageKind = OpenAC.MagTools.Chat.ChatMessageKind;

namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// The composition point for P4: owns the current-session and persistent
/// <see cref="CombatTracker"/> instances, the single chat subscription that
/// feeds both of them (see <see cref="StandardTracker"/>'s remarks for why
/// there is only one), the 10-minute persistent-stats save, and the
/// import-on-login / export-on-logoff lifecycle described in the port
/// design's §0 (<c>PluginCore.cs</c> lifecycle section).
/// </summary>
public sealed class CombatTrackerHost
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(10);

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly SettingsManager _settings;
    private Action<PluginChatMessage>? _onReceived;
    private IDisposable? _saveRegistration;
    private string _server = string.Empty;
    private string _character = string.Empty;
    private bool _running;

    public CombatTrackerHost(IPluginHost host, ChatOutput chat, SettingsManager settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _chat = chat;
        _settings = settings;
    }

    public CombatTracker Current { get; } = new();

    public CombatTracker Persistent { get; } = new();

    /// <summary>Begins tracking for the session that just started.</summary>
    public void Start(TickScheduler scheduler, string worldName, string characterName)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;

        _running = true;
        _server = worldName ?? string.Empty;
        _character = characterName ?? string.Empty;

        Import();

        _onReceived = OnReceived;
        _host.Automation.Chat.Received += _onReceived;

        _saveRegistration = scheduler.Every(SaveInterval, SavePersistent);
    }

    /// <summary>Ends tracking for the session that just ended.</summary>
    public void Stop()
    {
        if (!_running)
            return;

        _running = false;

        if (_onReceived is not null)
            _host.Automation.Chat.Received -= _onReceived;
        _onReceived = null;

        _saveRegistration?.Dispose();
        _saveRegistration = null;

        SavePersistent();

        if (_settings.CombatTracker.ExportOnLogOff.Value)
            ExportCurrent(showMessage: true);
    }

    /// <summary>The Options page's "Clear Current Stats" button.</summary>
    public void ClearCurrent() => Current.ClearStats();

    /// <summary>The Options page's "Clear Persistent Stats" button.</summary>
    public void ClearPersistent() => Persistent.ClearStats();

    /// <summary>The Options page's "Export Current Stats" button.</summary>
    public void ExportCurrent() => ExportCurrent(showMessage: true);

    private void ExportCurrent(bool showMessage)
    {
        if (string.IsNullOrEmpty(_character))
            return;

        string? xml = CombatTrackerExporter.Export(
            Current.CombatInfos, Current.AetheriaInfos, Current.CloakInfos);
        if (xml is null)
            return;

        string key = TimestampedKey();
        WriteStorage(key, xml);

        if (showMessage)
            _chat.Write("Stats exported to: " + DisplayPath(key));
    }

    private void SavePersistent()
    {
        if (!_settings.CombatTracker.Persistent.Value)
            return;
        if (string.IsNullOrEmpty(_character))
            return;

        string? xml = CombatTrackerExporter.Export(
            Persistent.CombatInfos, Persistent.AetheriaInfos, Persistent.CloakInfos);
        if (xml is not null)
            WriteStorage(PersistentKey, xml);
    }

    private void Import()
    {
        if (!_settings.CombatTracker.Persistent.Value)
            return;
        if (string.IsNullOrEmpty(_character))
            return;
        if (!_host.Storage.IsAvailable)
            return;

        string? xml = _host.Storage.ReadText(PersistentKey);
        if (CombatTrackerImporter.Import(
                xml, out List<CombatInfo> combatInfos, out List<AetheriaInfo> aetheriaInfos,
                out List<CloakInfo> cloakInfos))
        {
            Persistent.LoadStats(combatInfos, aetheriaInfos, cloakInfos);
        }
    }

    private void WriteStorage(string key, string content)
    {
        if (!_host.Storage.IsAvailable)
            return;
        _host.Storage.WriteText(key, content);
    }

    private string PersistentKey => _server + "/" + _character + ".CombatTracker.xml";

    private string TimestampedKey() =>
        _server + "/" + _character + ".CombatTracker."
        + DateTime.Now.ToString(
            "yyyy-MM-dd HH-mm", System.Globalization.CultureInfo.InvariantCulture)
        + ".xml";

    private string DisplayPath(string key) =>
        _host.Storage.RootPath is { Length: > 0 } root ? root.TrimEnd('/', '\\') + "/" + key : key;

    /// <summary>
    /// Parses one delivered chat line and forwards whatever it recognizes to
    /// both <see cref="Current"/> and <see cref="Persistent"/>. Only lines
    /// classified as combat (host <c>PluginChatMessage.Kind == 8</c>) reach
    /// the parsers — this replaces the original's own <c>Util.IsChat</c>
    /// guard inside each tracker, since the host already tells a plugin
    /// which lines are combat lines.
    /// </summary>
    private void OnReceived(PluginChatMessage message)
    {
        ChatClassifier.ChatLine line = ChatClassifier.Classify(message, _host.Automation);
        if (line.Kind != ChatMessageKind.Combat)
            return;

        string text = line.Message;
        if (string.IsNullOrEmpty(text))
            return;

        string localPlayerName = _host.Automation.Character.Name;

        CombatEventArgs? combatEvent =
            StandardTracker.Parse(text, localPlayerName, diagnostic => _chat.Write(diagnostic));
        if (combatEvent is not null)
        {
            Current.OnCombatEvent(combatEvent, localPlayerName);
            Persistent.OnCombatEvent(combatEvent, localPlayerName);
        }

        Aetheria.SurgeEventArgs? aetheriaEvent = AetheriaTracker.Parse(text);
        if (aetheriaEvent is not null)
        {
            Current.OnAetheriaSurge(aetheriaEvent, localPlayerName);
            Persistent.OnAetheriaSurge(aetheriaEvent, localPlayerName);
        }

        Cloaks.SurgeEventArgs? cloakEvent = Cloaks.CloakTracker.Parse(text, localPlayerName);
        if (cloakEvent is not null)
        {
            Current.OnCloakSurge(cloakEvent, localPlayerName);
            Persistent.OnCloakSurge(cloakEvent, localPlayerName);
        }
    }
}
