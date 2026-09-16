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
    public void AnsweringAConfirmationOutsideTheFiveSecondWindowIsIgnored()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // executes the action at t=2s

        // Disarm so the next 2s tick doesn't re-execute (and re-arm the
        // 5s window) on the still-present Aetheria pair.
        settings.AetheriaRevealer.Value = false;

        scheduler.Tick(6.0); // now t=8s, more than 5s after the t=2s action
        host.Events.RaiseConfirmationRequested(new PluginConfirmation(1u, 5, "Are you sure?"));

        Assert.Empty(host.Automation.Dialogs.Answers);
    }

    [Fact]
    public void AnsweringAConfirmationWithinTheFiveSecondWindowSucceeds()
    {
        (FakeHost host, InventoryManagementSettings settings, TickScheduler scheduler) = Make();
        settings.AetheriaRevealer.Value = true;
        host.Automation.Items.Owned.Add(Item(1u, "Aetheria Mana Stone", PluginObjectClass.Gem));
        host.Automation.Items.Owned.Add(Item(2u, "Coalesced Aetheria", PluginObjectClass.Gem));

        var manager = new IdleActionManager(host, settings);
        manager.Start(scheduler);
        scheduler.Tick(2.0); // executes at t=2s

        scheduler.Tick(1.0); // t=3s, 1s after the action
        host.Events.RaiseConfirmationRequested(new PluginConfirmation(1u, 5, "Are you sure?"));

        Assert.Single(host.Automation.Dialogs.Answers);
        Assert.Equal((1u, true), host.Automation.Dialogs.Answers[0]);
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

    private static PluginInventoryItem Item(uint id, string name, PluginObjectClass objectClass)
        => new(id, 0u, name, 0u, 500u, 0u, 0u, 0u, 0u, 0u, 0u, 1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0, 0, 0, 0)
        {
            ObjectClass = objectClass,
        };
}
