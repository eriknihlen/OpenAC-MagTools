using System.Reflection;
using System.Xml.Linq;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Ui;
using ChatLoggerFeature = OpenAC.MagTools.Loggers.Chat.ChatLogger;

namespace OpenAC.MagTools.Tests;

/// <summary>
/// Proves every <c>{Binding}</c> in both markup files resolves to a public
/// property of the right shape on its binding object, and that the tab rows are
/// the ones the port promised. This is the cheap way to catch the whole class
/// of binding typos, which otherwise only show up as a panel that refuses to
/// load at run time.
/// </summary>
public sealed class MarkupContractTests
{
    private static readonly string[] InteractiveElementNames =
    [
        "tab", "button", "toggle", "slider", "field", "menu", "list", "column",
    ];

    public static TheoryData<string, Type> MarkupFiles => new()
    {
        { "magtools.xml", typeof(MainViewModel) },
        { "magtools-hud.xml", typeof(HudViewModel) },
    };

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void EveryBindingResolvesToAPublicProperty(string file, Type bindingType)
    {
        XElement root = LoadRoot(file);
        IReadOnlyDictionary<string, PropertyInfo> byName = PropertiesOf(bindingType);

        foreach (XAttribute attribute in root.DescendantsAndSelf().Attributes())
        {
            string value = attribute.Value;
            if (!value.Contains('{', StringComparison.Ordinal))
                continue;

            Assert.Matches("^\\{[^{}]+\\}$", value);
            string name = value[1..^1];
            Assert.True(
                byName.ContainsKey(name),
                $"{file}: binding {value} on <{attribute.Parent?.Name}> has no "
                + $"public {bindingType.Name} property.");
        }
    }

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void EveryBindingMatchesTheRetainedUiDelegateShape(
        string file,
        Type bindingType)
    {
        XElement root = LoadRoot(file);
        IReadOnlyDictionary<string, PropertyInfo> byName = PropertiesOf(bindingType);

        foreach (XElement element in root.DescendantsAndSelf())
            AssertElementBindings(file, element, byName);
    }

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void EveryInteractiveControlDeclaresAHandler(string file, Type bindingType)
    {
        _ = bindingType;
        XElement root = LoadRoot(file);
        HashSet<string> interactive = new(InteractiveElementNames, StringComparer.Ordinal);

        XElement[] controls = [.. root.Descendants()
            .Where(element => interactive.Contains(element.Name.LocalName))];
        Assert.NotEmpty(controls);

        foreach (XElement control in controls)
        {
            if (control.Name.LocalName == "column"
                && (string?)control.Attribute("type") == "text")
                continue;

            XAttribute? handler = control.Attribute("onclick")
                ?? control.Attribute("onchange")
                ?? control.Attribute("onsubmit");
            Assert.True(
                handler is not null,
                $"{file}: <{control.Name.LocalName}> declares no handler.");
        }
    }

    [Fact]
    public void TheRootPanelVisibilityIsABinding()
    {
        foreach (string file in new[] { "magtools.xml", "magtools-hud.xml" })
        {
            XElement root = LoadRoot(file);
            Assert.Equal("panel", root.Name.LocalName);
            string? visible = (string?)root.Attribute("visible");
            Assert.NotNull(visible);
            Assert.Matches("^\\{[^{}]+\\}$", visible);
        }
    }

    [Fact]
    public void TopLevelTabsAreTheOriginalFourNotebooks()
    {
        XElement root = LoadRoot("magtools.xml");
        string[] topTabs = [.. root.Elements("tab")
            .Where(tab => (string?)tab.Attribute("y") == "22")
            .Select(tab => (string?)tab.Attribute("text") ?? string.Empty)];

        Assert.Equal(["Trackers", "Loggers", "Tools", "Misc"], topTabs);
    }

    [Fact]
    public void SecondRowTabsFollowTheOriginalTabTree()
    {
        XElement root = LoadRoot("magtools.xml");

        Assert.Equal(
            ["Mana", "Combat", "Corpse", "Player", "Inv. Items"],
            SecondRow(root, "TrackersTabsVisible"));
        Assert.Equal(
            ["Chat Group 1", "Chat Group 2", "Options"],
            SecondRow(root, "LoggersTabsVisible"));
        Assert.Equal(
            ["Inventory", "Tinkering", "Character", "Server"],
            SecondRow(root, "ToolsTabsVisible"));
        Assert.Equal(
            ["Options", "Filters", "About"],
            SecondRow(root, "MiscTabsVisible"));
    }

    [Fact]
    public void ThirdRowTabsCoverTheInnerNotebooks()
    {
        XElement root = LoadRoot("magtools.xml");

        Assert.Equal(
            ["Current Session Stats", "Persistent Stats", "Options"],
            SecondRow(root, "CombatTabsVisible"));
        Assert.Equal(
            ["Tracked Corpses", "Options"],
            SecondRow(root, "CorpseTabsVisible"));
        Assert.Equal(
            ["Tracked Players", "Options"],
            SecondRow(root, "PlayerTabsVisible"));
    }

    [Fact]
    public void EveryPageGroupHasAVisibilityBinding()
    {
        XElement root = LoadRoot("magtools.xml");
        XElement[] pages = [.. root.Elements("group")];

        // One group per page in the original tab tree, minus Misc -> Client,
        // whose every control was a Win32 hack.
        Assert.Equal(19, pages.Length);
        foreach (XElement page in pages)
        {
            string? visible = (string?)page.Attribute("visible");
            Assert.NotNull(visible);
            Assert.Matches("^\\{[^{}]+\\}$", visible);
        }
    }

    [Fact]
    public void OptionsAndFilterRowsComeFromTheSettingDescriptions()
    {
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var model = new MainViewModel(settings, new ChatLoggerFeature(host, settings));

        Assert.Equal(24, model.OptionCaptions.Count);
        Assert.Equal(model.OptionCaptions.Count, model.OptionChecks.Count);
        Assert.Contains("Show Item Info On Ident", model.OptionCaptions);
        Assert.Contains("Open Main Pack On Login", model.OptionCaptions);

        Assert.Equal(29, model.FilterCaptions.Count);
        Assert.Equal(model.FilterCaptions.Count, model.FilterChecks.Count);
        Assert.Equal("Attack Evades", model.FilterCaptions[0]);
        Assert.Equal("Status Text: All", model.FilterCaptions[^1]);
    }

    [Fact]
    public void TheMainWindowIsAvailableBeforeLogin()
    {
        // The root <panel visible="..."> binding is a host-side availability
        // gate (RetailUiRuntime.ShouldBeVisible), not the shown/hidden state,
        // and the Options page has to be reachable before a session exists.
        var host = new FakeHost();
        var settings = new SettingsManager(new SettingsFile(host.Storage));
        var model = new MainViewModel(settings, new ChatLoggerFeature(host, settings));

        Assert.True(model.WindowAvailable);
    }

    [Fact]
    public void TheHudIsGatedOnAutomationAvailability()
    {
        var host = new FakeHost();
        var model = new HudViewModel(host);

        Assert.False(model.WindowAvailable);

        host.Automation.IsAvailable = true;
        Assert.True(model.WindowAvailable);
    }

    [Fact]
    public void HudRowsAreTheOriginalFourteenInOrder()
    {
        var model = new HudViewModel(new FakeHost());

        Assert.Equal(14, model.Names.Count);
        Assert.Equal("Mana", model.Names[0]);
        Assert.Equal("ID Queue", model.Names[^1]);
        Assert.Equal(model.Names.Count, model.Values.Count);
        Assert.All(model.Values, static value => Assert.Equal(string.Empty, value));
    }

    private static string[] SecondRow(XElement root, string visibleBinding)
        => [.. root.Elements("tab")
            .Where(tab => (string?)tab.Attribute("visible") == "{" + visibleBinding + "}")
            .Select(tab => (string?)tab.Attribute("text") ?? string.Empty)];

    private static void AssertElementBindings(
        string file,
        XElement element,
        IReadOnlyDictionary<string, PropertyInfo> byName)
    {
        if (element.Name.LocalName == "column")
        {
            AssertBindingType(file, element, "onchange", typeof(Action<int>), byName);
            AssertBindingType(file, element, "onclick", typeof(Action<int>), byName);
            AssertListBinding(file, element, "items", typeof(string), byName);

            string? columnType = (string?)element.Attribute("type");
            switch (columnType)
            {
                case "check":
                    AssertListBinding(file, element, "values", typeof(bool), byName);
                    break;
                case "icon":
                    AssertIconListBinding(file, element, "values", byName);
                    break;
                default:
                    AssertListBinding(file, element, "values", null, byName);
                    break;
            }

            return;
        }

        AssertBindingType(file, element, "onclick", typeof(Action), byName);
        AssertBindingType(file, element, "onsubmit", typeof(Action<string>), byName);
        AssertBindingType(file, element, "visible", typeof(bool), byName);
        AssertBindingType(file, element, "enabled", typeof(bool), byName);
        AssertBindingType(file, element, "checked", typeof(bool), byName);
        AssertBindingType(file, element, "selected", SelectedType(element), byName);

        Type? changeType = element.Name.LocalName switch
        {
            "field" or "menu" => typeof(Action<string>),
            "slider" => typeof(Action<float>),
            "list" => typeof(Action<int>),
            _ => null,
        };
        if (changeType is not null)
            AssertBindingType(file, element, "onchange", changeType, byName);

        if (element.Name.LocalName is "list" or "menu")
            AssertListBinding(file, element, "items", typeof(string), byName);
    }

    private static Type? SelectedType(XElement element) =>
        element.Name.LocalName switch
        {
            "tab" => typeof(bool),
            "list" => typeof(int),
            _ => null,
        };

    private static void AssertBindingType(
        string file,
        XElement element,
        string attributeName,
        Type? expected,
        IReadOnlyDictionary<string, PropertyInfo> byName)
    {
        if (expected is null)
            return;
        string? value = (string?)element.Attribute(attributeName);
        if (value is null || !value.StartsWith('{'))
            return;

        PropertyInfo property = byName[value[1..^1]];
        Assert.True(
            expected.IsAssignableFrom(property.PropertyType),
            $"{file}: <{element.Name.LocalName} {attributeName}=\"{value}\"> is "
            + $"{property.PropertyType.Name}, expected {expected.Name}.");
    }

    private static void AssertListBinding(
        string file,
        XElement element,
        string attributeName,
        Type? elementType,
        IReadOnlyDictionary<string, PropertyInfo> byName)
    {
        string? value = (string?)element.Attribute(attributeName);
        if (value is null || !value.StartsWith('{'))
            return;

        PropertyInfo property = byName[value[1..^1]];
        Type type = property.PropertyType;
        Assert.True(
            typeof(System.Collections.IEnumerable).IsAssignableFrom(type),
            $"{file}: <{element.Name.LocalName} {attributeName}=\"{value}\"> is "
            + $"{type.Name}, expected a list.");

        if (elementType is null)
            return;
        Type? bound = type.IsGenericType ? type.GetGenericArguments()[0] : null;
        Assert.True(
            bound == elementType,
            $"{file}: <{element.Name.LocalName} {attributeName}=\"{value}\"> holds "
            + $"{bound?.Name ?? "?"}, expected {elementType.Name}.");
    }

    private static XElement LoadRoot(string file)
    {
        XDocument document = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, file));
        return Assert.IsType<XElement>(document.Root);
    }

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void EveryListColumnHasASaneWidth(string file, Type bindingType)
    {
        _ = bindingType;
        XElement root = LoadRoot(file);

        foreach (XElement list in root.Descendants("list"))
        {
            XElement[] columns = [.. list.Elements("column")];
            if (columns.Length == 0)
                continue;

            for (int index = 0; index < columns.Length; index++)
            {
                string? width = (string?)columns[index].Attribute("width");
                Assert.True(
                    width is not null,
                    $"{file}: <column> {index} has no width.");

                bool isLast = index == columns.Length - 1;
                bool isStar = width == "*";
                bool isPositiveNumber =
                    double.TryParse(
                        width,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double parsed)
                    && parsed > 0d;

                Assert.True(
                    isStar || isPositiveNumber,
                    $"{file}: <column width=\"{width}\"> at index {index} is "
                    + "neither a positive number nor \"*\".");

                if (!isLast)
                {
                    Assert.True(
                        isStar || isPositiveNumber,
                        $"{file}: non-last <column width=\"{width}\"> at index "
                        + $"{index} must be a positive number or \"*\".");
                }
            }
        }
    }

    private static void AssertIconListBinding(
        string file,
        XElement element,
        string attributeName,
        IReadOnlyDictionary<string, PropertyInfo> byName)
    {
        string? value = (string?)element.Attribute(attributeName);
        if (value is null || !value.StartsWith('{'))
            return;

        PropertyInfo property = byName[value[1..^1]];
        Type type = property.PropertyType;
        Assert.True(
            typeof(System.Collections.IEnumerable).IsAssignableFrom(type),
            $"{file}: <{element.Name.LocalName} {attributeName}=\"{value}\"> is "
            + $"{type.Name}, expected a list.");

        Type? bound = type.IsGenericType ? type.GetGenericArguments()[0] : null;
        Assert.True(
            bound == typeof(uint) || bound == typeof(int),
            $"{file}: <{element.Name.LocalName} {attributeName}=\"{value}\"> holds "
            + $"{bound?.Name ?? "?"}, expected uint or int.");
    }

    private static IReadOnlyDictionary<string, PropertyInfo> PropertiesOf(Type type)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(static property => property.Name, StringComparer.Ordinal);
}
