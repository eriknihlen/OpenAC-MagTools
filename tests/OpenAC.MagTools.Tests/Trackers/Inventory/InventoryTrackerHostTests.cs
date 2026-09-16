using System.Linq;
using AcDream.Plugin.Abstractions;
using OpenAC.MagTools;
using OpenAC.MagTools.Tests.Fakes;
using OpenAC.MagTools.Trackers.Inventory;
using Xunit;

namespace OpenAC.MagTools.Tests.Trackers.Inventory;

/// <summary>
/// H1 (P5 fix round): <see cref="InventoryTrackerHost"/> used to prime both
/// trackers synchronously inside <see cref="InventoryTrackerHost.Start"/>,
/// before the host's post-login inventory stream had delivered anything —
/// see the class's own remarks. These tests exercise the replacement
/// priming-on-quiescence + per-tick coalescing design: nothing is captured
/// until the inventory stream (every <c>ObjectChanged</c>, not just
/// consumable/profit-relevant ones) goes quiet for a full second, and every
/// event inside one 500 ms tick coalesces to at most one
/// <c>CaptureOwnedItems()</c> call.
/// </summary>
public sealed class InventoryTrackerHostTests
{
    private static PluginInventoryItem ManaStone(uint objectId, int stackSize = 1)
        => new(
            objectId, 0u, "Mana Stone", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            stackSize, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.ManaStone,
        };

    /// <summary>An object the local player owns — a real inventory item streaming in.</summary>
    private static PluginWorldObject OwnedWorldObject(uint objectId)
        => new(objectId, 0u, "Something Owned", PluginObjectClass.Unknown, 0u, 0u, 0u) { IsOwned = true };

    /// <summary>
    /// An object the local player does NOT own — a monster spawning, another
    /// player crossing a cell boundary, a corpse decaying, etc. (MEDIUM-1).
    /// </summary>
    private static PluginWorldObject UnownedWorldObject(uint objectId)
        => new(objectId, 0u, "Something Else", PluginObjectClass.Monster, 0u, 0u, 0u) { IsOwned = false };

    [Fact]
    public void Start_does_not_prime_immediately()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 3));

        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(new TickScheduler(host.Events, new ChatOutput(host)));

        // No tick has fired yet — even a fully-populated inventory must not
        // be captured until the quiet period below has elapsed.
        Assert.Empty(inventoryHost.Consumables.Tracked);
        Assert.Equal(0, host.Automation.Items.CaptureCount);
    }

    [Fact]
    public void A_tick_before_the_quiet_period_elapses_does_not_prime()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 7));
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);

        scheduler.Tick(0.5); // half the 1 s quiet period

        Assert.Empty(inventoryHost.Consumables.Tracked);
    }

    [Fact]
    public void Priming_happens_once_the_quiet_period_elapses_with_no_activity()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 7));
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);

        scheduler.Tick(0.5);
        scheduler.Tick(0.5); // 1.0 s total — the quiet period has elapsed

        Assert.Equal(7, inventoryHost.Consumables.Tracked.First().CurrentCount);
        Assert.Equal(1, host.Automation.Items.CaptureCount);
    }

    [Fact]
    public void An_ObjectChanged_burst_during_login_delays_priming_until_it_goes_quiet()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);

        // A burst of OWNED-object activity (real inventory items streaming
        // in) keeps resetting the quiet clock — priming must not happen
        // while it is still going.
        for (int i = 0; i < 3; i++)
        {
            uint id = (uint)(100 + i);
            host.Automation.Objects.Objects.Add(OwnedWorldObject(id));
            scheduler.Tick(0.5);
            host.Events.RaiseObjectChanged(id, PluginObjectChangeKind.Created);
        }

        Assert.Equal(0, host.Automation.Items.CaptureCount);

        // The burst goes quiet: the full inventory (added only now, exactly
        // like the async post-login stream) is captured as ONE clean
        // baseline instead of a synthetic 0->V ramp.
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 40));
        scheduler.Tick(0.5);
        scheduler.Tick(0.5); // 1.0 s of quiet since the last event

        Assert.Equal(1, host.Automation.Items.CaptureCount);
        TrackedConsumable tracked = Assert.Single(inventoryHost.Consumables.Tracked);
        Assert.Equal(40, tracked.CurrentCount);

        // With only one snapshot ever recorded, the rate math reads exactly
        // zero — no fabricated login-time spike.
        Assert.Equal(0d, tracked.History.GetValueDifference(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1)));
        Assert.Equal(TimeSpan.Zero, tracked.History.GetTimeToDepletion(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void World_only_ObjectChanged_events_do_not_dirty_or_delay_priming()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);

        // MEDIUM-1: a monster spawning, a stranger crossing a cell boundary
        // (an object that resolves but is NOT owned by the local player) —
        // this must not reset the quiet clock. In a populated area,
        // ObjectChanged fires constantly for objects like these.
        for (int i = 0; i < 20; i++)
        {
            uint id = (uint)(200 + i);
            host.Automation.Objects.Objects.Add(UnownedWorldObject(id));
            host.Events.RaiseObjectChanged(id, PluginObjectChangeKind.Created);
        }

        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 12));

        // Only the ordinary 1 s quiet period is needed — the 20 unowned
        // events above never touched the quiet clock at all.
        scheduler.Tick(0.5);
        scheduler.Tick(0.5);

        Assert.Equal(1, host.Automation.Items.CaptureCount);
        Assert.Equal(12, inventoryHost.Consumables.Tracked.First().CurrentCount);

        // And after priming, a burst of the same unowned-object churn must
        // not mark the tick dirty (no extra capture).
        for (int i = 0; i < 20; i++)
            host.Events.RaiseObjectChanged((uint)(200 + i), PluginObjectChangeKind.Updated);
        scheduler.Tick(0.5);

        Assert.Equal(1, host.Automation.Items.CaptureCount);
    }

    [Fact]
    public void The_hard_prime_deadline_primes_anyway_under_continuous_owned_activity()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 3));

        // Owned-object activity every single tick — quiescence never
        // arrives, but MEDIUM-1's hard 5 s deadline must prime anyway.
        for (int i = 0; i < 9; i++)
        {
            uint id = (uint)(300 + i);
            host.Automation.Objects.Objects.Add(OwnedWorldObject(id));
            scheduler.Tick(0.5); // 0.5 .. 4.5 s
            host.Events.RaiseObjectChanged(id, PluginObjectChangeKind.Created);
        }

        Assert.Equal(0, host.Automation.Items.CaptureCount); // still not primed at 4.5 s

        scheduler.Tick(0.5); // 5.0 s — the hard deadline

        Assert.Equal(1, host.Automation.Items.CaptureCount);
        Assert.Equal(3, inventoryHost.Consumables.Tracked.First().CurrentCount);
    }

    [Fact]
    public void After_priming_a_relevant_change_adds_exactly_one_snapshot()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);
        scheduler.Tick(1.0); // primed with an empty inventory
        Assert.Equal(1, host.Automation.Items.CaptureCount);

        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 5));
        host.Automation.Objects.Objects.Add(OwnedWorldObject(1u));
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.Created);
        scheduler.Tick(0.5); // the coalesced resync tick

        Assert.Equal(2, host.Automation.Items.CaptureCount);
        Assert.Equal(5, inventoryHost.Consumables.Tracked.First().CurrentCount);
    }

    [Fact]
    public void After_priming_an_irrelevant_change_captures_but_adds_no_snapshot()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 5));
        inventoryHost.Start(scheduler);
        scheduler.Tick(1.0); // primed at count 5
        int countAfterPriming = inventoryHost.Consumables.Tracked.First().History.GetValueTotal(
            TimeSpan.FromHours(1), out _);

        // A change to an OWNED object that never affects any tracked total
        // (e.g. a weapon being equipped) still triggers ObjectChanged and IS
        // relevant activity (it recomputes), but recomputes to the SAME
        // totals — no new snapshot should be recorded for it.
        host.Automation.Objects.Objects.Add(OwnedWorldObject(999u));
        host.Events.RaiseObjectChanged(999u, PluginObjectChangeKind.Updated);
        scheduler.Tick(0.5);

        Assert.Equal(2, host.Automation.Items.CaptureCount); // priming + this coalesced tick
        Assert.Equal(5, inventoryHost.Consumables.Tracked.First().CurrentCount);
        Assert.Equal(
            countAfterPriming,
            inventoryHost.Consumables.Tracked.First().History.GetValueTotal(TimeSpan.FromHours(1), out _));
    }

    [Fact]
    public void A_change_to_an_object_that_is_not_owned_at_all_is_never_captured()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 5));
        inventoryHost.Start(scheduler);
        scheduler.Tick(1.0); // primed at count 5
        int capturesAfterPriming = host.Automation.Items.CaptureCount;

        // MEDIUM-1: a monster spawning nearby is neither owned nor already
        // tracked — must not mark the tick dirty at all (no extra capture).
        host.Automation.Objects.Objects.Add(UnownedWorldObject(999u));
        host.Events.RaiseObjectChanged(999u, PluginObjectChangeKind.Created);
        scheduler.Tick(0.5);

        Assert.Equal(capturesAfterPriming, host.Automation.Items.CaptureCount);
    }

    [Fact]
    public void N_ObjectChanged_events_in_one_tick_add_at_most_one_snapshot()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);
        scheduler.Tick(1.0); // primed empty
        int capturesAfterPriming = host.Automation.Items.CaptureCount;

        host.Automation.Items.Owned.Add(ManaStone(1u, stackSize: 9));
        host.Automation.Objects.Objects.Add(OwnedWorldObject(1u));
        for (int i = 0; i < 10; i++)
            host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.Updated);

        scheduler.Tick(0.5); // one coalesced tick for all ten events

        Assert.Equal(capturesAfterPriming + 1, host.Automation.Items.CaptureCount);
        Assert.Equal(9, inventoryHost.Consumables.Tracked.First().CurrentCount);
    }

    [Fact]
    public void An_idle_tick_after_priming_still_raises_Changed()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);
        scheduler.Tick(1.0); // primed

        bool raised = false;
        inventoryHost.Consumables.Changed += () => raised = true;

        scheduler.Tick(0.5); // idle — nothing changed since priming
        Assert.True(raised);
        Assert.Equal(1, host.Automation.Items.CaptureCount); // still just the priming capture
    }

    [Fact]
    public void Stop_clears_the_consumables_tracker_and_stops_resyncing()
    {
        var host = new FakeHost();
        var scheduler = new TickScheduler(host.Events, new ChatOutput(host));
        var inventoryHost = new InventoryTrackerHost(host);
        inventoryHost.Start(scheduler);
        scheduler.Tick(1.0); // primed empty

        host.Automation.Items.Owned.Add(ManaStone(1u));
        host.Automation.Objects.Objects.Add(OwnedWorldObject(1u));
        host.Events.RaiseObjectChanged(1u, PluginObjectChangeKind.Created);
        scheduler.Tick(0.5);
        Assert.NotEmpty(inventoryHost.Consumables.Tracked);

        inventoryHost.Stop();
        Assert.Empty(inventoryHost.Consumables.Tracked);

        host.Automation.Items.Owned.Add(ManaStone(2u));
        host.Automation.Objects.Objects.Add(OwnedWorldObject(2u));
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created);
        scheduler.Tick(1.5);
        Assert.Empty(inventoryHost.Consumables.Tracked);
    }
}
