using AcDream.Plugin.Abstractions;
using OpenAC.MagTools;
using OpenAC.MagTools.Macros.Idle;
using OpenAC.MagTools.Settings;
using OpenAC.MagTools.Tests.Fakes;

namespace OpenAC.MagTools.Tests.Macros.Idle;

public sealed class IdleActionManagerTests
{
    private static (FakeHost Host, InventoryManagementSettings Settings, TickScheduler Scheduler) Make()
    {
        var host = new FakeHost();
        host.Automation.IsAvailable = true;
        host.Automation.Combat.Snapshot = new PluginCombatSnapshot(0u, PluginCombatMode.Peace, default, 0f, 0f, false, false, false, false);
        var settingsFile = new SettingsFile(host.Storage);
        var settings = new InventoryManagementSettings(settingsFile);
        var chat = new ChatOutput(host);
        var scheduler = new TickScheduler(host.Events, chat);
        return (host, settings, scheduler);
    }

    [Fact]
    public void RevealsAetheriaWhenBothItemsArePresent()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;

        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Equal(2u, host.Selection.SelectedObjectId);
        Assert.Contains(("apply", 1u, 2u), host.Automation.Items.Calls);
    }

    [Fact]
    public void DoesNothingOutsidePeaceMode()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Combat.Snapshot = new PluginCombatSnapshot(0u, PluginCombatMode.Melee, default, 0f, 0f, false, false, false, false);

        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Empty(host.Automation.Items.Calls);
    }

    [Fact]
    public void DoesNothingWhileBusy()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Items.IsBusy = true;

        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Empty(host.Automation.Items.Calls);
    }

    [Fact]
    public void AetheriaRevealNeverArmsTheConfirmationWindow()
    {
        // H4: only HeartCarver/ShatteredKeyFixer can raise the "chance to
        // succeed" confirmation in the original -- Aetheria reveal never
        // does, so a confirmation shortly after one must NOT be answered.
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // executes the reveal at t=2s

        host.Events.RaiseConfirmationRequested(new PluginConfirmation(1u, 5, "Are you sure?"));

        Assert.Empty(host.Automation.Dialogs.Answers);
    }

    [Fact]
    public void AnsweringAConfirmationOutsideTheFiveSecondWindowIsIgnored()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.HeartCarver.Value = true;
        host.Automation.Character.Skills.Add(new PluginSkillInfo(IdleActionManager.LockpickSkillId, "Lockpick", PluginSkillTraining.Trained, 100u));
        host.Automation.Items.Owned.Add(Item(1u, "Intricate Carving Tool", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(2u, "Drudge Heart", PluginObjectClass.Misc));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // executes the action at t=2s

        // Disarm so the next 2s tick doesn't re-execute (and re-arm the
        // 5s window) on the still-present items.
        settings.HeartCarver.Value = false;

        scheduler.Tick(6.0); // now t=8s, more than 5s after the t=2s action
        host.Events.RaiseConfirmationRequested(new PluginConfirmation(1u, 5, "Are you sure?"));

        Assert.Empty(host.Automation.Dialogs.Answers);
    }

    [Fact]
    public void AnsweringAConfirmationWithinTheFiveSecondWindowSucceeds()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.HeartCarver.Value = true;
        host.Automation.Character.Skills.Add(new PluginSkillInfo(IdleActionManager.LockpickSkillId, "Lockpick", PluginSkillTraining.Trained, 100u));
        host.Automation.Items.Owned.Add(Item(1u, "Intricate Carving Tool", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(2u, "Drudge Heart", PluginObjectClass.Misc));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // executes at t=2s

        scheduler.Tick(1.0); // t=3s, 1s after the action
        host.Events.RaiseConfirmationRequested(new PluginConfirmation(1u, 5, "Are you sure?"));

        Assert.Single(host.Automation.Dialogs.Answers);
        Assert.Equal((1u, true), host.Automation.Dialogs.Answers[0]);
    }

    [Fact]
    public void AnsweringAConfirmationIsOneShot()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.HeartCarver.Value = true;
        host.Automation.Character.Skills.Add(new PluginSkillInfo(IdleActionManager.LockpickSkillId, "Lockpick", PluginSkillTraining.Trained, 100u));
        host.Automation.Items.Owned.Add(Item(1u, "Intricate Carving Tool", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(2u, "Drudge Heart", PluginObjectClass.Misc));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // executes at t=2s, arms the window
        settings.HeartCarver.Value = false; // no re-arm on the next tick

        host.Events.RaiseConfirmationRequested(new PluginConfirmation(1u, 5, "First?"));
        host.Events.RaiseConfirmationRequested(new PluginConfirmation(2u, 5, "Second?"));

        Assert.Single(host.Automation.Dialogs.Answers);
        Assert.Equal(1u, host.Automation.Dialogs.Answers[0].ContextId);
    }

    [Fact]
    public void HeartCarvingRequiresLockpickTraining()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.HeartCarver.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Intricate Carving Tool", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(2u, "Drudge Heart", PluginObjectClass.Misc));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Empty(host.Automation.Items.Calls);

        host.Automation.Character.Skills.Add(new PluginSkillInfo(IdleActionManager.LockpickSkillId, "Lockpick", PluginSkillTraining.Trained, 100u));
        scheduler.Tick(2.0);

        Assert.Contains(("apply", 1u, 2u), host.Automation.Items.Calls);
    }

    [Fact]
    public void UnappraisedKeyringRequestsAnIdWhenRingingOrDeringingIsOn()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.KeyRinger.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Burning Sands Keyring", PluginObjectClass.Misc));
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Burning Sands Keyring", PluginObjectClass.Misc, 0u, 500u, 0u)
        {
            HasAppraisalData = false,
        });

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void UnappraisedKeyringOnlyRequestsAnIdOncePerObjectNotEveryTick()
    {
        // P6 re-review: the original's per-event wake triggers happened to
        // re-request every tick the keyring stayed unidentified; that was
        // never a deliberate design, and a plugin identify request costs a
        // real appraisal-queue slot, so this port dedupes to one request per
        // object id until it comes back appraised.
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.KeyRinger.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Burning Sands Keyring", PluginObjectClass.Misc));
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Burning Sands Keyring", PluginObjectClass.Misc, 0u, 500u, 0u)
        {
            HasAppraisalData = false,
        });

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);
        scheduler.Tick(2.0);
        scheduler.Tick(2.0);

        Assert.Single(host.Automation.Objects.IdentifyRequests, static id => id == 1u);
    }

    [Fact]
    public void UnappraisedKeyringDoesNotRequestAnIdWhenNeitherRingingNorDeringingIsOn()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(Item(1u, "Burning Sands Keyring", PluginObjectClass.Misc));
        host.Automation.Objects.Objects.Add(new PluginWorldObject(1u, 0u, "Burning Sands Keyring", PluginObjectClass.Misc, 0u, 500u, 0u)
        {
            HasAppraisalData = false,
        });

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Empty(host.Automation.Objects.IdentifyRequests);
    }

    [Fact]
    public void OnlyKeyDeringerRunsWhileAContainerIsOpen()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));
        host.Automation.Objects.OpenContainerObjectId = 999u;

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Empty(host.Automation.Items.Calls);
    }

    [Fact]
    public void BestKeyringPrefersMostKeysThenFewerUsesOnlyAtAZeroKeyTie()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.KeyRinger.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aged Legendary Key", PluginObjectClass.Key));
        host.Automation.Items.Owned.Add(Item(2u, "Burning Sands Keyring", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(3u, "Burning Sands Keyring", PluginObjectClass.Misc));
        // Both rings tied at 0 keys held; ring 3 has FEWER uses remaining, so
        // it must win over ring 2 despite arriving second (M4).
        AddKeyring(host, 2u, usesRemaining: 5, keysHeld: 0);
        AddKeyring(host, 3u, usesRemaining: 2, keysHeld: 0);

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Contains(("apply", 3u, 1u), host.Automation.Items.Calls);
        Assert.DoesNotContain(("apply", 2u, 1u), host.Automation.Items.Calls);
    }

    [Fact]
    public void BestKeyringPrefersStrictlyMoreKeysOverFewerUsesAtANonzeroTie()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.KeyRinger.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aged Legendary Key", PluginObjectClass.Key));
        host.Automation.Items.Owned.Add(Item(2u, "Burning Sands Keyring", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(3u, "Burning Sands Keyring", PluginObjectClass.Misc));
        AddKeyring(host, 2u, usesRemaining: 5, keysHeld: 3);
        AddKeyring(host, 3u, usesRemaining: 5, keysHeld: 7); // strictly more keys wins

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Contains(("apply", 3u, 1u), host.Automation.Items.Calls);
    }

    [Fact]
    public void DeringingTargetIsTheFirstMatchingKeyringNotTheOneWithTheMostKeys()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.KeyDeringer.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Intricate Carving Tool", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(2u, "Burning Sands Keyring", PluginObjectClass.Misc));
        host.Automation.Items.Owned.Add(Item(3u, "Burning Sands Keyring", PluginObjectClass.Misc));
        // Ring 3 has MORE keys, but ring 2 was found FIRST -- deringing must
        // target ring 2 (the original `break`s on the first match).
        AddKeyring(host, 2u, usesRemaining: 1, keysHeld: 2);
        AddKeyring(host, 3u, usesRemaining: 1, keysHeld: 9);
        host.Automation.Navigation.Snapshot = new PluginNavigationSnapshot(true, false, 1u, default, false, false);
        host.Automation.Navigation.Objects.Add(new PluginNavigationObject(50u, "Iron Chest", default));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0);

        Assert.Contains(("apply", 1u, 2u), host.Automation.Items.Calls);
        Assert.DoesNotContain(("apply", 1u, 3u), host.Automation.Items.Calls);
    }

    private static void AddKeyring(FakeHost host, uint objectId, int usesRemaining, int keysHeld)
    {
        host.Automation.Objects.Objects.Add(new PluginWorldObject(objectId, 0u, "Burning Sands Keyring", PluginObjectClass.Misc, 0u, 500u, 0u)
        {
            HasAppraisalData = true,
        });
        host.Automation.Objects.Properties[objectId] = new PluginItemProperties(
            new Dictionary<uint, int>
            {
                { (uint)OpenAC.MagTools.ItemInfo.ItemModel.UsesRemainingKey, usesRemaining },
                { (uint)OpenAC.MagTools.ItemInfo.ItemModel.KeysHeldKey, keysHeld },
            },
            new Dictionary<uint, long>(),
            new Dictionary<uint, bool>(),
            new Dictionary<uint, double>(),
            new Dictionary<uint, string>(),
            new Dictionary<uint, uint>(),
            new Dictionary<uint, uint>());
    }

    private static PluginInventoryItem Item(uint id, string name, PluginObjectClass objectClass)
        => new(id, 0u, name, 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u, 1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = objectClass,
        };

    /// <summary>A world object the local player owns -- resolves <see cref="PluginWorldObject.IsOwned"/> true for <see cref="IdleActionManager"/>'s ObjectChanged filter.</summary>
    private static PluginWorldObject OwnedWorldObject(uint objectId, string name, PluginObjectClass objectClass)
        => new(objectId, 0u, name, objectClass, 0u, 500u, 0u) { IsOwned = true };

    /// <summary>A world object the local player does NOT own -- a monster spawning, another player crossing a cell boundary, etc.</summary>
    private static PluginWorldObject UnownedWorldObject(uint objectId, string name, PluginObjectClass objectClass)
        => new(objectId, 0u, name, objectClass, 0u, 0u, 0u) { IsOwned = false };

    [Fact]
    public void NoObjectChangesMeansTheOwnedPackIsWalkedOnlyOnTheFirstThink()
    {
        // The bug: BuildSnapshot walked IItemAutomation.CaptureOwnedItems()
        // unconditionally on every 2 s think, even though
        // InventoryManagementSettings.AetheriaRevealer defaults to true so
        // the "all toggles off" early return never fires. 60 s / 2 s = 30
        // think ticks; with nothing ever changing, only the first one may
        // walk the owned pack.
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);

        for (int second = 0; second < 60; second++)
            scheduler.Tick(1.0);

        Assert.Equal(1, host.Automation.Items.CaptureOwnedItemsCalls);
    }

    [Fact]
    public void AnOwnedItemCreatedMidRunRebuildsTheSnapshotAndTheActionFires()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // first think: walks the pack, only the stone is present -- no plan

        Assert.Empty(host.Automation.Items.Calls);
        Assert.Equal(1, host.Automation.Items.CaptureOwnedItemsCalls);

        // The companion Coalesced Aetheria arrives mid-run (looted).
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));
        host.Automation.Objects.Objects.Add(OwnedWorldObject(2u, "Coalesced Aetheria", PluginObjectClass.Gem));
        host.Events.RaiseObjectChanged(2u, PluginObjectChangeKind.Created);

        scheduler.Tick(2.0); // next think rebuilds and plans/fires the reveal

        Assert.Equal(2, host.Automation.Items.CaptureOwnedItemsCalls);
        Assert.Equal(2u, host.Selection.SelectedObjectId);
        Assert.Contains(("apply", 1u, 2u), host.Automation.Items.Calls);
    }

    [Fact]
    public void AChangeToAnUnownedObjectDoesNotTriggerARebuild()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // first think: walks the pack once

        Assert.Equal(1, host.Automation.Items.CaptureOwnedItemsCalls);

        // A monster spawns nearby -- ObjectChanged fires for it, but it is
        // not owned by the local player, so it must not invalidate the cache.
        host.Automation.Objects.Objects.Add(UnownedWorldObject(99u, "Drudge", PluginObjectClass.Monster));
        host.Events.RaiseObjectChanged(99u, PluginObjectChangeKind.Created);

        scheduler.Tick(2.0);

        Assert.Equal(1, host.Automation.Items.CaptureOwnedItemsCalls);
    }

    [Fact]
    public void StopThenStartResetsTheDirtyFlagSoTheFirstThinkAfterStartRebuilds()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // first think after Start: one walk

        Assert.Equal(1, host.Automation.Items.CaptureOwnedItemsCalls);

        manager.Stop();
        manager.Start(scheduler); // a reconnect -- nothing owned has changed
        scheduler.Tick(2.0); // first think after the new Start must rebuild anyway

        Assert.Equal(2, host.Automation.Items.CaptureOwnedItemsCalls);
    }
}
