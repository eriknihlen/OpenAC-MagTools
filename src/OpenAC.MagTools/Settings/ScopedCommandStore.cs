using System.Globalization;
using System.Xml.Linq;

namespace OpenAC.MagTools.Settings;

/// <summary>
/// A command the plugin re-issues on a fixed interval, offset from midnight.
/// </summary>
public readonly record struct PeriodicCommand(
    string Command,
    TimeSpan Interval,
    TimeSpan OffsetFromMidnight);

/// <summary>
/// The three command lists (on login, on login complete, periodic) stored under
/// one scope path — either an account/server/character scope or a server scope.
/// </summary>
public sealed class ScopedCommandStore
{
    private const string OnLoginNode = "OnLoginCommands";
    private const string OnLoginCompleteNode = "OnLoginCompleteCommands";
    private const string PeriodicNode = "PeriodicCommands";
    private const string CommandChild = "Command";
    private const string PeriodicChild = "PeriodicCommand";

    private readonly SettingsFile _file;

    public ScopedCommandStore(SettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
    }

    public IReadOnlyList<string> GetOnLoginCommands(string scopePath)
        => _file.GetChildrenInnerTexts(Path(scopePath, OnLoginNode));

    public void SetOnLoginCommands(string scopePath, IReadOnlyList<string> commands)
        => _file.SetNodeChildren(Path(scopePath, OnLoginNode), CommandChild, commands);

    public IReadOnlyList<string> GetOnLoginCompleteCommands(string scopePath)
        => _file.GetChildrenInnerTexts(Path(scopePath, OnLoginCompleteNode));

    public void SetOnLoginCompleteCommands(
        string scopePath,
        IReadOnlyList<string> commands)
        => _file.SetNodeChildren(
            Path(scopePath, OnLoginCompleteNode),
            CommandChild,
            commands);

    public IReadOnlyList<PeriodicCommand> GetPeriodicCommands(string scopePath)
    {
        XElement? node = _file.GetNode(Path(scopePath, PeriodicNode));
        if (node is null)
            return [];

        var commands = new List<PeriodicCommand>();
        foreach (XElement child in node.Elements())
        {
            commands.Add(new PeriodicCommand(
                child.Value,
                TimeSpan.FromMinutes(ReadMinutes(child, "interval")),
                TimeSpan.FromMinutes(ReadMinutes(child, "offset"))));
        }

        return commands;
    }

    public void SetPeriodicCommands(
        string scopePath,
        IReadOnlyList<PeriodicCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var rows = new List<IReadOnlyDictionary<string, string>>(commands.Count);
        var texts = new List<string>(commands.Count);
        foreach (PeriodicCommand command in commands)
        {
            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["offset"] = command.OffsetFromMidnight.TotalMinutes
                    .ToString(CultureInfo.InvariantCulture),
                ["interval"] = command.Interval.TotalMinutes
                    .ToString(CultureInfo.InvariantCulture),
            });
            texts.Add(command.Command);
        }

        string path = Path(scopePath, PeriodicNode);
        _file.SetNodeChildren(path, PeriodicChild, rows);

        XElement? node = _file.GetNode(path);
        if (node is null)
            return;

        // The attribute form writes the attributes; the inner text is the
        // command itself, so fill it in on the same elements.
        int index = 0;
        foreach (XElement child in node.Elements())
        {
            if (index >= texts.Count)
                break;
            child.Value = texts[index];
            index++;
        }

        _file.MarkDirty();
    }

    private static double ReadMinutes(XElement element, string attributeName)
    {
        string? text = (string?)element.Attribute(attributeName);
        return double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double minutes)
            ? minutes
            : 0d;
    }

    private static string Path(string scopePath, string node)
    {
        ArgumentException.ThrowIfNullOrEmpty(scopePath);
        return scopePath + "/" + node;
    }
}
