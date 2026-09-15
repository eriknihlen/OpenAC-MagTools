using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Ui;

/// <summary>
/// A checkbox list over a set of settings: the check column shows each
/// setting's value and the text column shows its GUI caption, exactly as the
/// original generated the Options and Filters rows from the setting table.
/// </summary>
/// <remarks>
/// Check cells are not two-way in the markup. The host reports the row index
/// and this class flips the setting itself and rebuilds the bound list.
/// </remarks>
public sealed class OptionListViewModel : IDisposable
{
    private readonly IReadOnlyList<ISetting> _settings;
    private bool _disposed;

    public OptionListViewModel(IReadOnlyList<ISetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Captions = [.. settings.Select(static setting => setting.Description)];
        Checks = ReadChecks();
        Toggle = ToggleRow;
        Select = index => SelectedRow = index;

        // A setting can change from outside a click on this list's own
        // rows — most visibly the parent/child cascade (turning a child on
        // turns its parent on too), but also /mt opt set and a settled
        // reload. Without this, Checks stays a stale snapshot from
        // construction until something happens to call Refresh() again.
        foreach (ISetting setting in _settings)
            setting.Changed += OnSettingChanged;
    }

    /// <summary>
    /// Unsubscribes from every setting's <see cref="ISetting.Changed"/> event.
    /// Without this, a view-model that outlives its bound page (or is rebuilt
    /// on every panel mount) leaks a subscription per setting per instance,
    /// each of which keeps firing <see cref="Refresh"/> for a list nobody
    /// reads anymore.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (ISetting setting in _settings)
            setting.Changed -= OnSettingChanged;
    }

    private void OnSettingChanged(ISetting setting) => Refresh();

    public IReadOnlyList<string> Captions { get; }

    public IReadOnlyList<bool> Checks { get; private set; }

    public Action<int> Toggle { get; }

    public int SelectedRow { get; private set; } = -1;

    public Action<int> Select { get; }

    /// <summary>Re-reads every value, for when a setting changed elsewhere.</summary>
    public void Refresh() => Checks = ReadChecks();

    private void ToggleRow(int index)
    {
        if (index < 0 || index >= _settings.Count)
            return;

        ISetting setting = _settings[index];
        bool current = string.Equals(
            setting.ValueText,
            "True",
            StringComparison.OrdinalIgnoreCase);
        setting.TrySetFromText(current ? "False" : "True");
        Refresh();
    }

    private bool[] ReadChecks()
    {
        var checks = new bool[_settings.Count];
        for (int index = 0; index < _settings.Count; index++)
        {
            checks[index] = string.Equals(
                _settings[index].ValueText,
                "True",
                StringComparison.OrdinalIgnoreCase);
        }

        return checks;
    }
}
