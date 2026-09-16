using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Macros;

/// <summary>
/// Port of the original's §1.8 <c>Looter</c>: on <c>ContainerOpened</c>,
/// auto-loot a corpse/chest/one's-own-corpse per the four
/// <c>Looting/*</c> settings and the LIVE loot-classifier profile (unlike
/// <see cref="AutoBuySell"/>/<see cref="AutoTradeAdd"/>/
/// <see cref="InventoryPacker"/>, which classify against a NAMED profile,
/// the looter uses whatever profile the classifier already has loaded live —
/// <c>FLootPluginClassifyImmediate</c> in the original).
/// </summary>
/// <remarks>
/// Never loots a container named exactly <c>Storage</c>. Starts when: the
/// container is a Corpse and <c>AutoLootCorpses</c>; or a Corpse named
/// <c>Corpse of &lt;MyName&gt;</c> and <c>AutoLootMyCorpses</c> (loots
/// unconditionally, skipping every rule check); or <c>AutoLootChests</c> and
/// the name contains "Chest", "Vault" or "Reliquary". Think loop at 100 ms:
/// one item's id requested at a time (a plugin identify can be legitimately
/// <c>Refused</c>/<c>Busy</c> while the user has an appraisal of their own in
/// flight — see docs/plugin-api.md's Identify section — so this retries next
/// think rather than failing the id pass outright); classification is
/// skipped for NoLoot, Salvage unless <c>LootSalvage</c>, and anything that
/// is neither Keep nor KeepUpTo; a Keep-Up-To rule is skipped once the owned
/// count (matching rule name + KeepCount + item name, stack-aware) already
/// meets its threshold; an item that has been the "current working id" for
/// over 10 seconds is blacklisted BY NAME for the rest of this run.
/// "No more lootable items found." only prints for a non-corpse container.
/// <para>
/// The original's own bullet list reads "skip anything that is neither
/// IsKeep nor IsKeepUpTo" as a THIRD, separate check after the salvage gate
/// — taken completely literally that would make an item classified Salvage
/// unlootable even with <c>LootSalvage</c> on, which would make the setting
/// meaningless. VTank's action flags are not mutually exclusive the way this
/// host's single-valued <see cref="PluginLootAction"/> is, so this port
/// treats "Salvage, with LootSalvage on" as an accepted third action
/// alongside Keep/KeepUpTo rather than reproducing the contradiction. See
/// docs/deviations.md.
/// </para>
/// </remarks>
public sealed class Looter
{
    private static readonly TimeSpan ThinkInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StuckWindow = TimeSpan.FromSeconds(10);
    private const string StorageContainerName = "Storage";
    private static readonly string[] ChestNames = ["Chest", "Vault", "Reliquary"];

    private readonly IPluginHost _host;
    private readonly LootingSettings _settings;
    private readonly LootRuleProcessor _lootRules;
    private readonly TimeProvider _timeProvider;
    private readonly ChatOutput _chat;

    private IDisposable? _thinkRegistration;
    private Action<uint>? _onContainerOpened;
    private readonly HashSet<string> _blacklistedNames = new(StringComparer.Ordinal);
    private uint _workingItemId;
    private DateTime _workingSinceUtc;
    private bool _active;
    private bool _isMyCorpse;
    private bool _isCorpse;

    public Looter(
        IPluginHost host,
        ChatOutput chat,
        LootingSettings settings,
        LootRuleProcessor lootRules,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lootRules);
        _host = host;
        _chat = chat;
        _settings = settings;
        _lootRules = lootRules;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Start(TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_thinkRegistration is not null)
            return;

        _onContainerOpened = OnContainerOpened;
        _host.Events.ContainerOpened += _onContainerOpened;

        _thinkRegistration = scheduler.Every(ThinkInterval, Think);
    }

    public void Stop()
    {
        _thinkRegistration?.Dispose();
        _thinkRegistration = null;

        if (_onContainerOpened is not null)
            _host.Events.ContainerOpened -= _onContainerOpened;
        _onContainerOpened = null;

        ResetRun();
    }

    private void OnContainerOpened(uint containerObjectId)
    {
        if (!_host.Automation.Objects.TryGet(containerObjectId, out PluginWorldObject container))
            return;
        if (string.Equals(container.Name, StorageContainerName, StringComparison.Ordinal))
            return;

        bool isCorpse = container.ObjectClass == PluginObjectClass.Corpse;
        string myCorpseName = "Corpse of " + _host.Automation.Character.Name;
        bool isMyCorpse = isCorpse
            && string.Equals(container.Name, myCorpseName, StringComparison.Ordinal);

        bool shouldStart =
            (isCorpse && _settings.AutoLootCorpses.Value)
            || (isMyCorpse && _settings.AutoLootMyCorpses.Value)
            || (!isCorpse && _settings.AutoLootChests.Value && ContainsChestName(container.Name));

        if (!shouldStart)
            return;

        _isCorpse = isCorpse;
        _isMyCorpse = isMyCorpse && _settings.AutoLootMyCorpses.Value;
        _blacklistedNames.Clear();
        _workingItemId = 0u;
        _active = true;
    }

    private static bool ContainsChestName(string name)
    {
        foreach (string candidate in ChestNames)
        {
            if (name.Contains(candidate, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void ResetRun()
    {
        _active = false;
        _isCorpse = false;
        _isMyCorpse = false;
        _blacklistedNames.Clear();
        _workingItemId = 0u;
    }

    private void Think()
    {
        if (!_active)
            return;
        if (_host.Automation.Objects.OpenContainerObjectId == 0u)
        {
            ResetRun();
            return;
        }

        IReadOnlyList<PluginInventoryItem> contents = _host.Automation.Loot.CaptureCurrentContents();
        if (contents.Count == 0)
            return;

        if (_host.Automation.Loot.IsBusy)
            return;

        // One id request per think, for the first item that still needs one
        // -- H1: this used to `return` right after issuing that request,
        // which meant a single item that kept coming back Refused/Busy (or
        // simply never got appraised) stalled looting of every OTHER,
        // already-appraised item in the container forever. It now falls
        // through to PickNext below in the same tick, which already skips
        // anything still needing identification on its own (upstream's
        // `waitingForIds` equivalent) -- so identification and looting make
        // progress in parallel instead of the id pass gating the loot pass.
        if (!_isMyCorpse)
        {
            foreach (PluginInventoryItem item in contents)
            {
                if (!NeedsIdentification(item))
                    continue;

                _host.Automation.Loot.Identify(item.ObjectId);
                break;
            }
        }

        PluginInventoryItem? pick = PickNext(contents);
        if (pick is null)
        {
            _workingItemId = 0u;
            if (!_isCorpse)
                _chat.Write("No more lootable items found.");
            ResetRun();
            return;
        }

        PluginInventoryItem picked = pick.Value;

        if (picked.ObjectId != _workingItemId)
        {
            _workingItemId = picked.ObjectId;
            _workingSinceUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }
        else if (_timeProvider.GetUtcNow().UtcDateTime - _workingSinceUtc > StuckWindow)
        {
            _chat.Write("Blacklisting item: " + picked.ObjectId + ", " + picked.Name);
            _blacklistedNames.Add(picked.Name);
            _workingItemId = 0u;
            return;
        }

        _host.Automation.Loot.Pickup(picked.ObjectId);
    }

    private bool NeedsIdentification(PluginInventoryItem item)
    {
        _host.Automation.Loot.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties);
        var context = new PluginLootClassificationContext(
            item, properties, Array.Empty<PluginInventoryItem>());
        return _lootRules.NeedsIdentification(in context);
    }

    private PluginInventoryItem? PickNext(IReadOnlyList<PluginInventoryItem> contents)
    {
        foreach (PluginInventoryItem item in contents)
        {
            if (_blacklistedNames.Contains(item.Name))
                continue;

            if (_isMyCorpse)
                return item;

            if (NeedsIdentification(item))
                continue;

            if (!TryClassify(item, out PluginLootClassification classification))
                continue;
            if (classification.Action == PluginLootAction.NoLoot)
                continue;
            if (classification.Action == PluginLootAction.Salvage && !_settings.LootSalvage.Value)
                continue;
            if (classification.Action != PluginLootAction.Keep
                && classification.Action != PluginLootAction.KeepUpTo
                && classification.Action != PluginLootAction.Salvage)
                continue;

            if (classification.Action == PluginLootAction.KeepUpTo)
            {
                int have = CountOwnedForKeepUpTo(
                    classification.RuleName, classification.KeepCount, item.Name);
                if (have >= classification.KeepCount)
                    continue;
            }

            return item;
        }

        return null;
    }

    private int CountOwnedForKeepUpTo(string ruleName, int keepCount, string itemName)
    {
        int total = 0;
        foreach (PluginInventoryItem owned in _host.Automation.Items.CaptureOwnedItems())
        {
            if (!string.Equals(owned.Name, itemName, StringComparison.Ordinal))
                continue;

            _host.Automation.Items.TryCaptureProperties(owned.ObjectId, out PluginItemProperties properties);
            var context = new PluginLootClassificationContext(
                owned, properties, Array.Empty<PluginInventoryItem>());
            if (!_lootRules.TryClassifyLive(in context, out PluginLootClassification classification))
                continue;
            if (classification.Action != PluginLootAction.KeepUpTo)
                continue;
            if (classification.KeepCount != keepCount
                || !string.Equals(classification.RuleName, ruleName, StringComparison.Ordinal))
                continue;

            total += owned.StackSize > 0 ? owned.StackSize : 1;
        }

        return total;
    }

    private bool TryClassify(PluginInventoryItem item, out PluginLootClassification classification)
    {
        _host.Automation.Loot.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties);
        var context = new PluginLootClassificationContext(
            item, properties, Array.Empty<PluginInventoryItem>());
        return _lootRules.TryClassifyLive(in context, out classification);
    }
}
