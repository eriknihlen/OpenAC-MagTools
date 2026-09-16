using System.Globalization;
using System.Text;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Commands;

/// <summary>
/// The <c>/mt</c> console. Argument shapes, search order and message wording
/// follow the original; the actions behind them are OpenAC automation calls.
/// </summary>
/// <remarks>
/// Commands fall into three groups. Most run natively. A few wait on a plugin
/// API that does not exist yet and say so. The rest drove the retail client by
/// synthetic Win32 input and have no meaning here; they are listed in
/// <see cref="NotApplicable"/> and report that plainly instead of failing
/// silently.
/// </remarks>
public sealed class MtCommandRouter
{
    /// <summary>
    /// Commands that were Win32 input hacks against the retail client's own
    /// message loop. There is nothing to port; OpenAC has native equivalents
    /// for the few that mattered (facing, fellowship creation).
    /// </summary>
    public static readonly IReadOnlyList<string> NotApplicable =
    [
        "send",
        "click",
        "jump",
        "sjump",
        "jumpw",
        "jumpz",
        "jumpx",
        "jumpc",
        "movement",
        "quit",
        "exit",
        "get xy",
        "client minimize",
    ];

    private const string SpellDumpStorageKey = "mt spelldump.txt";

    private static readonly string[] CombatStateNames =
        ["magic", "melee", "missile", "peace"];

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly SettingsManager _settings;
    private readonly InventoryPacker? _inventoryPacker;
    private readonly Dictionary<string, string> _remembered =
        new(StringComparer.OrdinalIgnoreCase);

    public MtCommandRouter(
        IPluginHost host,
        ChatOutput chat,
        SettingsManager settings,
        InventoryPacker? inventoryPacker = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _chat = chat;
        _settings = settings;
        _inventoryPacker = inventoryPacker;
    }

    /// <summary>The registry entry point.</summary>
    public void Execute(PluginCommand command) => Execute(command.Arguments);

    /// <summary>
    /// Runs one command. The return value says whether the plugin recognised
    /// it, which is what the original used to decide whether to eat the line.
    /// </summary>
    public bool Execute(string? arguments)
    {
        string text = (arguments ?? string.Empty).Trim();
        string lower = text.ToLowerInvariant();

        if (lower.Length == 0)
        {
            _chat.Write("Usage: /mt <command>. Try /mt opt list.");
            return false;
        }

        foreach (string notApplicable in NotApplicable)
        {
            if (Matches(lower, notApplicable))
            {
                _chat.Write(notApplicable + " is not applicable in OpenAC");
                return true;
            }
        }

        if (Matches(lower, "trade"))
            return Trade(Remainder(lower, "trade"));

        if (Matches(lower, "vendor"))
            return Vendor(Remainder(lower, "vendor"), Remainder(text, "vendor"));

        if (lower == "test")
            return true;

        if (lower is "logoff" or "logout")
            return _host.Automation.Login.Logout();

        if (Matches(lower, "face"))
            return Face(Remainder(lower, "face"));

        if (Matches(lower, "fellow"))
            return Fellowship(Remainder(lower, "fellow"));

        if (Matches(lower, "castp"))
            return Cast(Remainder(lower, "castp"), partial: true);
        if (Matches(lower, "cast"))
            return Cast(Remainder(lower, "cast"), partial: false);

        if (Matches(lower, "selectp"))
            return Select(Remainder(lower, "selectp"), partial: true);
        if (Matches(lower, "select"))
            return Select(Remainder(lower, "select"), partial: false);

        if (Matches(lower, "useip"))
            return Use(Remainder(lower, "useip"), UseScope.Inventory, partial: true);
        if (Matches(lower, "usei"))
            return Use(Remainder(lower, "usei"), UseScope.Inventory, partial: false);
        if (Matches(lower, "uselp"))
            return Use(Remainder(lower, "uselp"), UseScope.Landscape, partial: true);
        if (Matches(lower, "usel"))
            return Use(Remainder(lower, "usel"), UseScope.Landscape, partial: false);
        if (Matches(lower, "usep"))
            return Use(Remainder(lower, "usep"), UseScope.Anywhere, partial: true);
        if (Matches(lower, "use"))
            return Use(Remainder(lower, "use"), UseScope.Anywhere, partial: false);

        if (Matches(lower, "equipp"))
            return Equip(Remainder(lower, "equipp"), partial: true);
        if (Matches(lower, "equip"))
            return Equip(Remainder(lower, "equip"), partial: false);

        if (Matches(lower, "dequipp"))
            return Dequip(Remainder(lower, "dequipp"), partial: true);
        if (Matches(lower, "dequip"))
            return Dequip(Remainder(lower, "dequip"), partial: false);

        if (Matches(lower, "givep"))
            return Give(Remainder(lower, "givep"), partial: true);
        if (Matches(lower, "give"))
            return Give(Remainder(lower, "give"), partial: false);

        if (Matches(lower, "lootp"))
            return Loot(Remainder(lower, "lootp"), partial: true);
        if (Matches(lower, "loot"))
            return Loot(Remainder(lower, "loot"), partial: false);

        if (Matches(lower, "dropp"))
            return Drop(Remainder(lower, "dropp"), partial: true);
        if (Matches(lower, "drop"))
            return Drop(Remainder(lower, "drop"), partial: false);

        if (Matches(lower, "combatstate"))
            return CombatState(Remainder(lower, "combatstate"));

        if (Matches(lower, "attack_melee"))
            return AttackMelee(Remainder(lower, "attack_melee"));

        if (lower == "autopack")
        {
            _inventoryPacker?.Start();
            return true;
        }

        if (lower == "dumpspells")
            return DumpSpells();

        if (Matches(lower, "opt"))
            return Option(Remainder(lower, "opt"), Remainder(text, "opt"));

        _chat.Write("Unknown command: " + lower);
        return false;
    }

    // ---- individual commands -------------------------------------------------

    private bool Face(string argument)
    {
        if (!float.TryParse(
                argument,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float heading))
        {
            _chat.Write("Usage: /mt face <heading>");
            return false;
        }

        return _host.Automation.Navigation.FaceHeading(heading)
            == PluginNavigationCommandStatus.Accepted;
    }

    private bool Fellowship(string argument)
    {
        IFellowshipAutomation fellowship = _host.Automation.Fellowship;

        if (Matches(argument, "create"))
        {
            string createName = Remainder(argument, "create");
            if (createName.Length == 0)
            {
                _chat.Write("Usage: /mt fellow create <name>");
                return false;
            }

            return Report(
                fellowship.Create(createName, shareExperience: true),
                "Fellowship created.");
        }

        if (argument == "open")
            return Report(fellowship.SetOpen(true), "Fellowship opened.");
        if (argument == "close")
            return Report(fellowship.SetOpen(false), "Fellowship closed.");
        if (argument == "disband")
            return Report(fellowship.Quit(disband: true), "Fellowship disbanded.");
        if (argument == "quit")
            return Report(fellowship.Quit(disband: false), "Left the fellowship.");

        if (Matches(argument, "recruit"))
        {
            string name = Remainder(argument, "recruit");
            uint id = FindClosestObjectNamed(name, partial: false);
            if (id == 0)
            {
                _chat.Write("No player found named: " + name);
                return false;
            }

            return Report(fellowship.Recruit(id), "Invited " + name + " to the fellowship.");
        }

        _chat.Write(
            "Usage: /mt fellow create <name>|open|close|disband|quit|recruit <player>");
        return false;

        // Every fellowship action must print SOMETHING -- previously a
        // Rejected/Unavailable result (the server refusing the request, or
        // no fellowship API available at all) was swallowed silently and
        // looked identical to the command never running. See defect #4 in
        // docs/live-results.md.
        bool Report(PluginFellowshipCommandResult result, string confirmation)
        {
            if (result.Accepted)
            {
                _chat.Write(confirmation);
                return true;
            }

            _chat.Write("Fellowship request refused: " + result.Status);
            return false;
        }
    }

    private bool Cast(string argument, bool partial)
    {
        (string spellText, string targetName) = SplitOn(argument, " on ");
        if (spellText.Length == 0)
        {
            _chat.Write("Usage: /mt cast <spell> [on <target>]");
            return false;
        }

        if (!TryResolveSpell(spellText, partial, out uint spellId))
        {
            _chat.Write("No spell named: " + spellText);
            return false;
        }

        if (targetName.Length == 0)
            return ReportCastRequest(_host.Automation.Magic.RequestCast(spellId));

        uint targetId = FindClosestObjectNamed(targetName, partial);
        if (targetId == 0)
        {
            _chat.Write("No target found named: " + targetName);
            return false;
        }

        return ReportCastRequest(_host.Automation.Magic.RequestCast(spellId, targetId));
    }

    /// <summary>
    /// Every cast outcome must print something -- a silent <c>/mt cast</c> is
    /// indistinguishable from one that never ran at all. Only
    /// <see cref="PluginCastRequestResult.Sent"/> stays quiet in chat (the
    /// server's own reaction is the confirmation); every refusal prints its
    /// status.
    /// </summary>
    private bool ReportCastRequest(PluginCastRequestResult result)
    {
        if (result == PluginCastRequestResult.Sent)
            return true;

        _chat.Write("Cast refused: " + result);
        return false;
    }

    private bool Select(string name, bool partial)
    {
        uint id = FindIdForName(
            name,
            searchInventory: true,
            searchOpenContainer: true,
            searchEnvironment: true,
            partial);
        if (id == 0)
        {
            _chat.Write("Nothing found named: " + name);
            return false;
        }

        return _host.Selection.Select(id);
    }

    private enum UseScope
    {
        Anywhere,
        Inventory,
        Landscape,
    }

    private bool Use(string argument, UseScope scope, bool partial)
    {
        (string itemName, string targetName) = SplitOn(argument, " on ");
        if (itemName.Length == 0)
        {
            _chat.Write("Usage: /mt use <item> [on <target>]");
            return false;
        }

        if (targetName.Length > 0)
        {
            // The original resolved the "use A on B" form without keyword
            // matching at all: A is always inventory-only, and B is resolved
            // in the command's scope but is never allowed to match A itself.
            uint sourceId = FindIdForName(
                itemName,
                searchInventory: true,
                searchOpenContainer: false,
                searchEnvironment: false,
                partial);
            if (sourceId == 0)
            {
                _chat.Write("Nothing found named: " + itemName);
                return false;
            }

            uint targetId = ResolveScopedTarget(targetName, scope, partial, sourceId);
            if (targetId == 0)
            {
                _chat.Write("Nothing found named: " + targetName);
                return false;
            }

            _host.Selection.Select(targetId);
            return ReportUse(
                _host.Automation.Items.Apply(sourceId, targetId),
                itemName + " on " + targetName);
        }

        uint id = ResolveUseTarget(itemName, scope, partial);
        if (id == 0)
        {
            _chat.Write("Nothing found named: " + itemName);
            return false;
        }

        return ReportUse(_host.Automation.Items.Use(id), itemName);
    }

    /// <summary>
    /// Every <c>/mt use*</c> outcome must print SOMETHING -- previously a
    /// refused command (InvalidItem, InvalidTarget, Busy, Refused,
    /// Unavailable) resolved a real object and issued the command with no
    /// chat output at all, indistinguishable from a host that silently did
    /// nothing (see defect #4/#6 in docs/live-results.md, e.g.
    /// <c>/mt usel closestvendor</c> against a real vendor).
    /// </summary>
    private bool ReportUse(PluginItemCommandResult result, string what)
    {
        if (result.Status == PluginItemCommandStatus.Started)
        {
            _chat.Write("Using " + what + ".");
            return true;
        }

        _chat.Write("Use refused: " + result.Status);
        return false;
    }

    private uint ResolveUseTarget(string name, UseScope scope, bool partial)
    {
        uint keyword = ResolveKeyword(name);
        return keyword != 0 ? keyword : ResolveScopedTarget(name, scope, partial);
    }

    /// <summary>
    /// The scope's search set, with no keyword matching. The original never
    /// searched the open container for <c>use</c>/<c>usei</c>/<c>usel</c>; it
    /// searched only the packs (and the landscape, unless restricted to
    /// inventory).
    /// </summary>
    private uint ResolveScopedTarget(
        string name,
        UseScope scope,
        bool partial,
        uint idToSkip = 0)
    {
        return scope switch
        {
            UseScope.Inventory => FindIdForName(
                name,
                searchInventory: true,
                searchOpenContainer: false,
                searchEnvironment: false,
                partial,
                idToSkip),
            UseScope.Landscape => FindIdForName(
                name,
                searchInventory: false,
                searchOpenContainer: false,
                searchEnvironment: true,
                partial,
                idToSkip),
            _ => FindIdForName(
                name,
                searchInventory: true,
                searchOpenContainer: false,
                searchEnvironment: true,
                partial,
                idToSkip),
        };
    }

    private uint ResolveKeyword(string name) => name switch
    {
        "closestnpc" => FindClosestOfClass(PluginObjectClass.Npc),
        "closestvendor" => FindClosestOfClass(PluginObjectClass.Vendor),
        "closestportal" => FindClosestOfClass(PluginObjectClass.Portal),
        _ => 0u,
    };

    private bool Equip(string name, bool partial)
    {
        uint id = FindOwnedItem(name, partial, equipped: false);
        if (id == 0)
        {
            _chat.Write("No unequipped item found named: " + name);
            return false;
        }

        return _host.Automation.Equipment.Equip(id).Status
            == PluginEquipmentCommandStatus.Started;
    }

    private bool Dequip(string name, bool partial)
    {
        uint id = FindOwnedItem(name, partial, equipped: true);
        if (id == 0)
        {
            _chat.Write("No equipped item found named: " + name);
            return false;
        }

        // There is no Unequip on the contract; using an equipped item takes it
        // off, which is exactly what the original did.
        return Started(_host.Automation.Items.Use(id));
    }

    private bool Give(string argument, bool partial)
    {
        (string itemName, string targetName) = SplitOn(argument, " to ");
        if (itemName.Length == 0 || targetName.Length == 0)
        {
            _chat.Write("Usage: /mt give <item> to <target>");
            return false;
        }

        uint itemId = FindIdForName(
            itemName,
            searchInventory: true,
            searchOpenContainer: false,
            searchEnvironment: false,
            partial);
        uint targetId = FindClosestObjectNamed(targetName, partial);
        if (itemId == 0 || targetId == 0)
        {
            _chat.Write("Nothing found named: "
                + (itemId == 0 ? itemName : targetName));
            return false;
        }

        return Started(_host.Automation.Items.Give(itemId, targetId));
    }

    private bool Loot(string name, bool partial)
    {
        uint id = FindInOpenContainer(name, partial);
        if (id == 0)
        {
            _chat.Write("Nothing found in the open container named: " + name);
            return false;
        }

        return Started(_host.Automation.Loot.Pickup(id));
    }

    private bool Drop(string name, bool partial)
    {
        uint id = FindIdForName(
            name,
            searchInventory: true,
            searchOpenContainer: false,
            searchEnvironment: false,
            partial);
        if (id == 0)
        {
            _chat.Write("No inventory item found named: " + name);
            return false;
        }

        return Started(_host.Automation.Items.Drop(id));
    }

    private bool CombatState(string argument)
    {
        if (!CombatStateNames.Contains(argument))
        {
            _chat.Write("Usage: /mt combatstate magic|melee|missile|peace");
            return false;
        }

        PluginCombatMode mode = argument switch
        {
            "magic" => PluginCombatMode.Magic,
            "melee" => PluginCombatMode.Melee,
            "missile" => PluginCombatMode.Missile,
            _ => PluginCombatMode.Peace,
        };

        PluginCombatCommandResult result = _host.Automation.Combat.EnterMode(mode);
        return result.Status is PluginCombatCommandStatus.ModeChangeSent
            or PluginCombatCommandStatus.AlreadyReady;
    }

    private bool Trade(string argument)
    {
        ITradeAutomation trade = _host.Automation.Trade;

        if (Matches(argument, "addp"))
            return TradeAdd(Remainder(argument, "addp"), partial: true);
        if (Matches(argument, "add"))
            return TradeAdd(Remainder(argument, "add"), partial: false);

        if (argument == "accept")
            return trade.Accept().Status
                is PluginTradeCommandStatus.Sent or PluginTradeCommandStatus.AlreadyAccepted;
        if (argument == "decline")
            return trade.Decline().Status == PluginTradeCommandStatus.Sent;
        if (argument == "reset")
            return trade.Reset().Status == PluginTradeCommandStatus.Sent;
        if (argument == "end")
            return trade.End().Status == PluginTradeCommandStatus.Sent;

        _chat.Write("Usage: /mt trade add|addp <name>|accept|decline|reset|end");
        return false;
    }

    private bool TradeAdd(string name, bool partial)
    {
        uint id = FindIdForName(
            name,
            searchInventory: true,
            searchOpenContainer: false,
            searchEnvironment: false,
            partial);
        if (id == 0)
        {
            _chat.Write("No inventory item found named: " + name);
            return false;
        }

        return _host.Automation.Trade.Add(id).Status == PluginTradeCommandStatus.Sent;
    }

    /// <param name="argument">The lower-cased argument.</param>
    /// <param name="original">The argument as typed, for the item-name portion of addbuy/addsell (names are case-preserved).</param>
    private bool Vendor(string argument, string original)
    {
        IVendorAutomation vendor = _host.Automation.Vendor;

        if (Matches(argument, "addbuyp"))
            return VendorAddBuy(Remainder(original, "addbuyp"), partial: true);
        if (Matches(argument, "addbuy"))
            return VendorAddBuy(Remainder(original, "addbuy"), partial: false);

        if (Matches(argument, "addsellp"))
            return VendorAddSell(Remainder(original, "addsellp"), partial: true);
        if (Matches(argument, "addsell"))
            return VendorAddSell(Remainder(original, "addsell"), partial: false);

        if (argument == "buy")
            return vendor.BuyAll().Status == PluginVendorCommandStatus.Sent;
        if (argument == "sell")
            return vendor.SellAll().Status == PluginVendorCommandStatus.Sent;
        if (argument == "clearbuy")
            return vendor.ClearBuyList().Status == PluginVendorCommandStatus.Sent;
        if (argument == "clearsell")
            return vendor.ClearSellList().Status == PluginVendorCommandStatus.Sent;

        _chat.Write(
            "Usage: /mt vendor addbuy|addbuyp <name> [count]|addsell|addsellp <name>|buy|sell|clearbuy|clearsell");
        return false;
    }

    private bool VendorAddBuy(string argument, bool partial)
    {
        // Only when a vendor pane is actually open, exactly like the
        // original's Actions.VendorId != 0 gate.
        if (!_host.Automation.Vendor.IsOpen)
        {
            _chat.Write("No vendor is open.");
            return false;
        }

        (string name, int count) = VendorCommandArguments.ParseAddBuy(argument);
        PluginVendorItem? item = FindVendorItemByName(name, partial);
        if (item is not { } found)
        {
            _chat.Write("No vendor item found named: " + name);
            return false;
        }

        return _host.Automation.Vendor.AddToBuyList(found.TemplateObjectId, count).Status
            == PluginVendorCommandStatus.Sent;
    }

    private bool VendorAddSell(string name, bool partial)
    {
        uint id = FindIdForName(
            name,
            searchInventory: true,
            searchOpenContainer: false,
            searchEnvironment: false,
            partial);
        if (id == 0)
        {
            _chat.Write("No inventory item found named: " + name);
            return false;
        }

        return _host.Automation.Vendor.AddToSellList(id).Status == PluginVendorCommandStatus.Sent;
    }

    private PluginVendorItem? FindVendorItemByName(string name, bool partial)
    {
        foreach (bool substring in partial ? new[] { false, true } : new[] { false })
        {
            foreach (PluginVendorItem item in _host.Automation.Vendor.Items)
            {
                if (NameMatches(item.Name, name, substring))
                    return item;
            }
        }

        return null;
    }

    private bool AttackMelee(string argument)
    {
        bool closest = argument == "closest";
        ICombatAutomation combat = _host.Automation.Combat;

        if (combat.Snapshot.Mode != PluginCombatMode.Melee)
        {
            PluginCombatCommandResult mode =
                combat.EnterMode(PluginCombatMode.Melee);
            return mode.Status is PluginCombatCommandStatus.ModeChangeSent
                or PluginCombatCommandStatus.AlreadyReady;
        }

        uint target = _host.Selection.SelectedObjectId ?? 0u;
        if (closest || !IsMonster(target))
            target = FindClosestOfClass(PluginObjectClass.Monster);

        if (target == 0)
        {
            _chat.Write("No monster found to attack.");
            return false;
        }

        _host.Selection.Select(target);

        // The original fired a single Delete keypress, which the retail
        // client's own input handling turned into one complete press+release
        // attack. BeginPhysicalAttack/ReleasePhysicalAttack are OpenAC's two
        // halves of that same gesture; calling only the first leaves the
        // attack gate charging (and Busy) forever.
        PluginCombatSnapshot snapshot = combat.Snapshot;
        PluginAttackHeight height = snapshot.AttackHeight == default
            ? PluginAttackHeight.Medium
            : snapshot.AttackHeight;
        float power = snapshot.DesiredPower <= 0f ? 1f : snapshot.DesiredPower;

        PluginCombatCommandResult begin =
            combat.BeginPhysicalAttack(target, height, power);
        if (begin.Status is not (PluginCombatCommandStatus.Started
            or PluginCombatCommandStatus.AlreadyReady))
        {
            return false;
        }

        PluginCombatCommandResult release = combat.ReleasePhysicalAttack();
        return release.Status == PluginCombatCommandStatus.Released;
    }

    private bool IsMonster(uint objectId)
        => objectId != 0
            && _host.Automation.Objects.TryGet(objectId, out PluginWorldObject value)
            && value.ObjectClass == PluginObjectClass.Monster;

    private bool DumpSpells()
    {
        if (!_host.Storage.IsAvailable)
        {
            _chat.Write("Storage is not available; cannot write the spell dump.");
            return false;
        }

        var builder = new StringBuilder();
        builder.AppendLine("SpellId,Name,Family,Tier,Difficulty,ManaCost,School");

        foreach (PluginSpellInfo spell in _host.Automation.Spells.All)
        {
            builder.Append(spell.SpellId.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(Csv(spell.Name))
                .Append(',').Append(spell.Family.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(spell.Tier.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(spell.Difficulty.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(spell.ManaCost.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(spell.School.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        _host.Storage.WriteText(SpellDumpStorageKey, builder.ToString());
        _chat.Write("Spell dump written: " + SpellDumpStorageKey);
        return true;

        static string Csv(string value)
            => value.Contains(',', StringComparison.Ordinal)
                ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
                : value;
    }

    /// <param name="argument">The lower-cased argument, used for matching.</param>
    /// <param name="original">
    /// The same argument as typed. Setting names resolve case-insensitively, so
    /// matching uses the lower-cased form, but a failure message echoes what the
    /// user actually wrote rather than a flattened copy of it.
    /// </param>
    private bool Option(string argument, string original)
    {
        if (argument == "list")
        {
            foreach (SettingDescriptor descriptor in _settings.All)
            {
                _chat.Write(descriptor.QualifiedName
                    + " <" + descriptor.Setting.TypeName + ">");
            }

            return true;
        }

        if (Matches(argument, "get"))
        {
            string name = Remainder(original, "get");
            SettingDescriptor? descriptor = _settings.Find(name);
            if (descriptor is null)
            {
                _chat.Write("Failed to Get " + name);
                return false;
            }

            // The original printed the field name twice and lost the class
            // name; this prints the real Class.Field.
            _chat.Write(descriptor.QualifiedName + " = "
                + descriptor.Setting.ValueText);
            return true;
        }

        if (Matches(argument, "remember"))
        {
            string name = Remainder(original, "remember");
            SettingDescriptor? descriptor = _settings.Find(name);
            if (descriptor is null)
            {
                _chat.Write("Failed to Remember " + name);
                return false;
            }

            _remembered[descriptor.QualifiedName] = descriptor.Setting.ValueText;
            _chat.Write("Remembered " + descriptor.QualifiedName + " = "
                + descriptor.Setting.ValueText);
            return true;
        }

        if (Matches(argument, "restore"))
        {
            string name = Remainder(original, "restore");
            SettingDescriptor? descriptor = _settings.Find(name);
            if (descriptor is null
                || !_remembered.TryGetValue(descriptor.QualifiedName, out string? stored)
                || !descriptor.Setting.TrySetFromText(stored))
            {
                _chat.Write("Failed to Restore " + name);
                return false;
            }

            _chat.Write("Restored " + descriptor.QualifiedName + " = "
                + descriptor.Setting.ValueText);
            return true;
        }

        if (Matches(argument, "set"))
        {
            string rest = Remainder(original, "set");
            int split = rest.IndexOf(' ', StringComparison.Ordinal);
            string name = split < 0 ? rest : rest[..split];
            string value = split < 0 ? string.Empty : rest[(split + 1)..].Trim();

            SettingDescriptor? descriptor = _settings.Find(name);
            if (descriptor is null || !descriptor.Setting.TrySetFromText(value))
            {
                // Matches the other three failure messages' shape
                // ("Failed to Get|Remember|Restore|Set <Option>"); it does not
                // echo the value that failed to parse.
                _chat.Write("Failed to Set " + name);
                return false;
            }

            _chat.Write("Set " + descriptor.QualifiedName + " = "
                + descriptor.Setting.ValueText);
            return true;
        }

        _chat.Write("Usage: /mt opt list|get|set|remember|restore");
        return false;
    }

    // ---- object lookup -------------------------------------------------------

    /// <summary>
    /// The original's two-pass lookup: an exact pass over inventory, then the
    /// open container, then the closest landscape object; only if that finds
    /// nothing and partial matching was asked for, the same three passes again
    /// with a substring match.
    /// </summary>
    public uint FindIdForName(
        string name,
        bool searchInventory,
        bool searchOpenContainer,
        bool searchEnvironment,
        bool partial,
        uint idToSkip = 0)
    {
        if (string.IsNullOrEmpty(name))
            return 0;

        uint exact = FindPass(
            name,
            searchInventory,
            searchOpenContainer,
            searchEnvironment,
            substring: false,
            idToSkip);
        if (exact != 0 || !partial)
            return exact;

        return FindPass(
            name,
            searchInventory,
            searchOpenContainer,
            searchEnvironment,
            substring: true,
            idToSkip);
    }

    private uint FindPass(
        string name,
        bool searchInventory,
        bool searchOpenContainer,
        bool searchEnvironment,
        bool substring,
        uint idToSkip)
    {
        if (searchInventory)
        {
            foreach (PluginInventoryItem item in
                _host.Automation.Items.CaptureOwnedItems())
            {
                if (item.ObjectId != idToSkip && NameMatches(item.Name, name, substring))
                    return item.ObjectId;
            }
        }

        if (searchOpenContainer)
        {
            foreach (PluginInventoryItem item in
                _host.Automation.Loot.CaptureCurrentContents())
            {
                if (item.ObjectId != idToSkip && NameMatches(item.Name, name, substring))
                    return item.ObjectId;
            }
        }

        if (searchEnvironment)
        {
            foreach (PluginWorldObject candidate in LandscapeByDistance())
            {
                if (candidate.ObjectId != idToSkip
                    && NameMatches(candidate.Name, name, substring))
                    return candidate.ObjectId;
            }
        }

        return 0;
    }

    private uint FindInOpenContainer(string name, bool partial)
    {
        foreach (bool substring in partial ? new[] { false, true } : new[] { false })
        {
            foreach (PluginInventoryItem item in
                _host.Automation.Loot.CaptureCurrentContents())
            {
                if (NameMatches(item.Name, name, substring))
                    return item.ObjectId;
            }
        }

        return 0;
    }

    private uint FindOwnedItem(string name, bool partial, bool equipped)
    {
        foreach (bool substring in partial ? new[] { false, true } : new[] { false })
        {
            foreach (PluginInventoryItem item in
                _host.Automation.Items.CaptureOwnedItems())
            {
                if (item.IsEquipped != equipped)
                    continue;
                if (NameMatches(item.Name, name, substring))
                    return item.ObjectId;
            }
        }

        return 0;
    }

    private uint FindClosestObjectNamed(string name, bool partial)
    {
        uint keyword = ResolveKeyword(name);
        if (keyword != 0)
            return keyword;

        foreach (bool substring in partial ? new[] { false, true } : new[] { false })
        {
            foreach (PluginWorldObject candidate in LandscapeByDistance())
            {
                if (NameMatches(candidate.Name, name, substring))
                    return candidate.ObjectId;
            }
        }

        return 0;
    }

    private uint FindClosestOfClass(PluginObjectClass objectClass)
    {
        foreach (PluginWorldObject candidate in LandscapeByDistance())
        {
            if (candidate.ObjectClass == objectClass)
                return candidate.ObjectId;
        }

        return 0;
    }

    private IEnumerable<PluginWorldObject> LandscapeByDistance()
    {
        PluginNavigationPosition origin =
            _host.Automation.Navigation.Snapshot.Position;

        return _host.Automation.Objects.CaptureObjects()
            .Where(static candidate => candidate.IsLandscape && candidate.HasPosition)
            .OrderBy(candidate => candidate.Position.HorizontalDistanceMeters(origin));
    }

    // ---- small helpers -------------------------------------------------------

    private bool TryResolveSpell(string text, bool partial, out uint spellId)
    {
        if (uint.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out spellId))
            return true;

        if (_host.Automation.Spells.TryFindByName(text, partial, out PluginSpellInfo spell))
        {
            spellId = spell.SpellId;
            return true;
        }

        spellId = 0;
        return false;
    }

    private static bool Started(PluginItemCommandResult result)
        => result.Status == PluginItemCommandStatus.Started;

    private static bool NameMatches(string candidate, string name, bool substring)
    {
        if (string.IsNullOrEmpty(candidate))
            return false;
        return substring
            ? candidate.Contains(name, StringComparison.OrdinalIgnoreCase)
            : string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the text is exactly the word, or the word plus args.</summary>
    private static bool Matches(string text, string word)
        => text.Equals(word, StringComparison.Ordinal)
            || text.StartsWith(word + " ", StringComparison.Ordinal);

    private static string Remainder(string text, string word)
        => text.Length <= word.Length ? string.Empty : text[(word.Length + 1)..].Trim();

    private static (string Left, string Right) SplitOn(string text, string separator)
    {
        int index = text.IndexOf(separator, StringComparison.Ordinal);
        return index < 0
            ? (text.Trim(), string.Empty)
            : (text[..index].Trim(), text[(index + separator.Length)..].Trim());
    }
}
