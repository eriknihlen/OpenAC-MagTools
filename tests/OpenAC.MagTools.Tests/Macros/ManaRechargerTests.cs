using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class ManaRechargerTests
{
    private static PluginInventoryItem Stone(uint objectId, string name, int currentMana)
        => new(
            objectId, 0u, name, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.ManaStone,
            ItemCurrentMana = currentMana,
        };

    [Fact]
    public void An_identified_mana_stone_with_mana_is_applied_to_the_player()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Items.Owned.Add(Stone(1u, "Mana Stone", currentMana: 500));
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Mana Stone", PluginObjectClass.ManaStone, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
            LastIdTime = (int)DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds(),
        });

        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, new OpenAC.MagTools.ChatOutput(host));
        var recharger = new ManaRecharger(host);
        recharger.Start(scheduler);
        scheduler.Tick(0.1);

        Assert.Contains(
            ("apply", 1u, 999u), host.Automation.Items.Calls);
    }

    [Fact]
    public void An_unidentified_mana_stone_is_identified_before_it_can_be_applied()
    {
        var host = new FakeHost();
        host.Automation.Items.Owned.Add(Stone(1u, "Mana Stone", currentMana: 500));
        host.Automation.Objects.Objects.Add(new PluginWorldObject(
            1u, 0u, "Mana Stone", PluginObjectClass.ManaStone, 0u, 0u, 0u)
        {
            HasAppraisalData = false,
        });

        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, new OpenAC.MagTools.ChatOutput(host));
        var recharger = new ManaRecharger(host);
        recharger.Start(scheduler);
        scheduler.Tick(0.1);

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.DoesNotContain(host.Automation.Items.Calls, c => c.Command == "apply");
    }

    [Fact]
    public void Falls_back_to_the_mana_charge_with_the_smallest_mana_when_no_mana_stone_qualifies()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Items.Owned.Add(Stone(1u, "Mana Charge", currentMana: 300));
        host.Automation.Items.Owned.Add(Stone(2u, "Mana Charge", currentMana: 100));

        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, new OpenAC.MagTools.ChatOutput(host));
        var recharger = new ManaRecharger(host);
        recharger.Start(scheduler);
        scheduler.Tick(0.1);

        Assert.Contains(("apply", 2u, 999u), host.Automation.Items.Calls);
    }

    [Fact]
    public void Stops_on_the_mana_stone_gives_chat_line()
    {
        var host = new FakeHost();
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, new OpenAC.MagTools.ChatOutput(host));
        var recharger = new ManaRecharger(host);
        recharger.Start(scheduler);

        host.Automation.Chat.Deliver("The Mana Stone gives you 200 points of mana.");

        Assert.False(recharger.IsRunning);
    }

    [Fact]
    public void Stops_on_logoff()
    {
        var host = new FakeHost();
        var scheduler = new OpenAC.MagTools.TickScheduler(host.Events, new OpenAC.MagTools.ChatOutput(host));
        var recharger = new ManaRecharger(host);
        recharger.Start(scheduler);

        host.Events.RaiseLogoff();

        Assert.False(recharger.IsRunning);
    }
}
