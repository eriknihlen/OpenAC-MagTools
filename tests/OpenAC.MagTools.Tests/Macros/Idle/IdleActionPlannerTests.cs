using OpenAC.MagTools.Macros.Idle;

namespace OpenAC.MagTools.Tests.Macros.Idle;

public sealed class IdleActionPlannerTests
{
    private static readonly IdleActionOptions AllEnabled = new(
        AetheriaRevealer: true, HeartCarver: true, ShatteredKeyFixer: true, KeyRinger: true, KeyDeringer: true);

    [Fact]
    public void NothingArmedWhenNoOptionMatchesAvailableItems()
    {
        Assert.Null(IdleActionPlanner.Plan(AllEnabled, default));
    }

    [Fact]
    public void AetheriaRevealTakesPriorityOverEverythingElse()
    {
        var snapshot = new IdleActionSnapshot(
            AetheriaManaStoneId: 1u, CoalescedAetheriaId: 2u,
            IntricateCarvingToolId: 3u, HeartItemId: 4u, ShatteredKeyItemId: 0u,
            BestKeyringForRingingId: 0u, KeyringWithKeysId: 0u, AgedLegendaryKeyId: 0u,
            ChestWithin10Meters: false, AnyContainerOpen: false, LockpickTrained: true);

        IdleActionPlan? plan = IdleActionPlanner.Plan(AllEnabled, snapshot);

        Assert.Equal("AetheriaRevealer", plan!.Value.FeatureName);
        Assert.Equal(1u, plan.Value.ToolObjectId);
        Assert.Equal(2u, plan.Value.TargetObjectId);
    }

    [Fact]
    public void AetheriaRevealDisabledFallsThroughToHeartCarving()
    {
        var options = AllEnabled with { AetheriaRevealer = false };
        var snapshot = new IdleActionSnapshot(
            AetheriaManaStoneId: 1u, CoalescedAetheriaId: 2u,
            IntricateCarvingToolId: 3u, HeartItemId: 4u, ShatteredKeyItemId: 0u,
            BestKeyringForRingingId: 0u, KeyringWithKeysId: 0u, AgedLegendaryKeyId: 0u,
            ChestWithin10Meters: false, AnyContainerOpen: false, LockpickTrained: true);

        IdleActionPlan? plan = IdleActionPlanner.Plan(options, snapshot);

        Assert.Equal("HeartCarver", plan!.Value.FeatureName);
        Assert.Equal(3u, plan.Value.ToolObjectId);
        Assert.Equal(4u, plan.Value.TargetObjectId);
    }

    [Fact]
    public void HeartCarvingRequiresLockpickTraining()
    {
        var snapshot = new IdleActionSnapshot(
            AetheriaManaStoneId: 0u, CoalescedAetheriaId: 0u,
            IntricateCarvingToolId: 3u, HeartItemId: 4u, ShatteredKeyItemId: 0u,
            BestKeyringForRingingId: 0u, KeyringWithKeysId: 0u, AgedLegendaryKeyId: 0u,
            ChestWithin10Meters: false, AnyContainerOpen: false, LockpickTrained: false);

        Assert.Null(IdleActionPlanner.Plan(AllEnabled, snapshot));
    }

    [Fact]
    public void ShatteredKeyFixingRequiresLockpickTraining()
    {
        var snapshot = new IdleActionSnapshot(
            0u, 0u, 3u, 0u, 5u, 0u, 0u, 0u, false, false, LockpickTrained: false);

        Assert.Null(IdleActionPlanner.Plan(AllEnabled, snapshot));
    }

    [Fact]
    public void ShatteredKeyFixingFires()
    {
        var snapshot = new IdleActionSnapshot(
            0u, 0u, 3u, 0u, 5u, 0u, 0u, 0u, false, false, LockpickTrained: true);

        IdleActionPlan? plan = IdleActionPlanner.Plan(AllEnabled, snapshot);

        Assert.Equal("ShatteredKeyFixer", plan!.Value.FeatureName);
    }

    [Fact]
    public void KeyRingingRequiresNoChestNearbyAndNoOpenContainer()
    {
        var withChest = new IdleActionSnapshot(
            0u, 0u, 0u, 0u, 0u, BestKeyringForRingingId: 6u, KeyringWithKeysId: 0u,
            AgedLegendaryKeyId: 7u, ChestWithin10Meters: true, AnyContainerOpen: false, LockpickTrained: false);
        Assert.Null(IdleActionPlanner.Plan(AllEnabled, withChest));

        var withOpenContainer = withChest with { ChestWithin10Meters = false, AnyContainerOpen = true };
        Assert.Null(IdleActionPlanner.Plan(AllEnabled, withOpenContainer));

        var clear = withChest with { ChestWithin10Meters = false, AnyContainerOpen = false };
        IdleActionPlan? plan = IdleActionPlanner.Plan(AllEnabled, clear);
        Assert.Equal("KeyRinger", plan!.Value.FeatureName);
        Assert.Equal(6u, plan.Value.ToolObjectId);
        Assert.Equal(7u, plan.Value.TargetObjectId);
    }

    [Fact]
    public void KeyDeringingRequiresAChestNearbyAndAbortsIfHoldingAnUnringedKey()
    {
        var noChest = new IdleActionSnapshot(
            0u, 0u, IntricateCarvingToolId: 3u, HeartItemId: 0u, ShatteredKeyItemId: 0u,
            BestKeyringForRingingId: 0u, KeyringWithKeysId: 8u, AgedLegendaryKeyId: 0u,
            ChestWithin10Meters: false, AnyContainerOpen: false, LockpickTrained: false);
        Assert.Null(IdleActionPlanner.Plan(AllEnabled, noChest));

        var holdingKey = noChest with { ChestWithin10Meters = true, AgedLegendaryKeyId = 9u };
        Assert.Null(IdleActionPlanner.Plan(AllEnabled, holdingKey));

        var ready = noChest with { ChestWithin10Meters = true };
        IdleActionPlan? plan = IdleActionPlanner.Plan(AllEnabled, ready);
        Assert.Equal("KeyDeringer", plan!.Value.FeatureName);
        Assert.Equal(3u, plan.Value.ToolObjectId);
        Assert.Equal(8u, plan.Value.TargetObjectId);
    }

    [Fact]
    public void EveryOptionDisabledNeverArmsAnything()
    {
        var snapshot = new IdleActionSnapshot(
            1u, 2u, 3u, 4u, 5u, 6u, 8u, 7u, false, false, true);

        Assert.Null(IdleActionPlanner.Plan(default, snapshot));
    }
}
