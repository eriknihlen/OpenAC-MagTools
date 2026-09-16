using System.Globalization;

namespace OpenAC.MagTools.Trackers.Combat;

/// <summary>
/// Builds the two Combat-tab list bodies from a <see cref="CombatTracker"/>.
/// Port of the maths in the original's <c>Views/CombatTrackerGUI.cs</c> and
/// <c>Views/CombatTrackerGUIInfo.cs</c>. The host's <c>&lt;list&gt;</c>
/// control supports real <c>&lt;column&gt;</c> children (see
/// docs/plugin-ui-markup.md), so both lists are genuine multi-column lists
/// here — monster list 111/37/55/*, damage list 40/52/52/45/*. Every VALUE
/// and PERCENTAGE computation is byte-for-byte the same math as the
/// original. Cells are left-aligned, unpadded strings: the host's
/// <c>UiMarkupList</c> draws each cell left-aligned and clips it to the
/// column's pixel width with no ellipsis (<c>UiMarkupList.cs</c>'s
/// <c>DrawTextCell</c>/<c>PushClip</c>), so left-padding a numeric string to
/// fake right-alignment would push its least-significant digits past the
/// column edge and silently clip them off — worse than no alignment at all.
/// The markup's <c>&lt;column&gt;</c> element has no alignment attribute, so
/// right-alignment for these columns is simply not reproduced here. See
/// docs/deviations.md.
/// </summary>
public static class CombatTrackerRows
{
    /// <summary>
    /// One monster-list row, one value per column. <see cref="TargetName"/>
    /// is null for the header row, or the local player's own name for the
    /// "All" aggregate row (so a caller can feed it straight into
    /// <see cref="CombatTracker.GetCombatInfos"/> the same way row 1 does in
    /// the original), or the opponent's name. The original's selected-row
    /// marker glyph is dropped entirely (not moved into the name cell) —
    /// see docs/deviations.md, "Combat monster-list row marker": the host's
    /// <c>&lt;list selectionband="true"&gt;</c> already highlights the
    /// selected row.
    /// </summary>
    public readonly record struct MonsterRow(
        string Name, string KillingBlows, string DamageReceived, string DamageGiven, string? TargetName);

    /// <summary>
    /// Builds the monster list: a header row, the "All" aggregate row, then
    /// one row per opponent the local player has fought — optionally sorted
    /// alphabetically (ordinal, matching the original's bubble sort using
    /// <c>String.CompareOrdinal</c>).
    /// </summary>
    public static IReadOnlyList<MonsterRow> BuildMonsterRows(
        CombatTracker tracker, string localPlayerName, bool sortAlphabetically)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(localPlayerName);

        var rows = new List<MonsterRow>
        {
            new(string.Empty, "KB's", "Dmg Rcvd", "Dmg Givn", null),
            BuildAggregateRow(tracker, localPlayerName),
        };

        // The original's CombatTrackerGUI seeded the monster list from all
        // three trackers, not just the plain combat one — an opponent the
        // local player only ever aetheria-surged or cloak-surged against
        // (no melee/missile/magic hit recorded at all) still gets a row.
        var names = new List<string>();
        foreach (CombatInfo info in tracker.GetCombatInfos(localPlayerName))
            AddOpponent(names, OpponentOf(info.SourceName, info.TargetName, localPlayerName));
        foreach (AetheriaInfo info in tracker.GetAetheriaInfos(localPlayerName))
            AddOpponent(names, OpponentOf(info.SourceName, info.TargetName, localPlayerName));
        foreach (CloakInfo info in tracker.GetCloakInfos(localPlayerName))
            AddOpponent(names, OpponentOf(info.SourceName, info.TargetName, localPlayerName));

        if (sortAlphabetically)
            names.Sort(StringComparer.Ordinal);

        foreach (string name in names)
            rows.Add(BuildOpponentRow(tracker, name, localPlayerName));

        return rows;
    }

    private static string? OpponentOf(string sourceName, string targetName, string localPlayerName)
    {
        if (!string.IsNullOrEmpty(sourceName) && sourceName != localPlayerName)
            return sourceName;
        if (!string.IsNullOrEmpty(targetName) && targetName != localPlayerName)
            return targetName;
        return null;
    }

    private static void AddOpponent(List<string> names, string? opponent)
    {
        if (opponent is not null && !names.Contains(opponent, StringComparer.Ordinal))
            names.Add(opponent);
    }

    private static MonsterRow BuildAggregateRow(CombatTracker tracker, string localPlayerName)
        => BuildMonsterRow(tracker.GetCombatInfos(localPlayerName), "All", localPlayerName, localPlayerName);

    private static MonsterRow BuildOpponentRow(CombatTracker tracker, string name, string localPlayerName)
        => BuildMonsterRow(tracker.GetCombatInfos(name), name, localPlayerName, name);

    private static MonsterRow BuildMonsterRow(
        IReadOnlyList<CombatInfo> combatInfos, string displayName, string localPlayerName, string targetName)
    {
        int killingBlows = 0;
        long damageReceived = 0;
        long damageGiven = 0;

        foreach (CombatInfo info in combatInfos)
        {
            if (info.SourceName == localPlayerName)
            {
                killingBlows += info.KillingBlows;
                damageGiven += info.Damage;
            }
            else if (info.TargetName == localPlayerName)
            {
                damageReceived += info.Damage;
            }
        }

        string kb = killingBlows == 0 ? "" : NumberFormatter.Format(killingBlows, "#,##0", 99999);
        string rcvd = damageReceived == 0 ? "" : NumberFormatter.Format(damageReceived, "#,##0", 9999999);
        string givn = damageGiven == 0 ? "" : NumberFormatter.Format(damageGiven, "#,##0", 99999999);

        return new MonsterRow(displayName, kb, rcvd, givn, targetName);
    }

    // ── Damage/info list ────────────────────────────────────────────────────

    /// <summary>
    /// One damage-list row, one value per column: an element/summary label,
    /// the melee/missile and magic damage-received cells, a right-hand
    /// stat label, and its value.
    /// </summary>
    public readonly record struct DamageRow(
        string Label, string MeleeMissile, string Magic, string StatLabel, string StatValue);

    /// <summary>
    /// Builds the damage-list body for whichever monster row is selected
    /// (<paramref name="targetName"/> — the local player's own name for
    /// "All", per <see cref="MonsterRow.TargetName"/>).
    /// </summary>
    public static IReadOnlyList<DamageRow> BuildDamageRows(
        CombatTracker tracker, string? targetName, string localPlayerName)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(localPlayerName);

        if (targetName is null)
            return [];

        IReadOnlyList<CombatInfo> combatInfos = tracker.GetCombatInfos(targetName);
        IReadOnlyList<AetheriaInfo> aetheriaInfos = tracker.GetAetheriaInfos(targetName);
        IReadOnlyList<CloakInfo> cloakInfos = tracker.GetCloakInfos(targetName);

        // Damage RECEIVED, broken out by element (the player was the target).
        var receivedMeleeMissile = new Dictionary<DamageElement, int>();
        var receivedMagic = new Dictionary<DamageElement, int>();
        int totalMeleeMissile = 0;
        int totalMagic = 0;

        foreach (CombatInfo info in combatInfos)
        {
            if (info.TargetName != localPlayerName)
                continue;

            foreach (KeyValuePair<AttackType, DamageByAttackType> byAttackType in info.DamageByAttackTypes)
            {
                Dictionary<DamageElement, int> bucket = byAttackType.Key switch
                {
                    AttackType.MeleeMissle => receivedMeleeMissile,
                    AttackType.Magic => receivedMagic,
                    _ => new Dictionary<DamageElement, int>(),
                };

                foreach (KeyValuePair<DamageElement, DamageByElement> byElement in byAttackType.Value.DamageByElements)
                {
                    bucket.TryGetValue(byElement.Key, out int existing);
                    bucket[byElement.Key] = existing + byElement.Value.Damage;

                    if (byAttackType.Key == AttackType.MeleeMissle)
                        totalMeleeMissile += byElement.Value.Damage;
                    else if (byAttackType.Key == AttackType.Magic)
                        totalMagic += byElement.Value.Damage;
                }
            }
        }

        static string Cell(Dictionary<DamageElement, int> bucket, DamageElement element)
            => bucket.TryGetValue(element, out int value) && value != 0
                ? value.ToString("#,##0", CultureInfo.InvariantCulture)
                : "";

        // Attacks / evades / resists.
        int totalAttacks = 0;
        int failedAttacks = 0;
        int totalMeleeDefends = 0;
        int totalEvades = 0;
        int totalMagicDefends = 0;
        int totalResists = 0;

        foreach (CombatInfo info in combatInfos)
        {
            if (info.SourceName == localPlayerName)
            {
                totalAttacks += info.TotalAttacks;
                failedAttacks += info.FailedAttacks;
            }
            else if (info.TargetName == localPlayerName)
            {
                foreach (KeyValuePair<AttackType, DamageByAttackType> byAttackType in info.DamageByAttackTypes)
                {
                    if (byAttackType.Key == AttackType.MeleeMissle)
                    {
                        totalMeleeDefends += byAttackType.Value.TotalAttacks;
                        totalEvades += byAttackType.Value.FailedAttacks;
                    }
                    else if (byAttackType.Key == AttackType.Magic)
                    {
                        totalMagicDefends += byAttackType.Value.TotalAttacks;
                        totalResists += byAttackType.Value.FailedAttacks;
                    }
                }
            }
        }

        int aetheriaSurges = 0;
        foreach (AetheriaInfo info in aetheriaInfos)
        {
            if (info.SourceName == localPlayerName)
                aetheriaSurges += info.TotalSurges;
        }

        int cloakSurges = 0;
        foreach (CloakInfo info in cloakInfos)
        {
            if (info.SourceName == localPlayerName)
                cloakSurges += info.TotalSurges;
        }

        long totalNormalDamage = 0;
        int maxNormalDamage = 0;
        int crits = 0;
        long totalCritDamage = 0;
        int maxCritDamage = 0;
        int killingBlows = 0;
        long damageGiven = 0;

        foreach (CombatInfo info in combatInfos)
        {
            if (info.SourceName != localPlayerName)
                continue;

            totalNormalDamage += info.TotalNormalDamage;
            if (maxNormalDamage < info.MaxNormalDamage)
                maxNormalDamage = info.MaxNormalDamage;
            crits += info.Crits;
            totalCritDamage += info.TotalCritDamage;
            if (maxCritDamage < info.MaxCritDamage)
                maxCritDamage = info.MaxCritDamage;
            killingBlows += info.KillingBlows;
            damageGiven += info.Damage;
        }

        string attacksCell = totalAttacks == 0
            ? ""
            : NumberFormatter.Format(totalAttacks, "#,##0", 999999)
                + " (" + Percent(totalAttacks - failedAttacks, totalAttacks, 0) + "%)";
        string evadesCell = totalMeleeDefends == 0
            ? ""
            : NumberFormatter.Format(totalMeleeDefends, "#,##0", 999999)
                + " (" + Percent(totalEvades, totalMeleeDefends, 0) + "%)";
        string resistsCell = totalMagicDefends == 0
            ? ""
            : NumberFormatter.Format(totalMagicDefends, "#,##0", 999999)
                + " (" + Percent(totalResists, totalMagicDefends, 0) + "%)";
        string aSurgesCell = aetheriaSurges == 0
            ? ""
            : NumberFormatter.Format(aetheriaSurges, "#,##0", 999999)
                + " (" + Percent(aetheriaSurges, totalAttacks, 1) + "%)";
        int receivedHits = (totalMeleeDefends - totalEvades) + (totalMagicDefends - totalResists);
        string cSurgesCell = cloakSurges == 0
            ? ""
            : cloakSurges.ToString("#,##0", CultureInfo.InvariantCulture)
                + " (" + Percent(cloakSurges, receivedHits, 1) + "%)";

        int normalCount = totalAttacks - failedAttacks - crits - killingBlows;
        string avgMaxCell = normalCount <= 0
            ? ""
            : ((int)(totalNormalDamage / normalCount)).ToString("#,##0", CultureInfo.InvariantCulture)
                + " / " + maxNormalDamage.ToString("#,##0", CultureInfo.InvariantCulture);

        int critDenominator = totalAttacks - failedAttacks - killingBlows;
        string critsCell = critDenominator <= 0
            ? ""
            : NumberFormatter.Format(crits, "#,##0", 999999)
                + " (" + Percent(crits, critDenominator, 1) + "%)";

        string critsAvgMaxCell = crits == 0
            ? ""
            : ((int)(totalCritDamage / crits)).ToString("#,##0", CultureInfo.InvariantCulture)
                + " / " + maxCritDamage.ToString("#,##0", CultureInfo.InvariantCulture);

        string totalDamageCell = damageGiven == 0
            ? ""
            : damageGiven.ToString("#,##0", CultureInfo.InvariantCulture);

        return
        [
            new DamageRow("", "Mel/Msl", "Magic", "Attacks", attacksCell),
            new DamageRow(
                "Typeless",
                Cell(receivedMeleeMissile, DamageElement.Typeless),
                Cell(receivedMagic, DamageElement.Typeless),
                "Evades",
                evadesCell),
            new DamageRow(
                "Slash",
                Cell(receivedMeleeMissile, DamageElement.Slash),
                Cell(receivedMagic, DamageElement.Slash),
                "Resists",
                resistsCell),
            new DamageRow(
                "Pierce",
                Cell(receivedMeleeMissile, DamageElement.Pierce),
                Cell(receivedMagic, DamageElement.Pierce),
                "A.Surges",
                aSurgesCell),
            new DamageRow(
                "Bludge",
                Cell(receivedMeleeMissile, DamageElement.Bludge),
                Cell(receivedMagic, DamageElement.Bludge),
                "C.Surges",
                cSurgesCell),
            new DamageRow(
                "Fire",
                Cell(receivedMeleeMissile, DamageElement.Fire),
                Cell(receivedMagic, DamageElement.Fire),
                "",
                ""),
            new DamageRow(
                "Cold",
                Cell(receivedMeleeMissile, DamageElement.Cold),
                Cell(receivedMagic, DamageElement.Cold),
                "Av/Mx",
                avgMaxCell),
            new DamageRow(
                "Acid",
                Cell(receivedMeleeMissile, DamageElement.Acid),
                Cell(receivedMagic, DamageElement.Acid),
                "Crits",
                critsCell),
            new DamageRow(
                "Electric",
                Cell(receivedMeleeMissile, DamageElement.Electric),
                Cell(receivedMagic, DamageElement.Electric),
                "Av/Mx",
                critsAvgMaxCell),
            new DamageRow("", "", "", "", ""),
            new DamageRow(
                "Total",
                totalMeleeMissile == 0 ? "" : totalMeleeMissile.ToString("#,##0", CultureInfo.InvariantCulture),
                totalMagic == 0 ? "" : totalMagic.ToString("#,##0", CultureInfo.InvariantCulture),
                "Total",
                totalDamageCell),
        ];
    }

    /// <summary>Rounds like the original's <c>Math.Round(percent, digits)</c> against a 0/0 guard.</summary>
    private static string Percent(int numerator, int denominator, int digits)
    {
        if (denominator == 0)
            return (0d).ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        double percent = numerator / (double)denominator * 100;
        return Math.Round(percent, digits).ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
