using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.ItemInfo;
using OpenAC.MagTools.Settings;

namespace OpenAC.MagTools.Loggers.Inventory;

/// <summary>
/// Ports <c>Inventory/InventoryLogger.cs</c>: on login, request id data for
/// every ident-worthy owned item that lacks it (only when the file doesn't
/// exist yet), otherwise dump immediately; keep requesting ids for newly
/// arrived items thereafter; dump again on logoff. The dump merges with the
/// previous file via <see cref="MyWorldObjectRecord.Combine"/>.
/// </summary>
public sealed class InventoryLogger
{
    /// <summary>
    /// How often the post-startup snapshot poll runs (HIGH-3, P10 review):
    /// once per second through the plugin's one <see cref="TickScheduler"/>
    /// clock, instead of a raw <see cref="IEvents.Tick"/> subscription
    /// driving a full <see cref="IItemAutomation.CaptureOwnedItems"/> walk
    /// (object-table walk + per-item projection/allocation + sort) every
    /// single frame.
    /// </summary>
    private static readonly TimeSpan SnapshotPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many 1-second polls to wait for the owned pack to stream in
    /// before giving up on the startup capture entirely (HIGH-3) -- about a
    /// minute. If the pack never populates, nothing was ever captured and
    /// <see cref="Stop"/> will not write anything either (HIGH-2).
    /// </summary>
    private const int StartupGiveUpPolls = 60;

    /// <summary>
    /// How long (in <see cref="TickScheduler.ElapsedSeconds"/>) a single id
    /// may keep coming back non-accepted from
    /// <see cref="IWorldObjectAutomation.Identify"/> before
    /// <see cref="PumpIdentifyQueue"/> gives up on it and moves on to the
    /// next queued one. HIGH-A (P13 review): this bound is TEMPORAL, not a
    /// count of refused attempts -- an event-count budget was found live to
    /// be exhausted by unrelated <c>Created</c>/<c>IdentReceived</c> traffic
    /// (a login stream, a landblock crossing, a crowded town) within a
    /// single frame, giving up on a perfectly good item after roughly 11
    /// unrelated world registrations. Mirrors
    /// <c>InventoryExporter</c>'s own bounded retry-then-give-up cadence
    /// (defect 10, ~15 s there) -- the original relied on Decal's own
    /// queued <c>RequestId</c>; OpenAC has no equivalent queue, so this
    /// class provides its own.
    /// </summary>
    private const double MaxIdentifyRefusalSeconds = 30d;

    private readonly IPluginHost _host;
    private readonly ChatOutput _chat;
    private readonly InventoryManagementSettings _settings;

    /// <summary>
    /// Ids for which <see cref="IWorldObjectAutomation.Identify"/> was
    /// actually ACCEPTED (HIGH-1, P12 review: never populated merely because
    /// a request was attempted -- only once the host's own result confirms
    /// it was sent). Also used by <see cref="EnqueueIdentify"/> as a dedup
    /// guard so an already-sent id is never re-queued.
    /// </summary>
    private readonly HashSet<uint> _requestedIds = [];

    /// <summary>
    /// Ids still waiting for a paced <see cref="IWorldObjectAutomation.Identify"/>
    /// attempt (HIGH-1, P12 review). The host allows exactly one inventory
    /// transaction in flight at a time for ANY plugin/UI caller, so this
    /// class must issue identify requests one at a time rather than in a
    /// burst -- see <see cref="PumpIdentifyQueue"/>.
    /// </summary>
    private readonly Queue<uint> _pendingIdentifyIds = new();

    /// <summary>Membership mirror of <see cref="_pendingIdentifyIds"/> for O(1) dedup in <see cref="EnqueueIdentify"/>.</summary>
    private readonly HashSet<uint> _queuedIdentifyIds = [];

    /// <summary>
    /// Per-id timestamp (<see cref="TickScheduler.ElapsedSeconds"/>) of the
    /// FIRST non-accepted <see cref="IWorldObjectAutomation.Identify"/>
    /// response seen for that id, bounded by
    /// <see cref="MaxIdentifyRefusalSeconds"/> (HIGH-A, P13 review: time,
    /// not an event count -- see that constant's remarks).
    /// </summary>
    private readonly Dictionary<uint, double> _identifyFirstRefusalSeconds = [];

    /// <summary>
    /// The id this class most recently sent an ACCEPTED
    /// <see cref="IWorldObjectAutomation.Identify"/> for and is still
    /// awaiting a response for -- 0 means none. HIGH-A (P13 review, part 1):
    /// <see cref="PumpIdentifyQueue"/> must not attempt ANYTHING while this
    /// is set, because the host's single-in-flight-request gate
    /// (<c>InventoryTransactionState.CanBeginRequest</c>) guarantees every
    /// such attempt comes back <c>Busy</c> -- and the host raises
    /// <c>Created</c> for EVERY world entity (not just owned/ident-worthy
    /// ones), so a login stream/landblock crossing/crowded town previously
    /// pumped dozens of times per frame while THIS class's own just-sent
    /// identify was still outstanding, debiting the front queued id's
    /// retry budget for events that had nothing to do with it. Cleared on
    /// this same id's <see cref="PluginObjectChangeKind.IdentReceived"/>,
    /// or after <see cref="MaxIdentifyRefusalSeconds"/> as a safety valve
    /// in case that completion signal is ever lost.
    /// </summary>
    private uint _inFlightIdentifyId;

    /// <summary><see cref="TickScheduler.ElapsedSeconds"/> when <see cref="_inFlightIdentifyId"/> was sent.</summary>
    private double _inFlightIdentifySentAtSeconds;

    /// <summary>
    /// The plugin's one <see cref="TickScheduler"/> clock, stored (HIGH-A,
    /// P13 review) so <see cref="PumpIdentifyQueue"/> can time-bound both
    /// the in-flight safety valve and the per-id give-up against real
    /// elapsed time rather than an event count.
    /// </summary>
    private TickScheduler? _scheduler;

    private Action<PluginObjectChange>? _onObjectChanged;
    private string _storageKey = string.Empty;
    private bool _waitingForIdData;
    private bool _running;
    private IDisposable? _snapshotPoll;
    private bool _startupCaptureDone;
    private int _startupPollCount;

    /// <summary>
    /// The set of ident-worthy-and-identified item ids as of the last
    /// partial dump taken while <see cref="_waitingForIdData"/> is set
    /// (defect 8b). <see langword="null"/> means no partial dump has run
    /// yet this wait. Guards <see cref="OnSnapshotPoll"/>'s per-second
    /// re-dump so it only touches storage when the identified SET actually
    /// changed, not merely its count -- LOW-1 (P12 review): a count alone
    /// misses the case where one item leaves the owned set (sold, given
    /// away) in the same tick another gets identified, netting no count
    /// change even though the dump content did change. Reset to
    /// <see langword="null"/> whenever the wait ends (both in
    /// <see cref="Start"/> and once <see cref="OnObjectChanged"/>'s
    /// completion latch trips).
    /// </summary>
    private HashSet<uint>? _lastPartialDumpIdentifiedIds;

    /// <summary>
    /// The last non-empty owned-item snapshot this instance has actually
    /// captured. <see cref="Stop"/> dumps from this rather than a fresh
    /// <see cref="IItemAutomation.CaptureOwnedItems"/> call, because by the
    /// time logoff teardown runs the host has typically already released
    /// the owned objects -- a fresh capture there is reliably empty even
    /// though real items were logged earlier in the session (defect 8).
    /// Refreshed whenever <see cref="Dump"/> runs with real items AND on
    /// every post-startup <see cref="OnSnapshotPoll"/> tick (HIGH-1, P10
    /// review) -- not just at startup -- so it reflects anything looted,
    /// bought, or tinkered mid-session, not only the login-time inventory.
    /// </summary>
    private IReadOnlyList<PluginInventoryItem> _lastOwnedSnapshot = [];

    public InventoryLogger(IPluginHost host, ChatOutput chat, InventoryManagementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _chat = chat;
        _settings = settings;
    }

    /// <summary><c>ObjectClassNeedsIdent</c>, verbatim rule set.</summary>
    public static bool ObjectClassNeedsIdent(PluginObjectClass objectClass, string name)
    {
        if (objectClass is PluginObjectClass.Armor or PluginObjectClass.Clothing
            or PluginObjectClass.MeleeWeapon or PluginObjectClass.MissileWeapon
            or PluginObjectClass.WandStaffOrb or PluginObjectClass.Jewelry)
            return true;

        if (objectClass == PluginObjectClass.Gem && !string.IsNullOrEmpty(name)
            && name.Contains("Aetheria", StringComparison.Ordinal))
            return true;

        if (objectClass == PluginObjectClass.Misc && !string.IsNullOrEmpty(name)
            && name.Contains("Essence", StringComparison.Ordinal))
            return true;

        return false;
    }

    /// <summary>
    /// Starts the logger for a session. <paramref name="scheduler"/> is the
    /// plugin's one <see cref="TickScheduler"/> clock (HIGH-3, P10 review) --
    /// already constructed and running by the time <c>OnSessionReady</c>
    /// calls this, so every poll this class needs hangs off it rather than
    /// a raw <see cref="IEvents.Tick"/> subscription.
    /// </summary>
    public void Start(string server, string character, TickScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_running)
            return;
        _running = true;
        _scheduler = scheduler;
        _requestedIds.Clear();
        _pendingIdentifyIds.Clear();
        _queuedIdentifyIds.Clear();
        _identifyFirstRefusalSeconds.Clear();
        _inFlightIdentifyId = 0u;
        _inFlightIdentifySentAtSeconds = 0d;
        _waitingForIdData = false;
        _storageKey = server + "/" + character + ".Inventory.xml";
        _lastOwnedSnapshot = [];
        _startupCaptureDone = false;
        _startupPollCount = 0;
        _lastPartialDumpIdentifiedIds = null;

        if (!_settings.InventoryLogger.Value)
        {
            _onObjectChanged = OnObjectChanged;
            _host.Events.ObjectChanged += _onObjectChanged;
            return;
        }

        _onObjectChanged = OnObjectChanged;
        _host.Events.ObjectChanged += _onObjectChanged;

        // At SessionReady time the owned pack has not necessarily streamed
        // in yet (defect 8); if it's already populated (the common case --
        // e.g. a reconnect where the character was already fully in-world)
        // this runs immediately, exactly like the original.
        IReadOnlyList<PluginInventoryItem> items = _host.Automation.Items.CaptureOwnedItems();
        if (items.Count > 0)
        {
            _startupCaptureDone = true;
            RunStartupCapture(items);
        }

        // HIGH-3: poll through the plugin's one TickScheduler clock at 1 Hz
        // instead of a raw Events.Tick subscription driving a full
        // CaptureOwnedItems() walk every frame. Before the startup capture
        // succeeds this also counts toward a bounded give-up
        // (StartupGiveUpPolls); once it has succeeded, the SAME poll keeps
        // _lastOwnedSnapshot fresh for the rest of the session (HIGH-1).
        _snapshotPoll = scheduler.Every(SnapshotPollInterval, OnSnapshotPoll);
    }

    private void OnSnapshotPoll()
    {
        IReadOnlyList<PluginInventoryItem> items = _host.Automation.Items.CaptureOwnedItems();

        if (items.Count == 0)
        {
            if (_startupCaptureDone)
                return;

            _startupPollCount++;
            if (_startupPollCount < StartupGiveUpPolls)
                return;

            // Bounded give-up: the pack never populated within the wait
            // window. Nothing was ever captured, so Stop() will not write
            // anything either (HIGH-2). MEDIUM-B (P10 re-review): dispose
            // the poll itself here too, not just latch a flag -- otherwise
            // a LATER non-empty capture (the pack finally streams in after
            // all, well past the give-up) would still fall through to the
            // HIGH-1 refresh below and let Stop() dump a document with no
            // id requests ever made and no "Requesting id information..."
            // line, contradicting this very warning and the matching
            // docs/deviations.md row ("nothing is dumped that session").
            _startupCaptureDone = true;
            _host.Log.Warn(
                "InventoryLogger: the owned pack never populated within the "
                + StartupGiveUpPolls + "-second startup wait window; nothing "
                + "was captured this session.");
            _snapshotPoll?.Dispose();
            _snapshotPoll = null;
            return;
        }

        if (!_startupCaptureDone)
        {
            _startupCaptureDone = true;
            RunStartupCapture(items);
            return;
        }

        // HIGH-1: keep the snapshot fresh for the rest of the session, so a
        // later Stop() -- which typically runs after the host has already
        // torn the owned objects down -- dumps what was actually
        // looted/bought/tinkered mid-session, not just the login-time
        // snapshot.
        //
        // MEDIUM-A (P10 re-review): a poll must not replace a good
        // snapshot with one taken mid logoff-teardown. Host ordering,
        // checked under OpenAcRoot:
        //   - Character-logoff-to-select-screen path
        //     (LiveSessionController.CompleteCharacterLogOffCore,
        //     src/AcDream.Runtime/Session/LiveSessionController.cs:1213)
        //     calls host.ResetSessionState -> RuntimeGenerationReset.Reset,
        //     whose Drain() loop (src/AcDream.Runtime/
        //     RuntimeGenerationReset.cs:259) runs synchronously to
        //     RuntimeGenerationResetStage.Complete with no yield point --
        //     BEFORE _inWorld is set false two lines later, at
        //     LiveSessionController.cs:1219. Teardown is atomic and fully
        //     converged before the plugin's Logoff event can even fire.
        //   - Full disconnect/quit path (LiveSessionController.StopCore,
        //     same file, line 1751) sets _inWorld = false IMMEDIATELY and
        //     performs NO entity/inventory reset in that call at all --
        //     that reset is deferred to the NEXT session's StartCore
        //     (ResetHostBeforeStart, line 1817), which runs long after
        //     this plugin's Stop() (and its _snapshotPoll.Dispose()) has
        //     already executed.
        // So a PARTIAL capture is never actually observable in either
        // path -- the reset is all-or-nothing. What CAN happen is a poll
        // landing in the narrow gap where IsInWorld has already flipped
        // false but this plugin's own Logoff handler has not yet run
        // (both paths above set _inWorld before anything downstream
        // reacts to it). AppAutomationSurface.IsAvailable
        // (src/AcDream.App/Plugins/AppAutomationSurface.cs:145), which
        // ICharacterInfo.IsInWorld is wired to, reads
        // runtime.Lifecycle.State LIVE on every call rather than caching
        // the Logoff event -- a truthful "still fully in-world right now"
        // signal independent of whether Logoff has fired yet. Gating the
        // refresh on it means this poll never captures that gap's data.
        if (!_host.Automation.Character.IsInWorld)
            return;

        _lastOwnedSnapshot = items;

        // HIGH-1 (P12 review): pace the identify queue -- see
        // PumpIdentifyQueue's own remarks for the corrected root-cause
        // explanation (the host's single-in-flight-request Busy gate, not a
        // "displaced awaiting slot"). Runs regardless of _waitingForIdData:
        // Dump's own requestIdsIfMissing path can also enqueue ids (an
        // existing-file session's re-identify pass) without setting that
        // flag.
        if (_pendingIdentifyIds.Count > 0)
            PumpIdentifyQueue();

        // Defect 8b: the only other real-data dump this class ever wrote
        // was OnObjectChanged's "every ident-worthy item now has id data"
        // completion branch -- an all-or-nothing latch that needs literally
        // every item to appraise successfully. Live evidence (round 4)
        // showed 40 of 191 items never got id data in a session (out of
        // range, sold, or tinkered away -- not, as an earlier version of
        // this comment claimed, a "displaced awaiting slot": the actual
        // mechanism, corrected at P12 review, is the host's single
        // in-flight-request Busy gate documented on
        // MaxIdentifyAttemptsPerItem/PumpIdentifyQueue). With that latch
        // never tripping, NOTHING real ever reached Storage before logoff,
        // so Stop()'s dump -- running after the host has torn down the
        // owned objects -- found every item unresolved and wrote
        // HasIdData=false for all of them, even though CaptureOwnedItems()/
        // TryGet had shown real appraisal data for most items earlier in
        // the very same session.
        //
        // The fix: while waiting, persist partial progress here too, once
        // per second, independent of whether the all-or-nothing latch ever
        // trips. This does not print the completion line or clear
        // _waitingForIdData -- it just gives Stop()'s post-teardown Combine
        // a real `previous` (read back from Storage) to merge against
        // instead of an empty one. Guarded on the identified-id SET
        // actually changing (LOW-1, P12 review: a count alone misses an
        // item leaving the owned set the same tick another gets identified)
        // so an unmet wait does not rewrite the file every tick for the
        // rest of the session.
        if (_waitingForIdData)
        {
            var identifiedIds = new HashSet<uint>(items
                .Where(item => ObjectClassNeedsIdent(item.ObjectClass, item.Name) && HasIdData(item))
                .Select(item => item.ObjectId));
            if (_lastPartialDumpIdentifiedIds is null || !identifiedIds.SetEquals(_lastPartialDumpIdentifiedIds))
            {
                _lastPartialDumpIdentifiedIds = identifiedIds;
                Dump(requestIdsIfMissing: false, items);
            }
        }
    }

    /// <summary>
    /// HIGH-1 (P12 review, defect 8b root cause corrected): the host's
    /// <see cref="IWorldObjectAutomation.Identify"/>
    /// (<c>AppAutomationSurface.Identify</c>) refuses -- <c>Busy</c>, with
    /// NO call to the underlying appraisal request at all -- every identify
    /// attempt made while ANY inventory transaction is already in flight
    /// (<c>InventoryTransactionState.CanBeginRequest =&gt; _busyCount == 0
    /// &amp;&amp; ...</c>, a client-wide gate shared with Use/Move/etc, not
    /// specific to appraisal). A burst <c>foreach</c> over many items
    /// previously called <c>Identify</c> for every one of them in the same
    /// tick; only the FIRST could ever be accepted, and the remaining N-1
    /// were silently refused -- while the old code still added every one of
    /// them to <see cref="_requestedIds"/> regardless, poisoning
    /// <see cref="OnObjectChanged"/>'s own per-item dedup guard so they
    /// could never be retried either.
    /// <para>
    /// HIGH-A (P13 review): the P12 fix still pumped once per
    /// <c>Created</c>/<c>IdentReceived</c> event, and the host raises
    /// <c>Created</c> for EVERY world entity, not just owned/ident-worthy
    /// ones (<c>AppAutomationSurface.cs:1153-1168</c>). During a login
    /// stream/landblock crossing/crowded town this pumped many times per
    /// frame while THIS class's own just-accepted identify was still
    /// outstanding -- every one of those extra attempts came back
    /// <c>Busy</c> purely because of that self-inflicted contention, and an
    /// event-COUNT give-up (10 refusals) meant a real item was abandoned
    /// after roughly 11 unrelated world registrations. Two fixes, both
    /// required: (1) this method now refuses to attempt anything at all
    /// while <see cref="_inFlightIdentifyId"/> is set -- see that field's
    /// remarks; (2) the give-up bound is now temporal
    /// (<see cref="MaxIdentifyRefusalSeconds"/>), not a count, so a burst of
    /// same-instant events cannot exhaust it. <see cref="OnObjectChanged"/>
    /// also no longer pumps on plain <c>Created</c> at all (only on
    /// <c>IdentReceived</c>, plus the 1 Hz <see cref="OnSnapshotPoll"/> tick,
    /// plus its own per-item path which only reaches a pump call AFTER the
    /// ownership check).
    /// </para>
    /// <para>
    /// This pumps exactly the front of <see cref="_pendingIdentifyIds"/>
    /// once per call (skipping past any front id(s) already appraised by
    /// some other path first -- LOW-C, P13 review): on an accepted result it
    /// is popped and (only now) recorded into <see cref="_requestedIds"/>
    /// and latched as <see cref="_inFlightIdentifyId"/>; on anything else it
    /// is left at the front to retry on the next pump once
    /// <see cref="MaxIdentifyRefusalSeconds"/> has not yet elapsed since the
    /// first refusal seen for it.
    /// </para>
    /// </summary>
    private void PumpIdentifyQueue()
    {
        double now = _scheduler?.ElapsedSeconds ?? 0d;

        // HIGH-A part 1: never attempt anything while our own identify is
        // still outstanding -- the host's single-in-flight-request gate
        // guarantees every such attempt is refused, for reasons that have
        // nothing to do with the front queued id's own retry budget. A
        // temporal safety valve (the same bound as the give-up below)
        // covers a lost/never-arriving IdentReceived so this cannot wedge
        // the whole queue forever.
        if (_inFlightIdentifyId != 0u)
        {
            if (now - _inFlightIdentifySentAtSeconds < MaxIdentifyRefusalSeconds)
                return;
            _inFlightIdentifyId = 0u;
        }

        // LOW-C (P13 review): skip past every front id that already has id
        // data by some other path (a manual assess, a Combine from a
        // previous session's file, ...) to the first ACTIONABLE one, rather
        // than handling only the very front slot and leaving satisfied ids
        // sitting in the queue until their own turn.
        while (_pendingIdentifyIds.Count > 0)
        {
            uint front = _pendingIdentifyIds.Peek();
            if (!_host.Automation.Objects.TryGet(front, out PluginWorldObject wo) || !wo.HasAppraisalData)
                break;
            DequeueIdentify(front);
        }

        if (_pendingIdentifyIds.Count == 0)
            return;

        uint id = _pendingIdentifyIds.Peek();
        PluginItemCommandResult result = _host.Automation.Objects.Identify(id);
        if (result.Accepted)
        {
            DequeueIdentify(id);
            _requestedIds.Add(id);
            _inFlightIdentifyId = id;
            _inFlightIdentifySentAtSeconds = now;
            return;
        }

        // HIGH-A part 2: give up on TIME elapsed since the FIRST refusal
        // for this id, not on how many refusals were observed -- see
        // MaxIdentifyRefusalSeconds' remarks.
        if (!_identifyFirstRefusalSeconds.TryGetValue(id, out double firstRefusal))
        {
            _identifyFirstRefusalSeconds[id] = now;
            return;
        }

        if (now - firstRefusal >= MaxIdentifyRefusalSeconds)
        {
            DequeueIdentify(id);
            _host.Log.Warn(
                "InventoryLogger: gave up requesting id data for object 0x"
                + id.ToString("X8") + " after " + MaxIdentifyRefusalSeconds
                + "s of continuous refusal.");
        }
    }

    /// <summary>Enqueues <paramref name="objectId"/> for a paced identify attempt, unless it is already queued or already sent.</summary>
    private void EnqueueIdentify(uint objectId)
    {
        if (_requestedIds.Contains(objectId) || _queuedIdentifyIds.Contains(objectId))
            return;
        _queuedIdentifyIds.Add(objectId);
        _pendingIdentifyIds.Enqueue(objectId);
    }

    private void DequeueIdentify(uint objectId)
    {
        _pendingIdentifyIds.Dequeue();
        _queuedIdentifyIds.Remove(objectId);
        _identifyFirstRefusalSeconds.Remove(objectId);
    }

    private void RunStartupCapture(IReadOnlyList<PluginInventoryItem> items)
    {
        _lastOwnedSnapshot = items;

        if (_host.Storage.ReadText(_storageKey) is null)
        {
            _chat.Write("Requesting id information for all armor/weapon inventory. This will take a few minutes...");

            // HIGH-1 (P12 review): enqueue every missing id rather than
            // calling Identify for all of them in one burst -- see
            // PumpIdentifyQueue's remarks for why a burst only ever sends
            // the first one. EnqueueIdentify itself is the dedup guard a
            // later Created/IdentReceived delivery for this SAME
            // still-unappraised item needs (LOW-D, P10 re-review) --
            // _requestedIds only gains the id once PumpIdentifyQueue's own
            // Identify call is actually accepted.
            bool anyMissing = false;
            foreach (PluginInventoryItem item in items)
            {
                if (!HasIdData(item) && ObjectClassNeedsIdent(item.ObjectClass, item.Name))
                {
                    EnqueueIdentify(item.ObjectId);
                    anyMissing = true;
                }
            }

            if (anyMissing)
            {
                _waitingForIdData = true;
                // Kick off the first paced attempt immediately instead of
                // waiting up to a full second for the next poll tick.
                PumpIdentifyQueue();
            }
            else
            {
                // Nothing to identify (everything ident-worthy already has
                // id data) -- dump now rather than waiting forever for an
                // IdentReceived that will never come.
                Dump(requestIdsIfMissing: false, items);
            }
        }
        else
        {
            Dump(requestIdsIfMissing: true, items);
        }
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;
        _snapshotPoll?.Dispose();
        _snapshotPoll = null;

        // HIGH-2: skip the dump entirely when nothing was EVER captured
        // this session (an empty character, or Disable()/logoff before the
        // pack ever streamed in / the startup wait gave up) -- writing
        // Export([]) in that case would silently overwrite a perfectly
        // good pre-existing file with an empty document.
        if (_settings.InventoryLogger.Value && _lastOwnedSnapshot.Count > 0)
            Dump(requestIdsIfMissing: false, _lastOwnedSnapshot);

        if (_onObjectChanged is not null)
            _host.Events.ObjectChanged -= _onObjectChanged;
        _onObjectChanged = null;
        _requestedIds.Clear();
        _pendingIdentifyIds.Clear();
        _queuedIdentifyIds.Clear();
        _identifyFirstRefusalSeconds.Clear();
        _inFlightIdentifyId = 0u;
        _inFlightIdentifySentAtSeconds = 0d;
        _waitingForIdData = false;
        _lastPartialDumpIdentifiedIds = null;
    }

    private void OnObjectChanged(PluginObjectChange change)
    {
        if (!_settings.InventoryLogger.Value)
            return;
        // The original hooks CreateObject and ChangeObject (any change, not
        // only ident). This narrows to Created/IdentReceived because those
        // are the only PluginObjectChangeKind values that can plausibly flip
        // "needs an id request" or "just got its id" for an owned item; an
        // ordinary property Updated never does. If a real gap shows up here
        // (an item whose id-worthiness only becomes knowable on a later
        // Updated), widen this back to include it.
        if (change.Kind != PluginObjectChangeKind.Created && change.Kind != PluginObjectChangeKind.IdentReceived)
            return;

        // HIGH-A (P13 review): opportunistic pumping now happens ONLY on
        // IdentReceived, never on plain Created -- the host raises Created
        // for EVERY world entity (AppAutomationSurface.cs:1153-1168), not
        // just owned/ident-worthy ones, so pumping on it flooded the front
        // queued id with pointless Busy attempts during a login stream/
        // landblock crossing/crowded town. (The per-item path further down
        // still enqueues-and-pumps for a Created OWNED item, but only after
        // the ownership check below has already run.) An IdentReceived for
        // THIS class's own in-flight id frees PumpIdentifyQueue to move on
        // to the next queued id immediately, instead of waiting for the
        // MaxIdentifyRefusalSeconds safety-valve or the next 1 Hz
        // OnSnapshotPoll tick.
        if (change.Kind == PluginObjectChangeKind.IdentReceived)
        {
            if (change.ObjectId == _inFlightIdentifyId)
                _inFlightIdentifyId = 0u;
            if (_pendingIdentifyIds.Count > 0)
                PumpIdentifyQueue();
        }

        if (_waitingForIdData)
        {
            IReadOnlyList<PluginInventoryItem> currentItems = _host.Automation.Items.CaptureOwnedItems();
            bool allIdentified = currentItems
                .Where(item => ObjectClassNeedsIdent(item.ObjectClass, item.Name))
                .All(item => HasIdData(item));

            if (allIdentified)
            {
                _waitingForIdData = false;
                _lastPartialDumpIdentifiedIds = null;
                Dump(requestIdsIfMissing: false, currentItems);
                _chat.Write("Requesting id information for all armor/weapon inventory completed. Log file written.");
                return;
            }

            // LOW-9 (P10 review): fall through to the per-item identify path
            // below instead of returning here. An item that arrives DURING
            // the wait (e.g. looted mid-startup) was never in the original
            // request loop, and nothing else would ever request its id --
            // the wait would then hang forever, since the completeness check
            // above now also covers this new item. The per-item path below
            // (H7-ordered) requests it exactly like any other newly-seen
            // item.
        }

        // A TryGet miss means the object already left the table between the
        // event firing and this handler running (a Released can be queued
        // right behind a Created in the same delivery batch) -- there is
        // nothing to request an id for, so this silently skips rather than
        // restoring a synthetic default PluginWorldObject.
        if (!_host.Automation.Objects.TryGet(change.ObjectId, out PluginWorldObject wo))
            return;
        if (wo.HasAppraisalData || !ObjectClassNeedsIdent(wo.ObjectClass, wo.Name))
            return;
        // H7: the ownership check must run BEFORE marking the id as
        // requested. An object seen on the ground (not yet in my container)
        // must not get poisoned into _requestedIds -- otherwise once it's
        // picked up, this handler fires again for the same id but
        // _requestedIds.Add already returns false, and its id is never
        // actually requested.
        //
        // MEDIUM-5: an item equipped mid-session has ContainerObjectId ==
        // whatever it was equipped FROM (or 0), never the player -- the
        // wielding entity's id lives in WielderObjectId instead (the same
        // host-shape fact behind defect 9's IsEquippedByMe fix). Checking
        // ContainerObjectId alone silently dropped every mid-session equip.
        uint myId = _host.Automation.Character.ObjectId;
        if (wo.ContainerObjectId != myId && wo.WielderObjectId != myId)
            return;

        // HIGH-1 (P12 review): route through the paced queue instead of
        // calling Identify directly -- a burst of Created events (a stack
        // splitting into a full pack, several items looted in the same
        // delivery batch) hits the exact same host single-in-flight-Busy
        // gate PumpIdentifyQueue documents. EnqueueIdentify is the dedup
        // guard the old direct `_requestedIds.Add` check used to be (H7
        // above still applies: the ownership check runs before this).
        EnqueueIdentify(change.ObjectId);
        PumpIdentifyQueue();
    }

    /// <summary>
    /// The host's <see cref="PluginInventoryItem"/> is the ident-worthy owned
    /// snapshot; "has id data" for it is inferred the same way
    /// <see cref="ItemInfo.ItemModel"/> treats a captured properties bag: this
    /// logger asks <see cref="IWorldObjectAutomation.TryGet"/> for the
    /// matching world object's <c>HasAppraisalData</c> flag directly.
    /// </summary>
    private bool HasIdData(PluginInventoryItem item)
        => _host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject wo) && wo.HasAppraisalData;

    private void Dump(bool requestIdsIfMissing, IReadOnlyList<PluginInventoryItem> items)
    {
        // HIGH-1: refresh the last-known-good snapshot whenever this call
        // actually has real items, so a later Stop() dump (which typically
        // runs after the host has already torn the owned objects down)
        // reflects the most recently captured inventory, not whatever was
        // last captured at startup. LOW-C (P10 re-review): assign directly
        // rather than ToArray() -- IItemAutomation.CaptureOwnedItems
        // already allocates and returns a fresh list on every call
        // (AppAutomationSurface.cs:2573's `built`), so a defensive copy
        // here was a second full allocation for no correctness benefit.
        if (items.Count > 0)
            _lastOwnedSnapshot = items;

        List<MyWorldObjectRecord> previous = [];
        if (_host.Storage.ReadText(_storageKey) is { } content)
        {
            if (!InventoryLoggerXml.TryImport(content, out previous))
                _chat.Write("Inventory file is corrupt.");
        }

        var current = new List<MyWorldObjectRecord>();
        foreach (PluginInventoryItem item in items)
        {
            // An owned item the object table has no full PluginWorldObject
            // for yet (not yet resolved/appraised) is still owned and still
            // belongs in the dump -- the original always wrote every owned
            // item, with an empty id block for anything unresolved. See the
            // P9 fix round (docs/live-results.md) for the "empty document"
            // symptom this addresses. A previously-persisted record for this
            // id must survive a thin object-table dump unchanged: look it up
            // in `previous` and Combine rather than overwriting good id data
            // with an empty unresolved stub (HIGH-1).
            //
            // HIGH-A (P9 re-review): do NOT call Identify or record the id in
            // _requestedIds here. On the real host, TryGet's false branch
            // means there is no ClientObject for this id yet either, and
            // Identify requires one -- so an Identify call in THIS branch is
            // a guaranteed no-op. Worse, recording it in _requestedIds would
            // poison OnObjectChanged's `_requestedIds.Add` dedup guard (the
            // same H7 hazard documented at that method's container check
            // below): once poisoned, the item would never actually be
            // identified for the rest of the session, even after its
            // ClientObject shows up and OnObjectChanged fires for it. The
            // real request has to wait for that later, resolved-object path.
            if (!_host.Automation.Objects.TryGet(item.ObjectId, out PluginWorldObject wo))
            {
                MyWorldObjectRecord unresolved = MyWorldObjectRecord.CreateUnresolved(item);
                MyWorldObjectRecord? previousMatch = previous.FirstOrDefault(
                    prev => prev.Id == unresolved.Id && prev.ObjectClass == unresolved.ObjectClass);

                current.Add(previousMatch is null
                    ? unresolved
                    : MyWorldObjectRecord.Combine(previousMatch, unresolved));
                continue;
            }

            if (!_host.Automation.Objects.TryCaptureProperties(item.ObjectId, out PluginItemProperties properties))
                properties = ItemModel.EmptyProperties;

            MyWorldObjectRecord snapshot = MyWorldObjectRecord.Create(wo, properties);

            MyWorldObjectRecord? matched = previous.FirstOrDefault(
                prev => prev.Id == snapshot.Id && prev.ObjectClass == snapshot.ObjectClass);

            if (matched is null)
            {
                // HIGH-1 (P12 review): route through the paced queue -- this
                // loop runs once per Dump() call over every current item, so
                // calling Identify directly here is the exact same burst
                // shape RunStartupCapture's fresh-file loop had.
                if (requestIdsIfMissing && !snapshot.HasIdData && ObjectClassNeedsIdent(wo.ObjectClass, wo.Name))
                    EnqueueIdentify(snapshot.Id);
                current.Add(snapshot);
                continue;
            }

            if (requestIdsIfMissing && !matched.HasIdData && !snapshot.HasIdData
                && ObjectClassNeedsIdent(wo.ObjectClass, wo.Name))
            {
                EnqueueIdentify(snapshot.Id);
                current.Add(snapshot);
            }
            else
            {
                current.Add(MyWorldObjectRecord.Combine(matched, snapshot));
            }
        }

        // Only one attempt is paced out per Dump() call (matching every
        // other pump call site) -- the rest of anything queued here rides
        // the same 1 Hz OnSnapshotPoll/opportunistic OnObjectChanged pumps
        // as the fresh-file startup queue.
        if (requestIdsIfMissing)
            PumpIdentifyQueue();

        _host.Storage.WriteText(_storageKey, InventoryLoggerXml.Export(current));
    }
}
