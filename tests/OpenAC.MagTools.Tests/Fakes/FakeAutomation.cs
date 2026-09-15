using AcDream.Plugin.Abstractions;

namespace OpenAC.MagTools.Tests.Fakes;

/// <summary>
/// A settable automation surface. Every child defaults to an empty fake, so a
/// test fills in only the state it exercises.
/// </summary>
public sealed class FakeAutomation : IAutomationSurface
{
    public bool IsAvailable { get; set; }

    public FakeCharacter Character { get; } = new();

    public RecordingChat Chat { get; } = new();

    public FakeSpellCatalog Spells { get; } = new();

    public FakeMagic Magic { get; } = new();

    public FakeItems Items { get; } = new();

    public FakeLoot Loot { get; } = new();

    public FakeObjects Objects { get; } = new();

    public FakeNavigation Navigation { get; } = new();

    public FakeFellowship Fellowship { get; } = new();

    public FakeCombat Combat { get; } = new();

    public FakeEquipment Equipment { get; } = new();

    ICharacterInfo IAutomationSurface.Character => Character;
    ISpellCatalog IAutomationSurface.Spells => Spells;
    IMagicCommands IAutomationSurface.Magic => Magic;
    IPluginChat IAutomationSurface.Chat => Chat;
    IItemAutomation IAutomationSurface.Items => Items;
    ILootAutomation IAutomationSurface.Loot => Loot;
    IWorldObjectAutomation IAutomationSurface.Objects => Objects;
    INavigationAutomation IAutomationSurface.Navigation => Navigation;
    IFellowshipAutomation IAutomationSurface.Fellowship => Fellowship;
    ICombatAutomation IAutomationSurface.Combat => Combat;
    IEquipmentAutomation IAutomationSurface.Equipment => Equipment;
}

public sealed class FakeCharacter : ICharacterInfo
{
    public bool IsInWorld { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public int ServerPopulation { get; set; } = -1;
    public string AccountName { get; set; } = string.Empty;
    public uint ObjectId { get; set; }
    public uint CurrentHealth { get; set; }
    public uint MaxHealth { get; set; }
    public uint CurrentStamina { get; set; }
    public uint MaxStamina { get; set; }
    public uint CurrentMana { get; set; }
    public uint MaxMana { get; set; }

    public IReadOnlyList<PluginSkillInfo> Skills { get; set; } = [];
    public IReadOnlyList<PluginAttributeInfo> Attributes { get; set; } = [];
    public IReadOnlyList<PluginActiveEnchantment> ActiveEnchantments { get; set; } = [];

    public bool TryGetSkill(uint skillId, out PluginSkillInfo skill)
    {
        skill = default;
        return false;
    }
}

/// <summary>
/// Records every posted line and every submitted command, and can raise
/// <see cref="Received"/> and drive registered filters the way the real host
/// does — before a line is added to <see cref="Posted"/> or delivered to
/// <see cref="Received"/>, every registered filter gets a chance to eat it.
/// </summary>
public sealed class RecordingChat : IPluginChat
{
    private readonly List<Func<PluginChatMessage, bool>> _filters = [];
    private ulong _nextSequence = 1;

    public List<string> Posted { get; } = [];

    public List<string> Submitted { get; } = [];

    public List<(string Text, int LogTextType)> PostedTyped { get; } = [];

    /// <summary>Every message offered to <see cref="Deliver"/>, filtered or not.</summary>
    public List<PluginChatMessage> Offered { get; } = [];

    /// <summary>Messages that survived every filter and reached <see cref="Received"/>.</summary>
    public List<PluginChatMessage> Delivered { get; } = [];

    public IReadOnlyList<PluginChatMessage> Messages { get; set; } = [];

    public IReadOnlyList<PluginChatMessage> CaptureMessages(ulong afterSequence)
        => [.. Messages.Where(message => message.Sequence > afterSequence)];

    public event Action<PluginChatMessage>? Received;

    public IDisposable RegisterFilter(Func<PluginChatMessage, bool> suppress)
    {
        _filters.Add(suppress);
        return new FilterRegistration(this, suppress);
    }

    /// <summary>Builds and offers a message the way the real client would, honoring filters.</summary>
    public PluginChatMessage Deliver(
        string text,
        int kind = 0,
        string sender = "",
        string channelName = "",
        int logTextType = 0,
        int combatKind = 0)
    {
        var message = new PluginChatMessage(
            _nextSequence++, 0u, kind, sender, text, channelName)
        {
            LogTextType = logTextType,
            CombatKind = combatKind,
            Received = DateTimeOffset.UtcNow,
        };

        Offered.Add(message);

        foreach (Func<PluginChatMessage, bool> filter in _filters)
        {
            if (filter(message))
                return message;
        }

        Delivered.Add(message);
        Received?.Invoke(message);
        return message;
    }

    public void PostSystemMessage(string text) => Posted.Add(text);

    public void PostMessage(string text, int logTextType)
    {
        Posted.Add(text);
        PostedTyped.Add((text, logTextType));
    }

    public bool Submit(string text)
    {
        Submitted.Add(text);
        return true;
    }

    private sealed class FilterRegistration(
        RecordingChat owner,
        Func<PluginChatMessage, bool> filter) : IDisposable
    {
        public void Dispose() => owner._filters.Remove(filter);
    }
}

public sealed class FakeSpellCatalog : ISpellCatalog
{
    public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; set; } = [];
    public IReadOnlyList<PluginSpellInfo> KnownAttackSpells { get; set; } = [];
    public IReadOnlyList<PluginSpellInfo> KnownCombatSpells { get; set; } = [];

    /// <summary>The full spell table, independent of what the character knows.</summary>
    public IReadOnlyList<PluginSpellInfo> All { get; set; } = [];

    public bool TryGet(uint spellId, out PluginSpellInfo info)
    {
        foreach (PluginSpellInfo spell in KnownSelfBuffs
            .Concat(KnownAttackSpells).Concat(KnownCombatSpells).Concat(All))
        {
            if (spell.SpellId != spellId)
                continue;
            info = spell;
            return true;
        }

        info = default;
        return false;
    }

    public bool TryFindByName(string name, bool partialMatch, out PluginSpellInfo spell)
    {
        spell = default;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (PluginSpellInfo candidate in All)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                spell = candidate;
                return true;
            }
        }

        if (!partialMatch)
            return false;

        foreach (PluginSpellInfo candidate in All)
        {
            if (candidate.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                spell = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds a minimal spell record for a test.</summary>
    public static PluginSpellInfo Spell(uint spellId, string name)
        => new(spellId, name, 0u, 1, 0, 0, 0f, 0u, string.Empty, true, true);
}

public sealed class FakeMagic : IMagicCommands
{
    public bool IsCasting { get; set; }

    public List<(uint SpellId, uint TargetObjectId)> Casts { get; } = [];

    public PluginCastGate EvaluateGate(uint spellId) => PluginCastGate.Ready;

    public PluginCastGate EvaluateGate(uint spellId, uint targetObjectId)
        => PluginCastGate.Ready;

    public bool Cast(uint spellId)
    {
        Casts.Add((spellId, 0u));
        return true;
    }

    public bool Cast(uint spellId, uint targetObjectId)
    {
        Casts.Add((spellId, targetObjectId));
        return true;
    }
}

public sealed class FakeItems : IItemAutomation
{
    public bool IsAvailable { get; set; } = true;
    public bool IsBusy { get; set; }

    public List<PluginInventoryItem> Owned { get; } = [];

    public List<(string Command, uint ObjectId, uint TargetObjectId)> Calls { get; } = [];

    public IReadOnlyList<PluginInventoryItem> CaptureOwnedItems() => Owned;

    public PluginItemCommandResult Use(uint objectId)
    {
        Calls.Add(("use", objectId, 0u));
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    public PluginItemCommandResult Apply(uint objectId, uint targetObjectId)
    {
        Calls.Add(("apply", objectId, targetObjectId));
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    public PluginItemCommandResult Drop(uint objectId, uint amount = 0u)
    {
        Calls.Add(("drop", objectId, 0u));
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    public PluginItemCommandResult Give(
        uint objectId,
        uint targetObjectId,
        uint amount = 0u)
    {
        Calls.Add(("give", objectId, targetObjectId));
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    /// <summary>Builds a minimal owned item for a test.</summary>
    public static PluginInventoryItem Item(
        uint objectId,
        string name,
        uint equippedLocation = 0u)
        => new(
            objectId, 0u, name, 0u, 0u, 0u, 0u, equippedLocation, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0);
}

public sealed class FakeLoot : ILootAutomation
{
    public bool IsAvailable { get; set; } = true;

    public List<PluginInventoryItem> Contents { get; } = [];

    public List<uint> PickedUp { get; } = [];

    public List<uint> Opened { get; } = [];

    public IReadOnlyList<PluginInventoryItem> CaptureCurrentContents() => Contents;

    public PluginItemCommandResult Open(uint containerObjectId)
    {
        Opened.Add(containerObjectId);
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    public PluginItemCommandResult Pickup(uint objectId, bool mainPack = false)
    {
        PickedUp.Add(objectId);
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }
}

public sealed class FakeObjects : IWorldObjectAutomation
{
    public bool IsAvailable { get; set; } = true;

    public List<PluginWorldObject> Objects { get; } = [];

    public IReadOnlyList<PluginWorldObject> CaptureObjects() => Objects;

    public bool TryGet(uint objectId, out PluginWorldObject value)
    {
        foreach (PluginWorldObject candidate in Objects)
        {
            if (candidate.ObjectId != objectId)
                continue;
            value = candidate;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Builds a landscape object at <paramref name="metres"/> east of the
    /// origin the fake navigation reports.
    /// </summary>
    public static PluginWorldObject Landscape(
        uint objectId,
        string name,
        PluginObjectClass objectClass,
        double metres)
        => new(objectId, 0u, name, objectClass, 0u, 0u, 0u)
        {
            IsLandscape = true,
            HasPosition = true,
            Position = new PluginNavigationPosition(
                1u, metres / 240d, 0d, 0d, 0f, true),
        };
}

public sealed class FakeNavigation : INavigationAutomation
{
    public PluginNavigationSnapshot Snapshot { get; set; } =
        new(true, false, 1u, default, false, false);

    public List<float> Headings { get; } = [];

    public PluginNavigationCommandStatus FaceHeading(float headingDegrees)
    {
        Headings.Add(headingDegrees);
        return PluginNavigationCommandStatus.Accepted;
    }

    public bool TryGetObject(uint objectId, out PluginNavigationObject value)
    {
        value = default;
        return false;
    }

    public PluginNavigationCommandStatus SetMovementIntent(
        in PluginMovementIntent intent)
        => PluginNavigationCommandStatus.Accepted;

    public PluginNavigationCommandStatus ClearMovementIntent()
        => PluginNavigationCommandStatus.Accepted;
}

public sealed class FakeFellowship : IFellowshipAutomation
{
    public List<string> Calls { get; } = [];

    public bool IsInFellowship { get; set; }

    public PluginFellowshipCommandResult Create(string name, bool shareExperience)
    {
        Calls.Add("create:" + name + ":" + shareExperience);
        return new PluginFellowshipCommandResult(
            PluginFellowshipCommandStatus.Accepted);
    }

    public PluginFellowshipCommandResult SetOpen(bool isOpen)
    {
        Calls.Add("setopen:" + isOpen);
        return new PluginFellowshipCommandResult(
            PluginFellowshipCommandStatus.Accepted);
    }

    public PluginFellowshipCommandResult Quit(bool disband)
    {
        Calls.Add("quit:" + disband);
        return new PluginFellowshipCommandResult(
            PluginFellowshipCommandStatus.Accepted);
    }

    public PluginFellowshipCommandResult Recruit(uint targetObjectId)
    {
        Calls.Add("recruit:" + targetObjectId);
        return new PluginFellowshipCommandResult(
            PluginFellowshipCommandStatus.Accepted);
    }
}

public sealed class FakeCombat : ICombatAutomation
{
    public PluginCombatSnapshot Snapshot { get; set; } =
        new(0u, PluginCombatMode.Peace, PluginAttackHeight.Medium, 1f, 0f,
            false, false, false, false);

    public List<string> Calls { get; } = [];

    /// <summary>Every BeginPhysicalAttack call, with the height/power it used.</summary>
    public List<(uint TargetObjectId, PluginAttackHeight Height, float Power)>
        BeginAttacks { get; } = [];

    public PluginCombatCommandResult EnterDefaultMode()
    {
        Calls.Add("mode:default");
        return new PluginCombatCommandResult(
            PluginCombatCommandStatus.ModeChangeSent);
    }

    public PluginCombatCommandResult EnterMode(PluginCombatMode mode)
    {
        Calls.Add("mode:" + mode);
        return new PluginCombatCommandResult(
            PluginCombatCommandStatus.ModeChangeSent);
    }

    public PluginCombatCommandResult BeginPhysicalAttack(
        uint targetObjectId,
        PluginAttackHeight height,
        float power)
    {
        Calls.Add("attack:" + targetObjectId);
        BeginAttacks.Add((targetObjectId, height, power));
        return new PluginCombatCommandResult(PluginCombatCommandStatus.Started);
    }

    public IReadOnlyList<PluginCombatTarget> CaptureHostileTargets(
        float maximumDistance) => [];

    public PluginCombatCommandResult ReleasePhysicalAttack()
    {
        Calls.Add("release");
        return new PluginCombatCommandResult(PluginCombatCommandStatus.Released);
    }

    public PluginCombatCommandResult AbortPhysicalAttack()
    {
        Calls.Add("abort");
        return new PluginCombatCommandResult(PluginCombatCommandStatus.Stopped);
    }
}

public sealed class FakeEquipment : IEquipmentAutomation
{
    public bool IsAvailable { get; set; } = true;

    public List<uint> Equipped { get; } = [];

    public PluginEquipmentCommandResult Equip(
        uint objectId,
        uint requestedLocation = 0u)
    {
        Equipped.Add(objectId);
        return new PluginEquipmentCommandResult(
            PluginEquipmentCommandStatus.Started);
    }
}
