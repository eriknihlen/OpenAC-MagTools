using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Tests.Fakes;

/// <summary>
/// The one fake host every test uses. Each surface is settable so a test names
/// only the state it cares about.
/// </summary>
public sealed class FakeHost : IPluginHost
{
    public bool HasUi { get; set; } = true;

    public IPluginLogger Log { get; } = new RecordingLogger();

    public IGameState State { get; } = new FakeGameState();

    public FakeEvents Events { get; } = new();

    public FakeSelection Selection { get; } = new();

    public FakeUiRegistry Ui { get; } = new();

    public FakeCommandRegistry Commands { get; } = new();

    public MemoryStorage Storage { get; } = new();

    public FakeAutomation Automation { get; } = new();

    public FakeLootClassifierRegistry LootClassifiers { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public FakeHotkeyRegistry Hotkeys { get; } = new();

    public FakeHostWindow Window { get; } = new();

    IEvents IPluginHost.Events => Events;

    ISelectionService IPluginHost.Selection => Selection;

    IUiRegistry IPluginHost.Ui => Ui;

    IPluginCommandRegistry IPluginHost.Commands => Commands;

    IPluginStorage IPluginHost.Storage => Storage;

    IAutomationSurface IPluginHost.Automation => Automation;

    IPluginLootClassifierRegistry IPluginHost.LootClassifiers => LootClassifiers;

    IPluginClipboard IPluginHost.Clipboard => Clipboard;

    IHotkeyRegistry IPluginHost.Hotkeys => Hotkeys;

    IHostWindow IPluginHost.Window => Window;

    /// <summary>The chat lines the plugin posted, in order.</summary>
    public IReadOnlyList<string> ChatLines => Automation.Chat.Posted;
}

/// <summary>
/// A settable <see cref="IHostWindow"/>: records every call, defaults to
/// <see cref="HostWindowStatus.Done"/> like a graphical host with a
/// responsive window, and a test can flip <see cref="MinimizeResult"/>/
/// <see cref="RestoreResult"/>/<see cref="RequestCloseResult"/> to simulate a
/// refusal (a headless host, or an unconfirmed platform outcome such as
/// Wayland's always-Unavailable Minimize).
/// </summary>
public sealed class FakeHostWindow : IHostWindow
{
    public int MinimizeCalls { get; private set; }

    public int RestoreCalls { get; private set; }

    public int RequestCloseCalls { get; private set; }

    public bool IsMinimized { get; set; }

    public HostWindowResult MinimizeResult { get; set; } = new(HostWindowStatus.Done);

    public HostWindowResult RestoreResult { get; set; } = new(HostWindowStatus.Done);

    public HostWindowResult RequestCloseResult { get; set; } = new(HostWindowStatus.Done);

    public HostWindowResult Minimize()
    {
        MinimizeCalls++;
        if (MinimizeResult.Succeeded)
            IsMinimized = true;
        return MinimizeResult;
    }

    public HostWindowResult Restore()
    {
        RestoreCalls++;
        if (RestoreResult.Succeeded)
            IsMinimized = false;
        return RestoreResult;
    }

    public HostWindowResult RequestClose()
    {
        RequestCloseCalls++;
        return RequestCloseResult;
    }
}

/// <summary>A settable <see cref="IPluginClipboard"/>: records every write, can fail on demand.</summary>
public sealed class FakeClipboard : IPluginClipboard
{
    public List<string> Written { get; } = [];

    public bool Available { get; set; } = true;

    public bool TrySetText(string text)
    {
        if (!Available)
            return false;
        Written.Add(text);
        return true;
    }
}

/// <summary>
/// A settable <see cref="IPluginLootClassifierRegistry"/>: one test-controlled
/// classifier answers <see cref="TryClassify"/>/<see cref="TryNeedsIdentification"/>/
/// <see cref="TryClassifyWithProfile"/>, or the registry reports no classifier
/// registered at all (the "VTank isn't loaded" degradation path) when
/// <see cref="Available"/> is left empty.
/// </summary>
public sealed class FakeLootClassifierRegistry : IPluginLootClassifierRegistry
{
    public List<PluginLootClassifierInfo> Available { get; } = [];

    /// <summary>What <see cref="TryClassify"/> returns for any classifier id.</summary>
    public PluginLootClassification? ClassificationResult { get; set; }

    /// <summary>What <see cref="TryNeedsIdentification"/> returns for any classifier id.</summary>
    public bool NeedsIdentificationResult { get; set; }

    /// <summary>
    /// When set, overrides <see cref="NeedsIdentificationResult"/> with a
    /// per-context verdict -- for a test where only SOME items in a
    /// container still need identification.
    /// </summary>
    public Func<PluginLootClassificationContext, bool>? NeedsIdentificationHandler { get; set; }

    /// <summary>What <see cref="TryClassifyWithProfile"/> returns for any classifier id.</summary>
    public PluginLootClassification? ProfileClassificationResult { get; set; }

    /// <summary>
    /// When set, overrides <see cref="ClassificationResult"/> with a
    /// per-context verdict — for a test that needs different items to
    /// classify differently. Returning null from the function means "no
    /// match" (mirrors the live registry's <c>false</c> return).
    /// </summary>
    public Func<PluginLootClassificationContext, PluginLootClassification?>? ClassifyHandler { get; set; }

    /// <summary>
    /// When set, overrides <see cref="ProfileClassificationResult"/> with a
    /// per-profile, per-context verdict. Returning null means "no profile,
    /// or no match" (both collapse to the registry's <c>false</c>).
    /// </summary>
    public Func<string, PluginLootClassificationContext, PluginLootClassification?>? ProfileClassifyHandler { get; set; }

    /// <summary>Every profile name ever probed/classified via <see cref="TryClassifyWithProfile"/>.</summary>
    public List<string> ProfileNamesSeen { get; } = [];

    public List<string> ClassifyCalls { get; } = [];

    IReadOnlyList<PluginLootClassifierInfo> IPluginLootClassifierRegistry.Available => Available;

    public bool TryClassify(
        string classifierId,
        in PluginLootClassificationContext context,
        out PluginLootClassification classification)
    {
        ClassifyCalls.Add(classifierId);

        if (ClassifyHandler is { } handler)
        {
            PluginLootClassification? result = handler(context);
            if (result is { } found)
            {
                classification = found;
                return true;
            }

            classification = default;
            return false;
        }

        if (ClassificationResult is { } fixedResult)
        {
            classification = fixedResult;
            return true;
        }

        classification = default;
        return false;
    }

    public bool TryNeedsIdentification(
        string classifierId,
        in PluginLootClassificationContext context)
        => NeedsIdentificationHandler is { } handler ? handler(context) : NeedsIdentificationResult;

    public bool TryClassifyWithProfile(
        string classifierId,
        string profileName,
        in PluginLootClassificationContext context,
        out PluginLootClassification classification)
    {
        ProfileNamesSeen.Add(profileName);

        if (ProfileClassifyHandler is { } handler)
        {
            PluginLootClassification? result = handler(profileName, context);
            if (result is { } found)
            {
                classification = found;
                return true;
            }

            classification = default;
            return false;
        }

        if (ProfileClassificationResult is { } fixedResult)
        {
            classification = fixedResult;
            return true;
        }

        classification = default;
        return false;
    }
}

/// <summary>Records every registration and lets a test fire the handler or flip <see cref="IPluginHotkeyRegistration.IsBound"/>.</summary>
public sealed class FakeHotkeyRegistry : IHotkeyRegistry
{
    public List<(string Id, string DisplayName, PluginKeyChord DefaultChord, Action Handler)> Registrations { get; } = [];

    /// <summary>The most recently returned handle for each id, so a test can assert on its Disposed/IsBound state.</summary>
    public Dictionary<string, FakeHotkeyRegistration> Handles { get; } = new(StringComparer.Ordinal);

    public IPluginHotkeyRegistration Register(
        string id,
        string displayName,
        PluginKeyChord defaultChord,
        Action handler)
    {
        Registrations.Add((id, displayName, defaultChord, handler));
        var registration = new FakeHotkeyRegistration(defaultChord);
        Handles[id] = registration;
        return registration;
    }

    /// <summary>Invokes the handler registered under <paramref name="id"/>, as if the chord had just fired.</summary>
    public void Fire(string id)
    {
        foreach ((string registeredId, _, _, Action handler) in Registrations)
        {
            if (string.Equals(registeredId, id, StringComparison.Ordinal))
            {
                handler();
                return;
            }
        }
    }
}

public sealed class FakeHotkeyRegistration(PluginKeyChord chord) : IPluginHotkeyRegistration
{
    public bool IsBound { get; set; } = true;
    public PluginKeyChord EffectiveChord { get; private set; } = chord;
    public bool Disposed { get; private set; }

    public void Rebind(PluginKeyChord newChord) => EffectiveChord = newChord;

    public void Dispose() => Disposed = true;
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

    public event Action<PluginObjectChange>? ObjectChanged;

    public event Action<uint>? ContainerOpened;

    public event Action<uint>? ContainerClosed;

    public event Action<PluginConfirmation>? ConfirmationRequested;

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

    public void RaiseObjectChanged(PluginObjectChange change) => ObjectChanged?.Invoke(change);

    public void RaiseObjectChanged(uint objectId, PluginObjectChangeKind kind)
        => ObjectChanged?.Invoke(new PluginObjectChange(objectId, kind));

    public void RaiseContainerOpened(uint containerObjectId) => ContainerOpened?.Invoke(containerObjectId);

    public void RaiseContainerClosed(uint containerObjectId) => ContainerClosed?.Invoke(containerObjectId);

    public void RaiseConfirmationRequested(PluginConfirmation confirmation)
        => ConfirmationRequested?.Invoke(confirmation);

    event Action<PluginConfirmation> IEvents.ConfirmationRequested
    {
        add => ConfirmationRequested += value;
        remove => ConfirmationRequested -= value;
    }

    event Action<PluginObjectChange> IEvents.ObjectChanged
    {
        add => ObjectChanged += value;
        remove => ObjectChanged -= value;
    }

    event Action<uint> IEvents.ContainerOpened
    {
        add => ContainerOpened += value;
        remove => ContainerOpened -= value;
    }

    event Action<uint> IEvents.ContainerClosed
    {
        add => ContainerClosed += value;
        remove => ContainerClosed -= value;
    }

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

    /// <summary>Every <see cref="ShowClientWindow"/> call, in order -- a test sets <see cref="ShowClientWindowResult"/> to simulate the host refusing it.</summary>
    public List<PluginClientWindow> ShowClientWindowCalls { get; } = [];

    public bool ShowClientWindowResult { get; set; } = true;

    public bool ShowClientWindow(PluginClientWindow window)
    {
        ShowClientWindowCalls.Add(window);
        return ShowClientWindowResult;
    }

    public List<PluginClientWindow> HideClientWindowCalls { get; } = [];

    public bool HideClientWindow(PluginClientWindow window)
    {
        HideClientWindowCalls.Add(window);
        return true;
    }

    public List<PluginClientWindow> ToggleClientWindowCalls { get; } = [];

    public bool ToggleClientWindowResult { get; set; } = true;

    public bool ToggleClientWindow(PluginClientWindow window)
    {
        ToggleClientWindowCalls.Add(window);
        return ToggleClientWindowResult;
    }

    public HashSet<PluginClientWindow> VisibleClientWindows { get; } = [];

    public bool IsClientWindowVisible(PluginClientWindow window)
        => VisibleClientWindows.Contains(window);

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
