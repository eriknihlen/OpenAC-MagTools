using System.Globalization;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.9 <c>InventoryPacker</c>: merge stacks and move
/// every item into the side pack its <c>&lt;CharacterName&gt;.AutoPack.utl</c>
/// (falling back to <c>Default.AutoPack.utl</c>) profile says it belongs in.
/// Started by the hotkey (default Ctrl+P), <c>/mt autopack</c>, or a chat line
/// containing <c>"&lt;mycharname&gt; autopack"</c> (eaten, same as the
/// original).
/// </summary>
/// <remarks>
/// <para>
/// Profile resolution: the host has no direct "does this profile exist"
/// query beyond <see cref="LootRuleProcessor.ProfileExists"/>'s probe
/// classification (E-LOOT's contract: <c>TryClassifyWithProfile</c> returns
/// <c>false</c> outright when the named profile is not loaded) — this port
/// probes <c>&lt;CharacterName&gt;.AutoPack</c> first, then
/// <c>Default.AutoPack</c>, and stays silent (matching the original's "Neither
/// present -&gt; silent no-op") if neither resolves.
/// </para>
/// <para>
/// <c>DoAutoPack</c>'s <c>Data1</c>/<c>KeepCount</c> digit-by-digit pack
/// priority list, the main-pack-is-index-0 numbering, and the free-slot
/// scan (excluding Containers/Foci/equipped items, matching the original's
/// <c>Util.GetFreePackSlots</c>) are ported verbatim; the 10-second
/// stuck-item blacklist is BY ID here (not by name, as in the looter),
/// exactly as the inventory doc records.
/// </para>
/// </remarks>
public sealed class InventoryPacker
{
    private static readonly TimeSpan ThinkInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StuckWindow = TimeSpan.FromSeconds(10);
    private const string ChatTriggerSuffix = " autopack";

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly LootRuleProcessor _lootRules;
    private readonly TimeProvider _timeProvider;

    private IDisposable? _thinkRegistration;
    private IDisposable? _chatFilterRegistration;
    private readonly HashSet<uint> _idRequested = [];
    private readonly HashSet<uint> _blacklistedIds = [];
    private readonly Dictionary<uint, DateTime> _attemptStartedUtc = [];
    private string _profileName = string.Empty;
    private bool _running;

    public InventoryPacker(
        IPluginHost host,
        ChatOutput chat,
        LootRuleProcessor lootRules,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(lootRules);
        _host = host;
        _chat = chat;
        _lootRules = lootRules;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsRunning => _running;

    /// <summary>Registers the "&lt;mycharname&gt; autopack" chat trigger. Independent of the run/stop lifecycle.</summary>
    public void AttachChatTrigger()
        => _chatFilterRegistration ??= _host.Automation.Chat.RegisterFilter(OnChatMessage);

    public void DetachChatTrigger()
    {
        _chatFilterRegistration?.Dispose();
        _chatFilterRegistration = null;
    }

    private bool OnChatMessage(PluginChatMessage message)
    {
        string text = message.Text;
        if (string.IsNullOrEmpty(text))
            return false;
        if (text.StartsWith("You say, ", StringComparison.Ordinal))
            return false;
        if (text.Contains("says, \"", StringComparison.Ordinal))
            return false;

        string trigger = _host.Automation.Character.Name + ChatTriggerSuffix;
        if (!text.Contains(trigger, StringComparison.OrdinalIgnoreCase))
            return false;

        Start();
        return true;
    }

    /// <summary>Bound once at session start so a hotkey/chat/command trigger can call the no-argument <see cref="Start()"/>.</summary>
    private TickScheduler? _boundScheduler;

    public void Bind(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _boundScheduler = scheduler;
    }

    /// <summary>
    /// Starts a run: resolves the profile (character-scoped, falling back to
    /// <c>Default.AutoPack</c>), and, if neither exists, stays a silent
    /// no-op exactly like the original. A no-op while already running.
    /// </summary>
    public void Start()
    {
        if (_running || _boundScheduler is null)
            return;

        string characterName = _host.Automation.Character.Name;
        string primary = characterName + ".AutoPack";
        const string fallback = "Default.AutoPack";

        if (_lootRules.ProfileExists(primary))
            _profileName = primary;
        else if (_lootRules.ProfileExists(fallback))
            _profileName = fallback;
        else
            return;

        _running = true;
        _idRequested.Clear();
        _blacklistedIds.Clear();
        _attemptStartedUtc.Clear();

        _chat.Write("Auto Pack - Started.");
        _thinkRegistration = _boundScheduler.Every(ThinkInterval, Think);
    }

    public void Stop()
    {
        _thinkRegistration?.Dispose();
        _thinkRegistration = null;
        _running = false;
        _idRequested.Clear();
        _blacklistedIds.Clear();
        _attemptStartedUtc.Clear();
    }

    private void Think()
    {
        if (!_running)
            return;

        bool pendingId = false;
        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (item.IsEquipped)
                continue;
            if (TryHasAppraisal(item.ObjectId))
                continue;

            pendingId = true;
            if (_idRequested.Add(item.ObjectId))
                _host.Automation.Objects.Identify(item.ObjectId);
        }

        if (_host.Automation.Items.IsBusy)
            return;

        if (DoAutoStack())
            return;
        if (DoAutoPack())
            return;

        if (!pendingId)
        {
            _chat.Write("Auto Pack - Completed.");
            Complete();
        }
    }

    private void Complete()
    {
        _thinkRegistration?.Dispose();
        _thinkRegistration = null;
        _running = false;
        _idRequested.Clear();
        _blacklistedIds.Clear();
        _attemptStartedUtc.Clear();
    }

    private bool TryHasAppraisal(uint objectId)
        => _host.Automation.Objects.TryGet(objectId, out PluginWorldObject world)
            && world.HasAppraisalData;

    /// <summary>Merges the first pair of same-name, non-full stackable items sharing a container. One merge per tick.</summary>
    private bool DoAutoStack()
    {
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();

        for (int i = 0; i < owned.Count; i++)
        {
            PluginInventoryItem item = owned[i];
            if (item.IsEquipped || item.MaximumStackSize <= 1 || item.StackSize >= item.MaximumStackSize)
                continue;

            for (int j = 0; j < owned.Count; j++)
            {
                if (i == j)
                    continue;
                PluginInventoryItem other = owned[j];
                if (other.IsEquipped || other.MaximumStackSize <= 1
                    || other.StackSize >= other.MaximumStackSize)
                    continue;
                if (other.ContainerObjectId != item.ContainerObjectId)
                    continue;
                if (!string.Equals(other.Name, item.Name, StringComparison.Ordinal))
                    continue;

                _host.Automation.Items.Merge(item.ObjectId, other.ObjectId);
                return true;
            }
        }

        return false;
    }

    private bool DoAutoPack()
    {
        Dictionary<int, uint> packs = BuildPacks();
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();

        List<(PluginInventoryItem Item, List<int> PackNumbers)> candidates = [];
        foreach (PluginInventoryItem item in owned)
        {
            if (item.IsEquipped)
                continue;
            if (item.ObjectClass is PluginObjectClass.Container or PluginObjectClass.Foci)
                continue;
            if (_blacklistedIds.Contains(item.ObjectId))
                continue;
            if (!TryClassify(item, out PluginLootClassification classification))
                continue;
            if (classification.Action != PluginLootAction.KeepUpTo)
                continue;

            List<int> digits = DigitsOf(classification.KeepCount);
            if (digits.Count == 0)
                continue;
            if (packs.TryGetValue(digits[0], out uint primaryPackId)
                && item.ContainerObjectId == primaryPackId)
                continue;

            candidates.Add((item, digits));
        }

        for (int priority = 0; priority <= 9; priority++)
        {
            foreach ((PluginInventoryItem item, List<int> digits) in candidates)
            {
                if (priority >= digits.Count)
                    continue;
                int packNumber = digits[priority];
                if (!packs.TryGetValue(packNumber, out uint targetPackId))
                    continue;
                if (item.ContainerObjectId == targetPackId)
                    continue;

                int freeSlots = GetFreePackSlots(targetPackId, owned);
                if (freeSlots <= 0)
                    continue;

                if (IsStuck(item.ObjectId))
                {
                    _chat.Write("Blacklisting item: " + item.ObjectId + ", " + item.Name);
                    _blacklistedIds.Add(item.ObjectId);
                    _attemptStartedUtc.Remove(item.ObjectId);
                    continue;
                }

                _host.Automation.Items.MoveToContainer(item.ObjectId, targetPackId, 0u, 1);
                return true;
            }
        }

        return false;
    }

    private bool IsStuck(uint objectId)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!_attemptStartedUtc.TryGetValue(objectId, out DateTime startedUtc))
        {
            _attemptStartedUtc[objectId] = now;
            return false;
        }

        return now - startedUtc > StuckWindow;
    }

    private Dictionary<int, uint> BuildPacks()
    {
        var packs = new Dictionary<int, uint> { [0] = _host.Automation.Character.ObjectId };
        foreach (PluginInventoryItem item in _host.Automation.Items.CaptureOwnedItems())
        {
            if (item.ObjectClass != PluginObjectClass.Container)
                continue;
            if (item.ContainerSlot < 0)
                continue;
            packs[item.ContainerSlot + 1] = item.ObjectId;
        }

        return packs;
    }

    /// <summary>
    /// <c>ItemSlots − (items in the container that are not
    /// Containers/Foci and not equipped)</c>, verbatim from
    /// <c>Util.GetFreePackSlots</c>.
    /// </summary>
    private int GetFreePackSlots(uint containerObjectId, IReadOnlyList<PluginInventoryItem> owned)
    {
        if (containerObjectId == _host.Automation.Character.ObjectId)
            return _host.Automation.Character.MainPackFreeSlots;

        int capacity = 0;
        foreach (PluginInventoryItem candidate in owned)
        {
            if (candidate.ObjectId != containerObjectId)
                continue;
            capacity = candidate.ItemsCapacity;
            break;
        }

        int used = 0;
        foreach (PluginInventoryItem candidate in owned)
        {
            if (candidate.ContainerObjectId != containerObjectId)
                continue;
            if (candidate.IsEquipped)
                continue;
            if (candidate.ObjectClass is PluginObjectClass.Container or PluginObjectClass.Foci)
                continue;
            used++;
        }

        return capacity - used;
    }

    private static List<int> DigitsOf(int value)
    {
        var digits = new List<int>();
        string text = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        foreach (char c in text)
            digits.Add(c - '0');
        return digits;
    }

    private bool TryClassify(PluginInventoryItem item, out PluginLootClassification classification)
    {
        _host.Automation.Items.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties);
        var context = new PluginLootClassificationContext(
            item, properties, Array.Empty<PluginInventoryItem>());
        return _lootRules.TryClassifyWithProfile(_profileName, in context, out classification);
    }
}
