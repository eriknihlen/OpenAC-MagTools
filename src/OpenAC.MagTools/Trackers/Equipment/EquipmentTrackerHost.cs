using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Chat;
using ChatClassifier = OpenAC.MagTools.Chat.ChatClassifier;

namespace OpenAC.MagTools.Trackers.Equipment;

/// <summary>
/// Lifecycle owner for §2.4's equipment/mana tracker: rebuilds the tracked
/// set from scratch on login, resyncs it against every subsequent
/// <see cref="IEvents.ObjectChanged"/>, and issues an id request for a newly
/// tracked item exactly like the original's <c>Actions.RequestId(id)</c>.
/// </summary>
/// <remarks>
/// The original distinguished <c>CreateObject</c> (add) from a
/// <c>ChangeObject</c> carrying a "StorageChange" flag (add-or-remove) from
/// <c>ReleaseObject</c> (remove) — three distinct wire-level signals. The host
/// contract here only distinguishes Created/Updated/IdentReceived/Moved/Released,
/// with no "did the equip slot change" flag, so this host resyncs the WHOLE
/// equipped set (a handful of items, at most) against
/// <c>Items.CaptureOwnedItems()</c> instead of diffing a single object. The
/// observable outcome — equip and unequip both update the tracked set
/// promptly — matches; the three-event dance itself does not. See
/// docs/deviations.md.
/// </remarks>
public sealed class EquipmentTrackerHost
{
    private static readonly TimeSpan ResyncInterval = TimeSpan.FromMilliseconds(500);

    private readonly IPluginHost _host;
    private readonly TimeProvider _timeProvider;
    private readonly ChatClassificationDispatcher? _chatDispatcher;
    private Action<PluginObjectChange>? _onObjectChanged;
    private Action<PluginChatMessage, ChatClassifier.ChatLine>? _onClassified;
    private Action<PluginChatMessage>? _onReceived;
    private IDisposable? _timerRegistration;
    private TickScheduler? _scheduler;
    private bool _running;
    private bool _dirty;

    /// <param name="chatDispatcher">
    /// The shared classify-once fan-out (see
    /// <see cref="ChatClassificationDispatcher"/>) — pass the same instance
    /// every other chat-driven consumer uses so a line is classified once per
    /// delivery. Optional so a caller (or a test) with no dispatcher still
    /// gets correct, self-contained M5 re-ident behavior by subscribing to
    /// the raw feed directly.
    /// </param>
    public EquipmentTrackerHost(
        IPluginHost host, TimeProvider? timeProvider = null, ChatClassificationDispatcher? chatDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _chatDispatcher = chatDispatcher;
        Tracker = new EquipmentTracker(_timeProvider);
    }

    public EquipmentTracker Tracker { get; }

    /// <summary>
    /// H3: <paramref name="scheduler"/> is optional so existing callers (and
    /// tests) that never pass one keep the pre-H3 per-event resync — but the
    /// composition root always passes the plugin's shared scheduler, which
    /// coalesces a whole burst of <see cref="IEvents.ObjectChanged"/> events
    /// into one <c>CaptureOwnedItems()</c> capture per 500 ms tick instead of
    /// one per event. See docs/deviations.md.
    /// </summary>
    public void Start(TickScheduler? scheduler = null)
    {
        if (_running)
            return;

        _running = true;
        _scheduler = scheduler;
        Resync(identifyNewItems: true);

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;

        if (_chatDispatcher is not null)
        {
            _onClassified = (message, _) => OnChatMessage(message);
            _chatDispatcher.Classified += _onClassified;
        }
        else
        {
            _onReceived = OnChatMessage;
            _host.Automation.Chat.Received += _onReceived;
        }

        if (_scheduler is not null)
            _timerRegistration = _scheduler.Every(ResyncInterval, OnTick);
    }

    public void Stop()
    {
        if (!_running)
            return;

        _running = false;
        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;

        if (_onClassified is not null && _chatDispatcher is not null)
            _chatDispatcher.Classified -= _onClassified;
        _onClassified = null;

        if (_onReceived is not null)
            _host.Automation.Chat.Received -= _onReceived;
        _onReceived = null;

        _timerRegistration?.Dispose();
        _timerRegistration = null;
        _scheduler = null;
        _dirty = false;

        Tracker.Clear();
    }

    private void OnObjectChanged(PluginObjectChange change)
    {
        if (change.Kind == PluginObjectChangeKind.IdentReceived)
        {
            foreach (EquipmentTrackedItem item in Tracker.Items)
            {
                if (item.ObjectId != change.ObjectId)
                    continue;
                if (_host.Automation.Items.TryCaptureProperties(
                        change.ObjectId, out PluginItemProperties properties))
                {
                    item.OnIdentReceived(properties, _timeProvider.GetUtcNow().UtcDateTime);
                }

                break;
            }
        }

        // No scheduler was supplied (a caller that wants the old
        // synchronous-per-event behavior, or most tests): resync right now,
        // same as before H3.
        if (_scheduler is null)
        {
            Resync(identifyNewItems: true);
            return;
        }

        // H3: coalesce — a whole burst of events inside one 500 ms tick
        // costs exactly one CaptureOwnedItems() capture, not one per event.
        _dirty = true;
    }

    private void OnTick()
    {
        if (!_dirty)
            return;

        _dirty = false;
        Resync(identifyNewItems: true);
    }

    /// <summary>
    /// M5: the original's <c>EquipmentTrackedItem.Current_ChatBoxMessage</c>
    /// re-requested an id for the item named in a "The Mana Stone gives "
    /// gift line or a "is low on Mana."/"is out of Mana." warning line — both
    /// only reachable by watching every delivered chat line, since neither is
    /// a wire-level ObjectChanged event. Ported here against the shared
    /// classify-once dispatcher instead of each tracked item subscribing to
    /// the raw feed independently (the original had one Decal
    /// <c>ChatBoxMessage</c> handler per tracked item — every line was
    /// filtered once per equipped item instead of once total). See
    /// docs/deviations.md.
    /// </summary>
    private void OnChatMessage(PluginChatMessage message)
    {
        string text = message.Text;
        if (string.IsNullOrEmpty(text))
            return;

        // The original's own guard against matching an item's name inside
        // someone's spoken sentence.
        if (text.StartsWith("You say, ", StringComparison.Ordinal)
            || text.Contains("says, \"", StringComparison.Ordinal))
        {
            return;
        }

        bool manaStoneGift = text.Contains("The Mana Stone gives ", StringComparison.Ordinal);
        bool lowOrOutOfMana = text.Contains("Your ", StringComparison.Ordinal)
            && (text.Contains(" is low on Mana.", StringComparison.Ordinal)
                || text.Contains(" is out of Mana.", StringComparison.Ordinal));

        if (!manaStoneGift && !lowOrOutOfMana)
            return;

        foreach (EquipmentTrackedItem item in Tracker.Items)
        {
            string name = item.Item.Name;
            if (!string.IsNullOrEmpty(name) && text.Contains(name, StringComparison.Ordinal))
                _host.Automation.Objects.Identify(item.ObjectId);
        }
    }

    private void Resync(bool identifyNewItems)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<PluginInventoryItem> owned = _host.Automation.Items.CaptureOwnedItems();

        var equippedIds = new HashSet<uint>();
        foreach (PluginInventoryItem item in owned)
        {
            if (!item.IsEquipped)
                continue;

            equippedIds.Add(item.ObjectId);

            bool isNew = !TryFind(item.ObjectId, out _);
            EquipmentTrackedItem tracked = Tracker.GetOrAdd(item.ObjectId);

            _host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject world);
            tracked.UpdateSnapshot(item, world, now);

            if (isNew && identifyNewItems)
                _host.Automation.Objects.Identify(item.ObjectId);
        }

        List<uint>? toRemove = null;
        foreach (EquipmentTrackedItem tracked in Tracker.Items)
        {
            if (!equippedIds.Contains(tracked.ObjectId))
                (toRemove ??= []).Add(tracked.ObjectId);
        }

        if (toRemove is not null)
        {
            foreach (uint objectId in toRemove)
                Tracker.Remove(objectId);
        }

        Tracker.RaiseChanged();
    }

    private bool TryFind(uint objectId, out EquipmentTrackedItem? item)
    {
        foreach (EquipmentTrackedItem candidate in Tracker.Items)
        {
            if (candidate.ObjectId != objectId)
                continue;
            item = candidate;
            return true;
        }

        item = null;
        return false;
    }
}
