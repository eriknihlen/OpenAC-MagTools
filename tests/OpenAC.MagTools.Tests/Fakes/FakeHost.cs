using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Tests.Fakes;

/// <summary>
/// The one fake host every test uses. Each surface is settable so a test names
/// only the state it cares about.
/// </summary>
public sealed class FakeHost : IPluginHost
{
    public bool HasUi { get; set; }

    public IPluginLogger Log { get; } = new RecordingLogger();

    public IGameState State { get; } = new FakeGameState();

    public FakeEvents Events { get; } = new();

    public FakeSelection Selection { get; } = new();

    public FakeUiRegistry Ui { get; } = new();

    public FakeCommandRegistry Commands { get; } = new();

    public MemoryStorage Storage { get; } = new();

    public FakeAutomation Automation { get; } = new();

    IEvents IPluginHost.Events => Events;

    ISelectionService IPluginHost.Selection => Selection;

    IUiRegistry IPluginHost.Ui => Ui;

    IPluginCommandRegistry IPluginHost.Commands => Commands;

    IPluginStorage IPluginHost.Storage => Storage;

    IAutomationSurface IPluginHost.Automation => Automation;

    /// <summary>The chat lines the plugin posted, in order.</summary>
    public IReadOnlyList<string> ChatLines => Automation.Chat.Posted;
}

public sealed class RecordingLogger : IPluginLogger
{
    public List<string> Messages { get; } = [];

    public void Info(string message) => Messages.Add("info: " + message);

    public void Warn(string message) => Messages.Add("warn: " + message);

    public void Error(string message, Exception? exception = null)
        => Messages.Add("error: " + message);
}

public sealed class FakeGameState : IGameState
{
    public IReadOnlyList<WorldEntitySnapshot> Entities { get; set; } = [];
}

/// <summary>Drives <see cref="IEvents"/> from a test.</summary>
public sealed class FakeEvents : IEvents
{
    public event Action<WorldEntitySnapshot>? EntitySpawned;

    public event Action<double>? Tick;

    public event Action? LoginComplete;

    public event Action? Logoff;

    public event Action<string>? LocalPlayerDied;

    public int TickSubscriberCount => Tick?.GetInvocationList().Length ?? 0;

    public int LoginCompleteSubscriberCount =>
        LoginComplete?.GetInvocationList().Length ?? 0;

    public int LogoffSubscriberCount => Logoff?.GetInvocationList().Length ?? 0;

    public void RaiseTick(double elapsedSeconds) => Tick?.Invoke(elapsedSeconds);

    public void RaiseEntitySpawned(WorldEntitySnapshot snapshot)
        => EntitySpawned?.Invoke(snapshot);

    public void RaiseLoginComplete() => LoginComplete?.Invoke();

    public void RaiseLogoff() => Logoff?.Invoke();

    public void RaiseLocalPlayerDied(string deathMessage)
        => LocalPlayerDied?.Invoke(deathMessage);

    event Action<WorldEntitySnapshot> IEvents.EntitySpawned
    {
        add => EntitySpawned += value;
        remove => EntitySpawned -= value;
    }

    event Action<double> IEvents.Tick
    {
        add => Tick += value;
        remove => Tick -= value;
    }

    event Action IEvents.LoginComplete
    {
        add => LoginComplete += value;
        remove => LoginComplete -= value;
    }

    event Action IEvents.Logoff
    {
        add => Logoff += value;
        remove => Logoff -= value;
    }

    event Action<string> IEvents.LocalPlayerDied
    {
        add => LocalPlayerDied += value;
        remove => LocalPlayerDied -= value;
    }
}

public sealed class FakeSelection : ISelectionService
{
    public uint? SelectedObjectId { get; private set; }

    public uint? PreviousObjectId { get; private set; }

    public event Action<SelectionChangedEvent>? Changed;

    public bool Select(uint objectId)
    {
        PreviousObjectId = SelectedObjectId;
        SelectedObjectId = objectId;
        Changed?.Invoke(new SelectionChangedEvent(PreviousObjectId, objectId));
        return true;
    }

    public bool Clear()
    {
        PreviousObjectId = SelectedObjectId;
        SelectedObjectId = null;
        Changed?.Invoke(new SelectionChangedEvent(PreviousObjectId, null));
        return true;
    }
}

public sealed record RegisteredPanel(
    PluginPanelDescriptor Descriptor,
    string MarkupPath,
    object Binding);

public sealed class FakeUiRegistry : IUiRegistry
{
    public List<RegisteredPanel> Panels { get; } = [];

    public void AddMarkupPanel(string markupPath, object binding)
        => Panels.Add(new RegisteredPanel(
            new PluginPanelDescriptor(
                Path.GetFileNameWithoutExtension(markupPath),
                "Mag-Tools"),
            markupPath,
            binding));

    public void AddPanel(
        PluginPanelDescriptor descriptor,
        string markupPath,
        object binding)
        => Panels.Add(new RegisteredPanel(descriptor, markupPath, binding));

    public IDisposable RegisterPanel(
        PluginPanelDescriptor descriptor,
        string markupPath,
        object binding)
    {
        var panel = new RegisteredPanel(descriptor, markupPath, binding);
        Panels.Add(panel);
        return new PanelRegistration(this, panel);
    }

    private sealed class PanelRegistration(FakeUiRegistry owner, RegisteredPanel panel)
        : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            owner.Panels.Remove(panel);
        }
    }
}

public sealed class FakeCommandRegistry : IPluginCommandRegistry
{
    public Dictionary<string, Action<PluginCommand>> Handlers { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Register(string verb, Action<PluginCommand> handler)
    {
        Handlers[verb] = handler;
        return new Registration(this, verb);
    }

    private sealed class Registration(FakeCommandRegistry owner, string verb)
        : IDisposable
    {
        public void Dispose() => owner.Handlers.Remove(verb);
    }
}

/// <summary>An in-memory <see cref="IPluginStorage"/>.</summary>
public sealed class MemoryStorage : IPluginStorage
{
    private readonly Dictionary<string, string> _files =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable { get; set; } = true;

    public string? RootPath { get; set; } = "memory://storage";

    public int WriteCount { get; private set; }

    /// <summary>Makes the next (and every subsequent) write throw.</summary>
    public bool ThrowOnWrite { get; set; }

    /// <summary>
    /// The message <see cref="WriteText"/> throws with while
    /// <see cref="ThrowOnWrite"/> is set. Change it between calls to make
    /// consecutive throws distinct EXCEPTION SHAPES for a test — the default
    /// keeps every throw identical, for a test pinning same-shape dedup.
    /// </summary>
    public string ThrowMessage { get; set; } = "Storage write failed (test fault).";

    public string? ReadText(string key)
        => _files.TryGetValue(key, out string? value) ? value : null;

    public IReadOnlyList<string> List(string prefix)
        => [.. _files.Keys.Where(key =>
            key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];

    public void WriteText(string key, string content)
    {
        if (ThrowOnWrite)
            throw new InvalidOperationException(ThrowMessage);

        _files[key] = content;
        WriteCount++;
    }

    public bool Delete(string key) => _files.Remove(key);

    /// <summary>Seeds a file as if it had been copied in by hand.</summary>
    public void Seed(string key, string content) => _files[key] = content;
}
