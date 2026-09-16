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
    private readonly HashSet<string> _dirtyPaths = new(StringComparer.Ordinal);
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
        _dirtyPaths.Clear();
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
        MarkDirty(xpath);
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
        MarkDirty(xpath);
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

        MarkDirty(xpath);
    }

    /// <summary>The element at <paramref name="xpath"/>, optionally created.</summary>
    public XElement? GetNode(string xpath, bool createIfMissing = false)
        => createIfMissing ? CreateElement(xpath) : FindElement(xpath);

    /// <summary>
    /// Removes the node at <paramref name="xpath"/>, if present, and records
    /// the path as dirty so the removal survives the next <see cref="Flush"/>.
    /// </summary>
    public void RemoveNode(string xpath)
    {
        FindElement(xpath)?.Remove();
        MarkDirty(xpath);
    }

    /// <summary>
    /// Records that the subtree at <paramref name="xpath"/> changed and needs
    /// a write. <see cref="Flush"/> re-reads storage and grafts only the
    /// recorded paths onto it, so a caller that mutates an
    /// <see cref="XElement"/> obtained from <see cref="GetNode"/> directly
    /// (rather than through one of the setters above) must call this with
    /// that same path afterwards.
    /// </summary>
    public void MarkDirty(string xpath)
    {
        ArgumentException.ThrowIfNullOrEmpty(xpath);
        _dirty = true;
        _secondsSinceChange = 0d;
        _dirtyPaths.Add(xpath);
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

    /// <summary>
    /// Writes the document now if anything is pending.
    /// </summary>
    /// <remarks>
    /// Reloads storage first and merges the reloaded content into the
    /// existing <see cref="_document"/> — never the other way around — so an
    /// <see cref="XElement"/> a caller is holding (from <see cref="GetNode"/>,
    /// typically) stays attached to the live document across a flush. Only
    /// the subtrees this instance did NOT touch since the last flush are
    /// synced from the reloaded copy; every path in <see cref="_dirtyPaths"/>
    /// is left exactly as this instance already has it, so a concurrent write
    /// from another <see cref="SettingsFile"/> instance over the same storage
    /// (two plugin sessions, or a settings-panel session and a headless bot)
    /// is picked up everywhere except where the two disagree, and this
    /// instance's own pending changes always win there.
    /// </remarks>
    public void Flush()
    {
        if (!_dirty)
            return;

        // Storage being down does not clear _dirty or _dirtyPaths: the next
        // Tick() (or an explicit Flush()) retries once it comes back,
        // instead of silently losing the pending write.
        if (!_storage.IsAvailable)
            return;

        if (_dirtyPaths.Count > 0)
        {
            XDocument fresh = LoadDocument();
            XElement? freshRoot = fresh.Root;
            if (freshRoot is not null)
                MergeNonDirty(Root, freshRoot, string.Empty);
        }

        // _dirty and _dirtyPaths are cleared only once WriteText has actually
        // returned — a throw here (e.g. a full disk) leaves this instance
        // still marked dirty for the next attempt, rather than reporting a
        // save that never happened.
        _storage.WriteText(StorageKey, Serialize(_document));
        SaveCount++;

        _dirty = false;
        _secondsSinceChange = 0d;
        _dirtyPaths.Clear();
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

    private XElement? FindElement(string xpath) => FindElementIn(Root, xpath);

    private XElement CreateElement(string xpath) => CreateElementIn(Root, xpath);

    private static XElement? FindElementIn(XElement root, string xpath)
    {
        XElement? current = root;
        foreach (string segment in SplitPath(xpath))
        {
            current = current.Element(segment);
            if (current is null)
                return null;
        }

        return current;
    }

    private static XElement CreateElementIn(XElement root, string xpath)
    {
        XElement current = root;
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

    /// <summary>
    /// Merges <paramref name="source"/> (freshly reloaded from storage) into
    /// <paramref name="destination"/> (the live, possibly-referenced
    /// <see cref="_document"/> subtree at <paramref name="currentPath"/>),
    /// leaving every path this instance still owes a write for untouched.
    /// </summary>
    /// <remarks>
    /// A path in <see cref="_dirtyPaths"/> is a whole-subtree claim: whichever
    /// setter recorded it (<see cref="PutSetting{T}"/>,
    /// <see cref="SetNodeChildren(string, string, IReadOnlyList{string})"/>,
    /// <see cref="RemoveNode"/>, or a direct <see cref="MarkDirty"/> after
    /// mutating a <see cref="GetNode"/> result) always rewrites that node's
    /// entire children, so same-named siblings never get individually dirtied
    /// — only their shared parent path is. That means an exact-path match can
    /// stop the recursion outright and only an ancestor of a dirty path needs
    /// to recurse into its children instead of a wholesale sync.
    /// </remarks>
    private void MergeNonDirty(XElement destination, XElement source, string currentPath)
    {
        if (_dirtyPaths.Contains(currentPath))
            return;

        if (!HasDirtyDescendant(currentPath))
        {
            SyncChildrenWholesale(destination, source);
            return;
        }

        // An ancestor of a dirty path: recurse per distinctly-named child so
        // the dirty branch is skipped while its siblings still pick up the
        // reloaded content.
        foreach (string name in DistinctChildNames(destination, source))
        {
            string childPath = currentPath.Length == 0 ? name : currentPath + "/" + name;
            XElement? sourceChild = source.Element(name);

            if (_dirtyPaths.Contains(childPath))
                continue;

            if (!HasDirtyDescendant(childPath))
            {
                XElement? destinationChild = destination.Element(name);
                if (sourceChild is null)
                {
                    destinationChild?.Remove();
                    continue;
                }

                if (destinationChild is not null)
                {
                    // Merge in place rather than clone-and-replace, so a
                    // caller's own reference into this untouched child (or
                    // one of ITS descendants, via GetNode) survives the
                    // merge — see SyncChildrenWholesale's remarks.
                    MergeElementInPlace(destinationChild, sourceChild);
                }
                else
                {
                    destination.Add(new XElement(sourceChild));
                }

                continue;
            }

            // This child is itself an ancestor of a (deeper) dirty path: keep
            // its element identity and recurse into it, creating it locally
            // first if only the reloaded copy has it so far.
            XElement destinationGrandchild = destination.Element(name)
                ?? AddNewChild(destination, name);
            if (sourceChild is not null)
                MergeNonDirty(destinationGrandchild, sourceChild, childPath);
        }
    }

    /// <summary>
    /// True when some dirty path lies strictly beneath <paramref name="path"/>
    /// (every dirty path counts as beneath the root, since none is empty).
    /// </summary>
    private bool HasDirtyDescendant(string path)
    {
        if (path.Length == 0)
            return _dirtyPaths.Count > 0;

        string prefix = path + "/";
        foreach (string dirtyPath in _dirtyPaths)
        {
            if (dirtyPath.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static XElement AddNewChild(XElement parent, string name)
    {
        var child = new XElement(name);
        parent.Add(child);
        return child;
    }

    private static IEnumerable<string> DistinctChildNames(XElement a, XElement b)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement child in a.Elements())
        {
            if (seen.Add(child.Name.LocalName))
                yield return child.Name.LocalName;
        }

        foreach (XElement child in b.Elements())
        {
            if (seen.Add(child.Name.LocalName))
                yield return child.Name.LocalName;
        }
    }

    /// <summary>
    /// Syncs <paramref name="destination"/>'s children and attributes to
    /// match <paramref name="source"/>, called only when nothing under this
    /// subtree is dirty.
    /// </summary>
    /// <remarks>
    /// The real guarantee: any destination child whose element name has a
    /// same-named counterpart in source keeps its own <see cref="XElement"/>
    /// identity (recursively) rather than being replaced by a clone. That
    /// matters because <see cref="GetNode"/> can hand a caller a reference
    /// into this document — a reload/merge must not silently swap that
    /// reference out from under them for a subtree that did not actually
    /// change. It is NOT a guarantee about which of several same-named
    /// siblings pairs with which (they pair off in document order, first to
    /// first), and it does not preserve identity for a child whose name has
    /// no counterpart in <paramref name="source"/> — that child is dropped.
    /// Attributes are added, updated, and removed to match source exactly.
    /// </remarks>
    private static void SyncChildrenWholesale(XElement destination, XElement source)
    {
        if (XNode.DeepEquals(destination, source))
            return;

        var byName = destination.Elements()
            .GroupBy(static child => child.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => new Queue<XElement>(group),
                StringComparer.Ordinal);

        var ordered = new List<XElement>();
        foreach (XElement sourceChild in source.Elements())
        {
            XElement target;
            if (byName.TryGetValue(sourceChild.Name.LocalName, out Queue<XElement>? matches)
                && matches.Count > 0)
            {
                target = matches.Dequeue();
                MergeElementInPlace(target, sourceChild);
            }
            else
            {
                target = new XElement(sourceChild);
            }

            ordered.Add(target);
        }

        // RemoveNodes() only unparents the matched instances above — it does
        // not destroy them, so a caller's own reference into one keeps
        // working — then they (and any newly cloned additions) are re-added
        // in source order. Anything left in `byName` (a destination child
        // whose name/occurrence had no counterpart in source) is simply not
        // re-added.
        destination.RemoveNodes();
        foreach (XElement child in ordered)
            destination.Add(child);

        SyncAttributes(destination, source);
    }

    /// <summary>
    /// Merges <paramref name="source"/> INTO <paramref name="destination"/>
    /// in place — children (recursively, via <see cref="SyncChildrenWholesale"/>),
    /// attributes, and (whenever the reloaded <paramref name="source"/> is a
    /// leaf) the text value — instead of cloning <paramref name="source"/>
    /// over it, so <paramref name="destination"/>'s own <see cref="XElement"/>
    /// identity is preserved for whoever is holding a reference to it.
    /// </summary>
    /// <remarks>
    /// The leaf-value copy is keyed on <c>source.HasElements</c> alone —
    /// the reloaded copy is authoritative for whether this node is a leaf or
    /// a container now — rather than also requiring
    /// <c>destination.HasElements</c> to already agree. The two happen to
    /// always agree by the time this line runs (<see cref="SyncChildrenWholesale"/>
    /// just finished making destination's children match source's), but
    /// deriving the leaf/container decision from BOTH sides was redundant at
    /// best and a trap for a future edit at worst: source is the fact, not a
    /// side effect worth re-deriving.
    /// </remarks>
    private static void MergeElementInPlace(XElement destination, XElement source)
    {
        SyncChildrenWholesale(destination, source);
        SyncAttributes(destination, source);
        if (!source.HasElements)
            destination.Value = source.Value;
    }

    private static void SyncAttributes(XElement destination, XElement source)
    {
        var sourceNames = new HashSet<XName>();
        foreach (XAttribute attribute in source.Attributes())
        {
            destination.SetAttributeValue(attribute.Name, attribute.Value);
            sourceNames.Add(attribute.Name);
        }

        foreach (XAttribute stale in destination.Attributes()
                     .Where(attribute => !sourceNames.Contains(attribute.Name))
                     .ToList())
        {
            stale.Remove();
        }
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
