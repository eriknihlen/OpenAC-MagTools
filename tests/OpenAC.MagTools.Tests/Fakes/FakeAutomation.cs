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

    public FakeDialogAutomation Dialogs { get; } = new();

    public FakeTrade Trade { get; } = new();

    public FakeVendor Vendor { get; } = new();

    public FakeLogin Login { get; } = new();

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
    IDialogAutomation IAutomationSurface.Dialogs => Dialogs;
    ITradeAutomation IAutomationSurface.Trade => Trade;
    IVendorAutomation IAutomationSurface.Vendor => Vendor;
    ILoginAutomation IAutomationSurface.Login => Login;
}

/// <summary>A settable <see cref="ITradeAutomation"/>: a test drives <see cref="Raise*"/> and inspects <see cref="Calls"/>.</summary>
public sealed class FakeTrade : ITradeAutomation
{
    public bool IsAvailable { get; set; } = true;
    public bool IsOpen { get; set; }
    public uint PartnerObjectId { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public List<uint> MyItems { get; } = [];
    public List<uint> PartnerItems { get; } = [];
    public bool MyAccepted { get; set; }
    public bool PartnerAccepted { get; set; }

    public List<string> Calls { get; } = [];

    /// <summary>The status <see cref="Add"/>/<see cref="Accept"/>/<see cref="Decline"/>/<see cref="Reset"/>/<see cref="End"/> answer with.</summary>
    public PluginTradeCommandStatus NextStatus { get; set; } = PluginTradeCommandStatus.Sent;

    IReadOnlyList<uint> ITradeAutomation.MyItems => MyItems;
    IReadOnlyList<uint> ITradeAutomation.PartnerItems => PartnerItems;

    public event Action<PluginTradeOpened>? Opened;
    public event Action? Closed;
    public event Action<uint>? PartnerTradeAccepted;
    public event Action<PluginTradeItemAdded>? ItemAdded;

    public void RaiseOpened(uint initiatorObjectId, uint partnerObjectId)
    {
        IsOpen = true;
        PartnerObjectId = partnerObjectId;
        Opened?.Invoke(new PluginTradeOpened(initiatorObjectId, partnerObjectId));
    }

    public void RaiseClosed()
    {
        IsOpen = false;
        Closed?.Invoke();
    }

    public void RaisePartnerAccepted(uint partnerObjectId)
        => PartnerTradeAccepted?.Invoke(partnerObjectId);

    public void RaiseItemAdded(uint itemObjectId, bool mine)
        => ItemAdded?.Invoke(new PluginTradeItemAdded(itemObjectId, mine));

    public PluginTradeCommandResult Add(uint itemObjectId)
    {
        Calls.Add("add:" + itemObjectId);
        return new PluginTradeCommandResult(NextStatus);
    }

    public PluginTradeCommandResult Accept()
    {
        Calls.Add("accept");
        return new PluginTradeCommandResult(NextStatus);
    }

    public PluginTradeCommandResult Decline()
    {
        Calls.Add("decline");
        return new PluginTradeCommandResult(NextStatus);
    }

    public PluginTradeCommandResult Reset()
    {
        Calls.Add("reset");
        return new PluginTradeCommandResult(NextStatus);
    }

    public PluginTradeCommandResult End()
    {
        Calls.Add("end");
        return new PluginTradeCommandResult(NextStatus);
    }

    event Action<PluginTradeOpened> ITradeAutomation.Opened
    {
        add => Opened += value;
        remove => Opened -= value;
    }

    event Action ITradeAutomation.Closed
    {
        add => Closed += value;
        remove => Closed -= value;
    }

    event Action<uint> ITradeAutomation.PartnerTradeAccepted
    {
        add => PartnerTradeAccepted += value;
        remove => PartnerTradeAccepted -= value;
    }

    event Action<PluginTradeItemAdded> ITradeAutomation.ItemAdded
    {
        add => ItemAdded += value;
        remove => ItemAdded -= value;
    }
}

/// <summary>A settable <see cref="IVendorAutomation"/>.</summary>
public sealed class FakeVendor : IVendorAutomation
{
    public bool IsAvailable { get; set; } = true;
    public bool IsOpen { get; set; }
    public uint VendorObjectId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public List<PluginVendorItem> Items { get; } = [];
    public bool IsBusy { get; set; }

    public Dictionary<uint, PluginItemProperties> Properties { get; } = [];

    public List<(uint TemplateObjectId, int Count)> BuyList { get; } = [];
    public List<uint> SellList { get; } = [];

    public List<string> Calls { get; } = [];

    public PluginVendorCommandStatus NextStatus { get; set; } = PluginVendorCommandStatus.Sent;

    IReadOnlyList<PluginVendorItem> IVendorAutomation.Items => Items;
    IReadOnlyList<(uint TemplateObjectId, int Count)> IVendorAutomation.BuyList => BuyList;
    IReadOnlyList<uint> IVendorAutomation.SellList => SellList;

    public event Action<uint>? Opened;
    public event Action? Closed;
    public event Action<PluginVendorTransaction>? TransactionCompleted;

    public void RaiseOpened(uint vendorObjectId)
    {
        IsOpen = true;
        VendorObjectId = vendorObjectId;
        Opened?.Invoke(vendorObjectId);
    }

    public void RaiseClosed()
    {
        IsOpen = false;
        Closed?.Invoke();
    }

    public void RaiseTransactionCompleted(PluginVendorTransactionKind kind, bool success = true)
        => TransactionCompleted?.Invoke(new PluginVendorTransaction(kind, success, null));

    public bool TryCaptureProperties(uint templateObjectId, out PluginItemProperties properties)
        => Properties.TryGetValue(templateObjectId, out properties);

    public PluginVendorCommandResult AddToBuyList(uint templateObjectId, int count)
    {
        Calls.Add("addbuy:" + templateObjectId + ":" + count);
        BuyList.Add((templateObjectId, count));
        return new PluginVendorCommandResult(NextStatus);
    }

    public PluginVendorCommandResult AddToSellList(uint itemObjectId)
    {
        Calls.Add("addsell:" + itemObjectId);
        SellList.Add(itemObjectId);
        return new PluginVendorCommandResult(NextStatus);
    }

    public PluginVendorCommandResult RemoveFromBuyList(uint templateObjectId)
    {
        Calls.Add("removebuy:" + templateObjectId);
        return new PluginVendorCommandResult(NextStatus);
    }

    public PluginVendorCommandResult RemoveFromSellList(uint itemObjectId)
    {
        Calls.Add("removesell:" + itemObjectId);
        return new PluginVendorCommandResult(NextStatus);
    }

    public PluginVendorCommandResult ClearBuyList()
    {
        Calls.Add("clearbuy");
        BuyList.Clear();
        return new PluginVendorCommandResult(NextStatus);
    }

    public PluginVendorCommandResult ClearSellList()
    {
        Calls.Add("clearsell");
        SellList.Clear();
        return new PluginVendorCommandResult(NextStatus);
    }

    public PluginVendorCommandResult BuyAll()
    {
        Calls.Add("buyall");
        return new PluginVendorCommandResult(NextStatus);
    }

    public PluginVendorCommandResult SellAll()
    {
        Calls.Add("sellall");
        return new PluginVendorCommandResult(NextStatus);
    }

    event Action<uint> IVendorAutomation.Opened
    {
        add => Opened += value;
        remove => Opened -= value;
    }

    event Action IVendorAutomation.Closed
    {
        add => Closed += value;
        remove => Closed -= value;
    }

    event Action<PluginVendorTransaction> IVendorAutomation.TransactionCompleted
    {
        add => TransactionCompleted += value;
        remove => TransactionCompleted -= value;
    }
}

/// <summary>A settable <see cref="ILoginAutomation"/>.</summary>
public sealed class FakeLogin : ILoginAutomation
{
    public bool IsAvailable { get; set; } = true;
    public bool LogoutResult { get; set; } = true;
    public int LogoutCalls { get; private set; }

    public bool Logout()
    {
        LogoutCalls++;
        return LogoutResult;
    }
}

/// <summary>Records every <see cref="Answer"/> call; a test can pre-arm which context ids it accepts.</summary>
public sealed class FakeDialogAutomation : IDialogAutomation
{
    public List<(uint ContextId, bool Accept)> Answers { get; } = [];

    /// <summary>Context ids <see cref="Answer"/> reports as outstanding (returns true for).</summary>
    public HashSet<uint> OutstandingContextIds { get; } = [];

    public bool Answer(uint contextId, bool accept)
    {
        Answers.Add((contextId, accept));
        return OutstandingContextIds.Contains(contextId);
    }
}

public sealed class FakeCharacter : ICharacterInfo
{
    public bool IsInWorld { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public int ServerPopulation { get; set; } = -1;
    public string AccountName { get; set; } = string.Empty;
    public int MainPackFreeSlots { get; set; }
    public uint ObjectId { get; set; }
    public uint CurrentHealth { get; set; }
    public uint MaxHealth { get; set; }
    public uint CurrentStamina { get; set; }
    public uint MaxStamina { get; set; }
    public uint CurrentMana { get; set; }
    public uint MaxMana { get; set; }

    public List<PluginSkillInfo> Skills { get; } = [];
    public IReadOnlyList<PluginAttributeInfo> Attributes { get; set; } = [];
    public IReadOnlyList<PluginActiveEnchantment> ActiveEnchantments { get; set; } = [];

    IReadOnlyList<PluginSkillInfo> ICharacterInfo.Skills => Skills;

    public bool TryGetSkill(uint skillId, out PluginSkillInfo skill)
    {
        foreach (PluginSkillInfo candidate in Skills)
        {
            if (candidate.SkillId != skillId)
                continue;
            skill = candidate;
            return true;
        }

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

    /// <summary>
    /// How many distinct handlers are currently subscribed to
    /// <see cref="Received"/> — used to prove a shared fan-out
    /// (<c>ChatClassificationDispatcher</c>) adds exactly one subscription
    /// to the raw feed no matter how many of ITS OWN consumers subscribe to
    /// it in turn.
    /// </summary>
    public int ReceivedSubscriberCount => Received?.GetInvocationList().Length ?? 0;

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
        int combatKind = 0,
        uint senderObjectId = 0u)
    {
        var message = new PluginChatMessage(
            _nextSequence++, senderObjectId, kind, sender, text, channelName)
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

    /// <summary>Keyed by object id; set by a test to make <see cref="TryCaptureProperties"/> answer.</summary>
    public Dictionary<uint, PluginItemProperties> Properties { get; } = [];

    /// <summary>
    /// How many times <see cref="CaptureOwnedItems"/> has been called — used
    /// to prove a coalescing host (H1/H3) captures at most once per tick no
    /// matter how many <c>ObjectChanged</c> events arrived inside it.
    /// </summary>
    public int CaptureCount { get; private set; }

    public IReadOnlyList<PluginInventoryItem> CaptureOwnedItems()
    {
        CaptureCount++;
        return Owned;
    }

    public bool TryCaptureProperties(uint objectId, out PluginItemProperties properties)
        => Properties.TryGetValue(objectId, out properties);

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

    /// <summary>Every <see cref="MoveToContainer"/> call, in order.</summary>
    public List<(uint ObjectId, uint ContainerObjectId, uint Amount, int Placement)> Moves { get; } = [];

    /// <summary>Every <see cref="Merge"/> call, in order.</summary>
    public List<(uint SourceObjectId, uint TargetObjectId, uint Amount)> Merges { get; } = [];

    public PluginItemCommandResult MoveToContainer(
        uint objectId,
        uint containerObjectId,
        uint amount = 0u,
        int placement = 0)
    {
        Calls.Add(("move", objectId, containerObjectId));
        Moves.Add((objectId, containerObjectId, amount, placement));
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    public PluginItemCommandResult Merge(
        uint sourceObjectId,
        uint targetObjectId,
        uint amount = 0u)
    {
        Calls.Add(("merge", sourceObjectId, targetObjectId));
        Merges.Add((sourceObjectId, targetObjectId, amount));
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

    public bool IsBusy { get; set; }

    public List<PluginInventoryItem> Contents { get; } = [];

    public List<uint> PickedUp { get; } = [];

    public List<uint> Opened { get; } = [];

    public List<uint> IdentifyRequests { get; } = [];

    public Dictionary<uint, PluginItemProperties> Properties { get; } = [];

    public IReadOnlyList<PluginInventoryItem> CaptureCurrentContents() => Contents;

    public bool TryCaptureProperties(uint objectId, out PluginItemProperties properties)
        => Properties.TryGetValue(objectId, out properties);

    public PluginItemCommandResult Open(uint containerObjectId)
    {
        Opened.Add(containerObjectId);
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    public PluginItemCommandResult Identify(uint objectId)
    {
        IdentifyRequests.Add(objectId);
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

    public uint OpenContainerObjectId { get; set; }

    public List<PluginWorldObject> Objects { get; } = [];

    public Dictionary<uint, PluginItemProperties> Properties { get; } = [];

    public List<uint> IdentifyRequests { get; } = [];

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

    public bool TryCaptureProperties(uint objectId, out PluginItemProperties properties)
        => Properties.TryGetValue(objectId, out properties);

    public PluginItemCommandResult Identify(uint objectId)
    {
        IdentifyRequests.Add(objectId);
        return new PluginItemCommandResult(PluginItemCommandStatus.Started);
    }

    /// <summary>Replaces a tracked object's snapshot (e.g. to flip <c>HasAppraisalData</c>).</summary>
    public void Replace(PluginWorldObject value)
    {
        for (int index = 0; index < Objects.Count; index++)
        {
            if (Objects[index].ObjectId != value.ObjectId)
                continue;
            Objects[index] = value;
            return;
        }

        Objects.Add(value);
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

    /// <summary>Backs <see cref="CaptureObjects"/> for tests that need a nearby landscape object (e.g. a chest).</summary>
    public List<PluginNavigationObject> Objects { get; } = [];

    public IReadOnlyList<PluginNavigationObject> CaptureObjects() => Objects;

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
