using System.Text;
using System.Xml;
using System.Xml.Linq;
using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Settings;

/// <summary>
/// The original's settings document: one XML file rooted at
/// <c>&lt;Mag-Tools&gt;</c> where every setting is an element addressed by a
/// <c>/</c>-separated path and holds its value as text. Missing elements are
/// created on write, so a fresh install starts from an empty root and grows.
/// </summary>
/// <remarks>
/// <para>
/// Storage is <see cref="IPluginStorage"/> under the key
/// <see cref="StorageKey"/>, which means a user can drop their existing
/// <c>Mag-Tools.xml</c> into the plugin's storage directory and it is read
/// unchanged.
/// </para>
/// <para>
/// DELIBERATE DEVIATION from the original: the original rewrote the whole file
/// on every single assignment. Here a write marks the document dirty and the
/// tick flushes it at most once per <see cref="SaveDebounce"/>; Disable flushes
/// synchronously. Semantics stay write-through — a reader always sees the new
/// value immediately — only the file I/O is coalesced.
/// </para>
/// </remarks>
public sealed class SettingsFile
{
    public const string StorageKey = "Mag-Tools.xml";
    public const string RootNodeName = "Mag-Tools";

    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(250);

    private readonly IPluginStorage _storage;
    private XDocument _document;
    private bool _dirty;
    private double _secondsSinceChange;

    public SettingsFile(IPluginStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
        _document = LoadDocument();
    }

    /// <summary>How many times the document has actually been written.</summary>
    public int SaveCount { get; private set; }

    /// <summary>True while a change is waiting for its debounced write.</summary>
    public bool HasPendingSave => _dirty;

    /// <summary>Re-reads the document from storage, discarding pending writes.</summary>
    public void Reload()
    {
        _document = LoadDocument();
        _dirty = false;
        _secondsSinceChange = 0d;
    }

    public T GetSetting<T>(string xpath, T defaultValue = default!)
    {
        XElement? element = FindElement(xpath);
        if (element is null)
            return defaultValue;
        return SettingValueCodec.TryParse(element.Value, out T parsed)
            ? parsed
            : defaultValue;
    }

    public void PutSetting<T>(string xpath, T value)
    {
        XElement element = CreateElement(xpath);
        element.Value = SettingValueCodec.Format(value);
        MarkDirty();
    }

    /// <summary>The inner texts of a node's children, in document order.</summary>
    public IReadOnlyList<string> GetChildrenInnerTexts(string xpath)
    {
        XElement? element = FindElement(xpath);
        if (element is null)
            return [];
        return [.. element.Elements().Select(static child => child.Value)];
    }

    /// <summary>Replaces a node's children with one element per value.</summary>
    public void SetNodeChildren(
        string xpath,
        string childName,
        IReadOnlyList<string> innerTexts)
    {
        ArgumentException.ThrowIfNullOrEmpty(childName);
        ArgumentNullException.ThrowIfNull(innerTexts);

        XElement? element = FindElement(xpath);
        if (element is null)
        {
            if (innerTexts.Count == 0)
                return;
            element = CreateElement(xpath);
        }

        element.RemoveAll();
        foreach (string innerText in innerTexts)
            element.Add(new XElement(childName, innerText));
        MarkDirty();
    }

    /// <summary>
    /// Replaces a node's children with one attribute-carrying element per row.
    /// </summary>
    public void SetNodeChildren(
        string xpath,
        string childName,
        IReadOnlyList<IReadOnlyDictionary<string, string>> childAttributes)
    {
        ArgumentException.ThrowIfNullOrEmpty(childName);
        ArgumentNullException.ThrowIfNull(childAttributes);

        XElement element = CreateElement(xpath);
        element.RemoveAll();
        foreach (IReadOnlyDictionary<string, string> attributes in childAttributes)
        {
            var child = new XElement(childName);
            foreach (KeyValuePair<string, string> pair in attributes)
                child.SetAttributeValue(pair.Key, pair.Value);
            element.Add(child);
        }

        MarkDirty();
    }

    /// <summary>The element at <paramref name="xpath"/>, optionally created.</summary>
    public XElement? GetNode(string xpath, bool createIfMissing = false)
        => createIfMissing ? CreateElement(xpath) : FindElement(xpath);

    /// <summary>Records that the document changed and needs a write.</summary>
    public void MarkDirty()
    {
        _dirty = true;
        _secondsSinceChange = 0d;
    }

    /// <summary>Advances the debounce and writes once the quiet period passed.</summary>
    public void Tick(double elapsedSeconds)
    {
        if (!_dirty)
            return;
        if (double.IsNaN(elapsedSeconds) || elapsedSeconds < 0d)
            elapsedSeconds = 0d;

        _secondsSinceChange += elapsedSeconds;
        if (_secondsSinceChange >= SaveDebounce.TotalSeconds)
            Flush();
    }

    /// <summary>Writes the document now if anything is pending.</summary>
    public void Flush()
    {
        if (!_dirty)
            return;

        _dirty = false;
        _secondsSinceChange = 0d;
        if (!_storage.IsAvailable)
            return;

        _storage.WriteText(StorageKey, Serialize(_document));
        SaveCount++;
    }

    /// <summary>The document's current text, as it would be written.</summary>
    public string ToXmlString() => Serialize(_document);

    private XDocument LoadDocument()
    {
        string? text = _storage.IsAvailable ? _storage.ReadText(StorageKey) : null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                XDocument loaded = XDocument.Parse(text);
                if (loaded.Root is not null)
                    return loaded;
            }
            catch (XmlException)
            {
                // A corrupt file is replaced by an empty document rather than
                // taking the plugin down, exactly as the original did.
            }
        }

        return new XDocument(new XElement(RootNodeName));
    }

    private XElement Root => _document.Root
        ?? throw new InvalidOperationException("The settings document has no root.");

    private XElement? FindElement(string xpath)
    {
        XElement? current = Root;
        foreach (string segment in SplitPath(xpath))
        {
            current = current.Element(segment);
            if (current is null)
                return null;
        }

        return current;
    }

    private XElement CreateElement(string xpath)
    {
        XElement current = Root;
        foreach (string segment in SplitPath(xpath))
        {
            XElement? child = current.Element(segment);
            if (child is null)
            {
                child = new XElement(segment);
                current.Add(child);
            }

            current = child;
        }

        return current;
    }

    private static string[] SplitPath(string xpath)
    {
        ArgumentException.ThrowIfNullOrEmpty(xpath);
        string[] segments = xpath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            throw new ArgumentException(
                "A setting path needs at least one element name.",
                nameof(xpath));
        return segments;
    }

    private static string Serialize(XDocument document)
    {
        var builder = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            // The original's document carried no declaration unless the file it
            // loaded had one; keeping that avoids churning a user's file.
            OmitXmlDeclaration = document.Declaration is null,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (XmlWriter writer = XmlWriter.Create(builder, settings))
            document.Save(writer);

        return builder.ToString();
    }
}
