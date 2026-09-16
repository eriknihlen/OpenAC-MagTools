using AcDream.Plugin.Abstractions;
using OpenAC.MagTools.Macros;
using OpenAC.MagTools.Tests.Fakes;
using Xunit;

namespace OpenAC.MagTools.Tests.Macros;

public sealed class OneTouchHealTests
{
    private static PluginInventoryItem Kit(uint objectId, string name, int usesRemaining, double affectsVitalAmt)
        => new(
            objectId, 0u, name, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, usesRemaining, usesRemaining, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.HealingKit,
            BoostValue = (int)affectsVitalAmt,
        };

    private static void Appraise(FakeHost host, uint objectId, string name, PluginObjectClass objectClass)
        => host.Automation.Objects.Objects.Add(new PluginWorldObject(
            objectId, 0u, name, objectClass, 0u, 0u, 0u)
        {
            HasAppraisalData = true,
        });

    [Fact]
    public void NoOpAtFullHealth()
    {
        var host = new FakeHost();
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 100;
        var heal = new OneTouchHeal(host);

        Assert.False(heal.TryHeal());
        Assert.Empty(host.Automation.Items.Calls);
    }

    [Fact]
    public void RequestsIdForTheFirstUnidentifiedKitAndStillAppliesItInTheSamePress()
    {
        // M3: the original falls through and applies the kit in the SAME
        // press it requests the id for -- it does not wait a full press
        // cycle. When the required-skill check would still pass with a 0
        // bonus (or there is no food fallback), the kit is applied anyway.
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 50;
        host.Automation.Character.Skills.Add(
            new PluginSkillInfo(OneTouchHeal.HealingSkillId, "Healing", PluginSkillTraining.Trained, 300));
        host.Automation.Items.Owned.Add(Kit(1u, "First Aid Kit", usesRemaining: 5, affectsVitalAmt: 10));

        var heal = new OneTouchHeal(host);
        Assert.True(heal.TryHeal());

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.Contains(("apply", 1u, 999u), host.Automation.Items.Calls);
    }

    [Fact]
    public void FallsThroughToFoodWhenTheUnidentifiedKitsZeroBonusIsNotEnoughAndFoodExists()
    {
        // Same "no kit appraised" starting point, but this time the 0-bonus
        // required-skill check genuinely fails AND a food fallback exists --
        // the kit must NOT be applied blind in that case.
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 10; // missing 10 -> required = 20 in Peace
        host.Automation.Character.Skills.Add(
            new PluginSkillInfo(OneTouchHeal.HealingSkillId, "Healing", PluginSkillTraining.Trained, 5));
        host.Automation.Combat.Snapshot = host.Automation.Combat.Snapshot with { Mode = PluginCombatMode.Peace };
        // effective (with a 0 bonus, since it is not yet appraised) = 5 < required 20.
        host.Automation.Items.Owned.Add(Kit(1u, "First Aid Kit", usesRemaining: 5, affectsVitalAmt: 0));
        PluginInventoryItem food = new(
            2u, 0u, "Healing Meat", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Food,
        };
        host.Automation.Items.Owned.Add(food);

        var heal = new OneTouchHeal(host);
        Assert.True(heal.TryHeal());

        Assert.Contains(1u, host.Automation.Objects.IdentifyRequests);
        Assert.DoesNotContain(host.Automation.Items.Calls, call => call.Command == "apply");
        Assert.Contains(("use", 2u, 0u), host.Automation.Items.Calls);
    }

    [Fact]
    public void PicksTheKitWithTheFewestUsesRemaining()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 90; // missing = 10, tiny required skill
        host.Automation.Character.Skills.Add(
            new PluginSkillInfo(OneTouchHeal.HealingSkillId, "Healing", PluginSkillTraining.Trained, 300));

        host.Automation.Items.Owned.Add(Kit(1u, "Worn Kit", usesRemaining: 2, affectsVitalAmt: 10));
        host.Automation.Items.Owned.Add(Kit(2u, "Fresh Kit", usesRemaining: 20, affectsVitalAmt: 10));
        Appraise(host, 1u, "Worn Kit", PluginObjectClass.HealingKit);
        Appraise(host, 2u, "Fresh Kit", PluginObjectClass.HealingKit);

        var heal = new OneTouchHeal(host);
        Assert.True(heal.TryHeal());

        Assert.Contains(("apply", 1u, 999u), host.Automation.Items.Calls);
    }

    [Fact]
    public void RequiredSkillIsDoubledMissingHealthInPeaceMode()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 50; // missing 50 -> required = 100 in Peace
        host.Automation.Character.Skills.Add(
            new PluginSkillInfo(OneTouchHeal.HealingSkillId, "Healing", PluginSkillTraining.Trained, 50));
        host.Automation.Combat.Snapshot = host.Automation.Combat.Snapshot with { Mode = PluginCombatMode.Peace };

        // effective = 50 + 40 = 90 < required 100 -> would normally fall through,
        // but there is no food, so the kit is applied anyway (the "or no food" branch).
        host.Automation.Items.Owned.Add(Kit(1u, "Kit", usesRemaining: 1, affectsVitalAmt: 40));
        Appraise(host, 1u, "Kit", PluginObjectClass.HealingKit);

        var heal = new OneTouchHeal(host);
        Assert.True(heal.TryHeal());

        Assert.Contains(("apply", 1u, 999u), host.Automation.Items.Calls);
    }

    [Fact]
    public void FallsBackToFoodWhenSkillIsTooLowAndFoodIsAvailable()
    {
        var host = new FakeHost();
        host.Automation.Character.ObjectId = 999u;
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 50; // missing 50 -> required = 100 in Peace
        host.Automation.Character.Skills.Add(
            new PluginSkillInfo(OneTouchHeal.HealingSkillId, "Healing", PluginSkillTraining.Trained, 50));
        host.Automation.Combat.Snapshot = host.Automation.Combat.Snapshot with { Mode = PluginCombatMode.Peace };

        host.Automation.Items.Owned.Add(Kit(1u, "Kit", usesRemaining: 1, affectsVitalAmt: 40));
        Appraise(host, 1u, "Kit", PluginObjectClass.HealingKit);
        PluginInventoryItem food = new(
            2u, 0u, "Healing Meat", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Food,
        };
        host.Automation.Items.Owned.Add(food);

        var heal = new OneTouchHeal(host);
        Assert.True(heal.TryHeal());

        Assert.Contains(("use", 2u, 0u), host.Automation.Items.Calls);
        Assert.DoesNotContain(host.Automation.Items.Calls, call => call.Command == "apply");
    }

    [Fact]
    public void FallsBackToFoodWhenHealingIsNotTrained()
    {
        var host = new FakeHost();
        host.Automation.Character.MaxHealth = 100;
        host.Automation.Character.CurrentHealth = 50;
        host.Automation.Character.Skills.Add(
            new PluginSkillInfo(OneTouchHeal.HealingSkillId, "Healing", PluginSkillTraining.Untrained, 10));

        PluginInventoryItem food = new(
            2u, 0u, "Tasty Meat", 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            1, 0, 0, 0u, 0, 0, 0u, false, 0d, 0, 0, 0, 0d, 0, 0, 0)
        {
            ObjectClass = PluginObjectClass.Food,
        };
        host.Automation.Items.Owned.Add(food);

        var heal = new OneTouchHeal(host);
        Assert.True(heal.TryHeal());

        Assert.Contains(("use", 2u, 0u), host.Automation.Items.Calls);
    }
}
