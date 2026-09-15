namespace OpenAC.MagTools.Settings;

/// <summary>
/// The type-erased face of a <see cref="Setting{T}"/>, used by the
/// <c>/mt opt</c> family and by the Options and Filters pages so neither needs
/// to know a setting's value type.
/// </summary>
public interface ISetting
{
    /// <summary>The element path inside the settings document.</summary>
    string XPath { get; }

    /// <summary>The GUI caption, and the row caption in the option lists.</summary>
    string Description { get; }

    /// <summary>One of <c>bool</c>, <c>int</c>, <c>double</c>, <c>string</c>.</summary>
    string TypeName { get; }

    /// <summary>The current value in its on-disk text form.</summary>
    string ValueText { get; }

    /// <summary>Parses and assigns; false when the text is not a legal value.</summary>
    bool TrySetFromText(string text);

    event Action<ISetting>? Changed;
}
