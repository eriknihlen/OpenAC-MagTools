namespace OpenAC.MagTools.Settings;

/// <summary>
/// One persisted value. It reads itself out of the settings document at
/// construction; assigning a different value writes through and raises
/// <see cref="Changed"/>. Assigning the value it already holds does nothing —
/// that is what keeps the child-forces-parent wiring from looping.
/// </summary>
public sealed class Setting<T> : ISetting
{
    private readonly SettingsFile _file;
    private T _value;

    public Setting(
        SettingsFile file,
        string xpath,
        string description,
        T defaultValue = default!)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrEmpty(xpath);

        _file = file;
        XPath = xpath;
        Description = description ?? string.Empty;
        DefaultValue = defaultValue;
        TypeName = SettingValueCodec.TypeName(typeof(T));
        _value = file.GetSetting(xpath, defaultValue);
    }

    public string XPath { get; }

    public string Description { get; }

    public string TypeName { get; }

    public T DefaultValue { get; }

    public T Value
    {
        get => _value;
        set
        {
            if (Equals(_value, value))
                return;

            _value = value;
            _file.PutSetting(XPath, value);
            Changed?.Invoke(this);
        }
    }

    public string ValueText => SettingValueCodec.Format(_value);

    public event Action<ISetting>? Changed;

    public bool TrySetFromText(string text)
    {
        if (!SettingValueCodec.TryParse(text, out T parsed))
            return false;
        Value = parsed;
        return true;
    }
}
