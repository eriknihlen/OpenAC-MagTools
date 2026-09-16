namespace OpenAC.MagTools.Macros.Idle;

/// <summary>
/// Ports <c>IdleActionManager</c>'s tool/target pairing table, first-pair-wins
/// order, verbatim (see the port design's §1.10).
/// </summary>
public static class IdleActionPlanner
{
    public static IdleActionPlan? Plan(IdleActionOptions options, IdleActionSnapshot snapshot)
    {
        if (options.AetheriaRevealer
            && snapshot.AetheriaManaStoneId != 0u
            && snapshot.CoalescedAetheriaId != 0u)
        {
            return new IdleActionPlan(snapshot.AetheriaManaStoneId, snapshot.CoalescedAetheriaId, "AetheriaRevealer");
        }

        if (options.HeartCarver
            && snapshot.IntricateCarvingToolId != 0u
            && snapshot.HeartItemId != 0u
            && snapshot.LockpickTrained)
        {
            return new IdleActionPlan(snapshot.IntricateCarvingToolId, snapshot.HeartItemId, "HeartCarver");
        }

        if (options.ShatteredKeyFixer
            && snapshot.IntricateCarvingToolId != 0u
            && snapshot.ShatteredKeyItemId != 0u
            && snapshot.LockpickTrained)
        {
            return new IdleActionPlan(snapshot.IntricateCarvingToolId, snapshot.ShatteredKeyItemId, "ShatteredKeyFixer");
        }

        if (options.KeyRinger
            && snapshot.BestKeyringForRingingId != 0u
            && snapshot.AgedLegendaryKeyId != 0u
            && !snapshot.ChestWithin10Meters
            && !snapshot.AnyContainerOpen)
        {
            return new IdleActionPlan(snapshot.BestKeyringForRingingId, snapshot.AgedLegendaryKeyId, "KeyRinger");
        }

        if (options.KeyDeringer
            && snapshot.IntricateCarvingToolId != 0u
            && snapshot.KeyringWithKeysId != 0u
            && snapshot.ChestWithin10Meters
            && snapshot.AgedLegendaryKeyId == 0u) // abort if we already hold an unringed key
        {
            return new IdleActionPlan(snapshot.IntricateCarvingToolId, snapshot.KeyringWithKeysId, "KeyDeringer");
        }

        return null;
    }
}
