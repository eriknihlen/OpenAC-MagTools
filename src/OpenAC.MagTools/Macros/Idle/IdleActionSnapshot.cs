namespace OpenAC.MagTools.Macros.Idle;

/// <summary>
/// The classified inventory/world state <see cref="IdleActionPlanner"/> needs
/// for one think tick, already resolved by the host from
/// <c>Items.CaptureOwnedItems()</c>/<c>Objects</c>/<c>Character</c> so the
/// planner itself stays a pure decision table over plain ids.
/// </summary>
public readonly record struct IdleActionSnapshot(
    uint AetheriaManaStoneId,
    uint CoalescedAetheriaId,
    uint IntricateCarvingToolId,
    uint HeartItemId,
    uint ShatteredKeyItemId,
    /// <summary>
    /// The best `Burning Sands Keyring` candidate: has id data, UsesRemaining
    /// &gt; 0, KeysHeld &lt; 24, preferring the ring with the MOST keys
    /// (tie-broken by most uses).
    /// </summary>
    uint BestKeyringForRingingId,
    /// <summary>Any `Burning Sands Keyring` with KeysHeld &gt; 0 (the deringing target).</summary>
    uint KeyringWithKeysId,
    uint AgedLegendaryKeyId,
    bool ChestWithin10Meters,
    bool AnyContainerOpen,
    bool LockpickTrained);

/// <summary>The five idle-action toggles (§1.10), reusing the existing InventoryManagement settings group.</summary>
public readonly record struct IdleActionOptions(
    bool AetheriaRevealer,
    bool HeartCarver,
    bool ShatteredKeyFixer,
    bool KeyRinger,
    bool KeyDeringer);

/// <summary>One decided (tool, target) pair and the feature that armed it.</summary>
public readonly record struct IdleActionPlan(uint ToolObjectId, uint TargetObjectId, string FeatureName);
