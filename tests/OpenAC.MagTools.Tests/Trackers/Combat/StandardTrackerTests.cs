using OpenAC.MagTools.Trackers.Combat;

namespace OpenAC.MagTools.Tests.Trackers.Combat;

public sealed class StandardTrackerTests
{
    private const string Me = "Acdream";

    // ── Failed attacks ──────────────────────────────────────────────────────

    [Fact]
    public void YouEvadedIsMeleeAttackOnMe()
    {
        CombatEventArgs? args = StandardTracker.Parse("You evaded Drudge Prowler!", Me);

        Assert.NotNull(args);
        Assert.Equal("Drudge Prowler", args!.SourceName);
        Assert.Equal(Me, args.TargetName);
        Assert.Equal(AttackType.MeleeMissle, args.AttackType);
        Assert.True(args.IsFailedAttack);
    }

    [Fact]
    public void TargetEvadedMyAttackIsMeleeAttackByMe()
    {
        CombatEventArgs? args = StandardTracker.Parse("Drudge Prowler evaded your attack.", Me);

        Assert.NotNull(args);
        Assert.Equal(Me, args!.SourceName);
        Assert.Equal("Drudge Prowler", args.TargetName);
        Assert.Equal(AttackType.MeleeMissle, args.AttackType);
        Assert.True(args.IsFailedAttack);
    }

    [Fact]
    public void YouResistTheSpellIsMagicAttackOnMe()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "You resist the spell cast by Virindi Rival", Me);

        Assert.NotNull(args);
        Assert.Equal("Virindi Rival", args!.SourceName);
        Assert.Equal(Me, args.TargetName);
        Assert.Equal(AttackType.Magic, args.AttackType);
        Assert.True(args.IsFailedAttack);
    }

    [Fact]
    public void TargetResistsMySpellIsMagicAttackByMe()
    {
        CombatEventArgs? args = StandardTracker.Parse("Sentient Crystal Shard resists your spell", Me);

        Assert.NotNull(args);
        Assert.Equal(Me, args!.SourceName);
        Assert.Equal("Sentient Crystal Shard", args.TargetName);
        Assert.Equal(AttackType.Magic, args.AttackType);
        Assert.True(args.IsFailedAttack);
    }

    // ── Melee/missile received ──────────────────────────────────────────────

    [Fact]
    public void PlainMeleeReceivedParsesSourceAndDamage()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Annihilator grazes your upper arm for 3 points of bludgeoning damage!", Me);

        Assert.NotNull(args);
        Assert.Equal("Annihilator", args!.SourceName);
        Assert.Equal(Me, args.TargetName);
        Assert.Equal(AttackType.MeleeMissle, args.AttackType);
        Assert.Equal(DamageElement.Bludge, args.DamageElement);
        Assert.Equal(3, args.DamageAmount);
        Assert.False(args.IsCriticalHit);
    }

    [Fact]
    public void CriticalMeleeReceivedHasOneSpaceAfterCriticalHit()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Critical hit! Virindi Executor scratches your upper leg for 3 points of slashing damage!",
            Me);

        Assert.NotNull(args);
        Assert.True(args!.IsCriticalHit);
        Assert.Equal(DamageElement.Slash, args.DamageElement);
        Assert.Equal(3, args.DamageAmount);
    }

    [Fact]
    public void OverpowerMeleeReceivedIsRecognized()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Overpower! Lugian Launcher bashes your foot for 43 points of bludgeoning damage!", Me);

        Assert.NotNull(args);
        Assert.True(args!.IsOverpower);
        Assert.Equal(DamageElement.Bludge, args.DamageElement);
        Assert.Equal(43, args.DamageAmount);
    }

    [Fact]
    public void CriticalOverpowerMeleeReceivedIsRecognized()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Critical hit! Overpower! Lugian Launcher smashes your lower leg for 108 points of bludgeoning damage!",
            Me);

        Assert.NotNull(args);
        Assert.True(args!.IsCriticalHit);
        Assert.True(args.IsOverpower);
        Assert.Equal(108, args.DamageAmount);
    }

    // ── Melee/missile received with an attacking monster's Dirty Fighting ──
    // A hostile monster's AI can use Sneak Attack/Reckless against the local
    // player too. Neither prefix is anchored inside MeleeMissileReceivedAttacks,
    // so it must be stripped before matching or the greedy leading
    // targetname group swallows it — see docs/deviations.md.

    [Fact]
    public void SneakAttackReceivedStripsThePrefixFromTheAttackerName()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Sneak Attack! Drudge Prowler mangles your arm for 12 points of slashing damage!", Me);

        Assert.NotNull(args);
        Assert.Equal("Drudge Prowler", args!.SourceName);
        Assert.Equal(Me, args.TargetName);
        Assert.Equal(12, args.DamageAmount);
        Assert.True(args.IsSneakAttack);
    }

    [Fact]
    public void RecklessReceivedStripsThePrefixAndSetsIsRecklessness()
    {
        // The host's DefenderLine emits "Reckless! " (not "Recklessness! ")
        // for an incoming reckless attack.
        CombatEventArgs? args = StandardTracker.Parse(
            "Reckless! Drudge Prowler mangles your arm for 12 points of slashing damage!", Me);

        Assert.NotNull(args);
        Assert.Equal("Drudge Prowler", args!.SourceName);
        Assert.True(args.IsRecklessness);
    }

    [Fact]
    public void CombinedSneakAttackAndRecklessReceivedStripsBothPrefixes()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Sneak Attack! Reckless! Drudge Prowler mangles your arm for 12 points of slashing damage!",
            Me);

        Assert.NotNull(args);
        Assert.Equal("Drudge Prowler", args!.SourceName);
        Assert.True(args.IsSneakAttack);
        Assert.True(args.IsRecklessness);
    }

    [Fact]
    public void RecklessnessSpellingIsAlsoAcceptedOnAReceivedLine()
    {
        // Defensive fallback in case a build ever emits the attacker-side
        // wording ("Recklessness! ") on an incoming line.
        CombatEventArgs? args = StandardTracker.Parse(
            "Recklessness! Drudge Prowler mangles your arm for 12 points of slashing damage!", Me);

        Assert.NotNull(args);
        Assert.Equal("Drudge Prowler", args!.SourceName);
        Assert.True(args.IsRecklessness);
    }

    [Fact]
    public void MagicReceivedAlsoStripsTheReceivedPrefixes()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Reckless! Crystal Shard Sentinel scorches you for 47 points with Flame Arc VII.", Me);

        Assert.NotNull(args);
        Assert.Equal("Crystal Shard Sentinel", args!.SourceName);
        Assert.Equal(AttackType.Magic, args.AttackType);
        Assert.True(args.IsRecklessness);
    }

    // ── Melee/missile given — the load-bearing spacing + ordering fix ──────

    [Fact]
    public void CriticalHitGivenHasTwoSpacesAfterCriticalHit()
    {
        // Retail's actual composed text: TWO spaces on the "given" side,
        // asymmetric with the received side's one space. If this regex used
        // one space (like the received table), it would still match this
        // input as far as .NET regex whitespace-insensitivity goes UNLESS the
        // literal spacing differs — pin the exact text here so a future edit
        // that "normalizes" the spacing gets caught.
        const string text = "Critical hit!  You scorch Annihilator for 685 points of fire damage!";

        CombatEventArgs? args = StandardTracker.Parse(text, Me);

        Assert.NotNull(args);
        Assert.Equal(Me, args!.SourceName);
        Assert.Equal("Annihilator", args.TargetName);
        Assert.True(args.IsCriticalHit);
        Assert.Equal(DamageElement.Fire, args.DamageElement);
        Assert.Equal(685, args.DamageAmount);
    }

    [Fact]
    public void SingleSpaceCriticalHitGivenDoesNotMatchAnyPattern()
    {
        // The mirror of the above: retail never emits a single-space
        // "Critical hit! You..." on the GIVEN side. Because every
        // given-side pattern requires either the exact two-space prefix or
        // no "Critical hit!" prefix at all (the plain "^You ..." pattern
        // cannot match text starting with "Critical hit!"), a wrongly-spaced
        // line like this one fails EVERY pattern and the whole line is
        // dropped (null) rather than silently parsed as an ordinary hit —
        // proof that the spacing is truly load-bearing, not cosmetic.
        const string text = "Critical hit! You scorch Annihilator for 685 points of fire damage!";

        CombatEventArgs? args = StandardTracker.Parse(text, Me);

        Assert.Null(args);
    }

    [Fact]
    public void SneakAttackGivenIsNotShadowedByTheGeneralPattern()
    {
        // Appendix B item 5: the upstream ordering put the general "You
        // [verb] X for N points" pattern before the Sneak-Attack/Recklessness
        // variants. This line's flags (computed by substring, independent of
        // which regex matches) must be true regardless of ordering, but the
        // fix's whole point is that the SPECIFIC pattern is what actually
        // matches — assert that via the captured target/damage, which would
        // still be correct either way, so the real proof is in
        // MeleeMissileGivenAttacksOrderTests below.
        CombatEventArgs? args = StandardTracker.Parse(
            "Sneak Attack! You chill Biaka for 357 points of cold damage!", Me);

        Assert.NotNull(args);
        Assert.True(args!.IsSneakAttack);
        Assert.Equal("Biaka", args.TargetName);
        Assert.Equal(357, args.DamageAmount);
        Assert.Equal(DamageElement.Cold, args.DamageElement);
    }

    [Fact]
    public void SneakAttackRecklessnessGivenParsesBothFlags()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Sneak Attack! Recklessness! You crush Olthoi Swarm Soldier for 234 points of bludgeoning damage!",
            Me);

        Assert.NotNull(args);
        Assert.True(args!.IsSneakAttack);
        Assert.True(args.IsRecklessness);
        Assert.Equal("Olthoi Swarm Soldier", args.TargetName);
        Assert.Equal(234, args.DamageAmount);
    }

    [Fact]
    public void RecklessnessOnlyGivenParses()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Recklessness! You scratch Reviath for 7 points of slashing damage!", Me);

        Assert.NotNull(args);
        Assert.True(args!.IsRecklessness);
        Assert.False(args.IsSneakAttack);
        Assert.Equal(DamageElement.Slash, args.DamageElement);
    }

    [Fact]
    public void PlainGivenParsesWithoutAnyFlag()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "You scorch Annihilator for 805 points of fire damage!", Me);

        Assert.NotNull(args);
        Assert.False(args!.IsCriticalHit);
        Assert.False(args.IsSneakAttack);
        Assert.False(args.IsRecklessness);
        Assert.Equal(805, args.DamageAmount);
    }

    // ── Magic received / given ──────────────────────────────────────────────

    [Fact]
    public void MagicReceivedParsesSourceAndDamage()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Crystal Shard Sentinel scorches you for 47 points with Flame Arc VII.", Me);

        Assert.NotNull(args);
        Assert.Equal("Crystal Shard Sentinel", args!.SourceName);
        Assert.Equal(Me, args.TargetName);
        Assert.Equal(AttackType.Magic, args.AttackType);
        Assert.Equal(DamageElement.Fire, args.DamageElement);
        Assert.Equal(47, args.DamageAmount);
    }

    [Fact]
    public void MagicalEnergiesLoseLineParsesAsMagicReceived()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Magical energies lose 1 point of health due to Sentient Crystal Shard casting Vitality Siphon",
            Me);

        Assert.NotNull(args);
        Assert.Equal("Sentient Crystal Shard", args!.SourceName);
        Assert.Equal(Me, args.TargetName);
        Assert.Equal(1, args.DamageAmount);
        Assert.Equal(DamageElement.Typeless, args.DamageElement);
    }

    [Fact]
    public void YouLoseHealthDueToCastingParsesAsMagicReceived()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "You lose 39 points of health due to Infernal Zefir casting Drain Health Other V on you",
            Me);

        Assert.NotNull(args);
        Assert.Equal("Infernal Zefir", args!.SourceName);
        Assert.Equal(39, args.DamageAmount);
    }

    [Fact]
    public void CastsAndDrainsParsesAsMagicReceived()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Crystal Minion casts Harm Other VI and drains 39 points of your health", Me);

        Assert.NotNull(args);
        Assert.Equal("Crystal Minion", args!.SourceName);
        Assert.Equal(39, args.DamageAmount);
    }

    [Fact]
    public void MagicGivenCriticalHitParses()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "Critical hit! You eradicate Invading Bronze Gauntlet Knight for 743 points with Incantation of Nether Arc.",
            Me);

        Assert.NotNull(args);
        Assert.True(args!.IsCriticalHit);
        Assert.Equal("Invading Bronze Gauntlet Knight", args.TargetName);
        Assert.Equal(DamageElement.Typeless, args.DamageElement);
        Assert.Equal(743, args.DamageAmount);
    }

    [Fact]
    public void MagicCastAttributesTargetWithZeroDamage()
    {
        CombatEventArgs? args = StandardTracker.Parse("You cast Gossamer Flesh on Chicken", Me);

        Assert.NotNull(args);
        Assert.Equal(Me, args!.SourceName);
        Assert.Equal("Chicken", args.TargetName);
        Assert.Equal(0, args.DamageAmount);
        Assert.Equal(DamageElement.None, args.DamageElement);
    }

    [Fact]
    public void MagicCastWithTrailingClauseAttributesTargetUpToComma()
    {
        CombatEventArgs? args = StandardTracker.Parse(
            "You cast Gossamer Flesh on Chicken, refreshing Gossamer Flesh", Me);

        Assert.NotNull(args);
        Assert.Equal("Chicken", args!.TargetName);
    }

    // ── Killing blow ─────────────────────────────────────────────────────

    [Fact]
    public void KillingBlowSourcesMeAndParsesTargetWithoutDamage()
    {
        CombatEventArgs? args = StandardTracker.Parse("You obliterate Drudge Skulker!", Me);

        Assert.NotNull(args);
        Assert.True(args!.IsKillingBlow);
        Assert.Equal(Me, args.SourceName);
        Assert.Equal("Drudge Skulker", args.TargetName);
        Assert.Equal(0, args.DamageAmount);
    }

    // ── Diagnostics ──────────────────────────────────────────────────────

    [Fact]
    public void UnparseableAttackLineReturnsNullWithoutDiagnostic()
    {
        var diagnostics = new List<string>();

        CombatEventArgs? args = StandardTracker.Parse("You say, \"hello there\"", Me, diagnostics.Add);

        Assert.Null(args);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EmptyLineReturnsNull()
        => Assert.Null(StandardTracker.Parse(string.Empty, Me));

    // ── Element inference ────────────────────────────────────────────────

    [Theory]
    [InlineData("slash", DamageElement.Slash)]
    [InlineData(" cut", DamageElement.Slash)]
    [InlineData(" scratch", DamageElement.Slash)]
    [InlineData(" mangle", DamageElement.Slash)]
    [InlineData("pierc", DamageElement.Pierce)]
    [InlineData(" gore", DamageElement.Pierce)]
    [InlineData(" impale", DamageElement.Pierce)]
    [InlineData(" nick", DamageElement.Pierce)]
    [InlineData(" stab", DamageElement.Pierce)]
    [InlineData("bludge", DamageElement.Bludge)]
    [InlineData(" smash", DamageElement.Bludge)]
    [InlineData(" bash", DamageElement.Bludge)]
    [InlineData(" graze", DamageElement.Bludge)]
    [InlineData(" crush", DamageElement.Bludge)]
    [InlineData("fire", DamageElement.Fire)]
    [InlineData(" burn", DamageElement.Fire)]
    [InlineData(" singe", DamageElement.Fire)]
    [InlineData(" scorch", DamageElement.Fire)]
    [InlineData(" incinerate", DamageElement.Fire)]
    [InlineData("cold", DamageElement.Cold)]
    [InlineData(" frost", DamageElement.Cold)]
    [InlineData(" chill", DamageElement.Cold)]
    [InlineData(" numb", DamageElement.Cold)]
    [InlineData(" freeze", DamageElement.Cold)]
    [InlineData("acid", DamageElement.Acid)]
    [InlineData(" blister", DamageElement.Acid)]
    [InlineData(" sear", DamageElement.Acid)]
    [InlineData(" corrode", DamageElement.Acid)]
    [InlineData(" dissolve", DamageElement.Acid)]
    [InlineData("electric", DamageElement.Electric)]
    [InlineData(" lightning", DamageElement.Electric)]
    [InlineData(" jolt", DamageElement.Electric)]
    [InlineData(" shock", DamageElement.Electric)]
    [InlineData(" spark", DamageElement.Electric)]
    [InlineData(" blast", DamageElement.Electric)]
    [InlineData(" eradicate", DamageElement.Typeless)]
    [InlineData(" wither", DamageElement.Typeless)]
    [InlineData(" scar", DamageElement.Typeless)]
    [InlineData(" twist", DamageElement.Typeless)]
    [InlineData(" deplete", DamageElement.Typeless)]
    [InlineData(" siphon", DamageElement.Typeless)]
    [InlineData(" exhaust", DamageElement.Typeless)]
    [InlineData(" drain", DamageElement.Typeless)]
    [InlineData(" lose ", DamageElement.Typeless)]
    [InlineData(" health ", DamageElement.Typeless)]
    [InlineData("qqqzzz", DamageElement.Unknown)]
    public void ElementInferenceMatchesEverySubstring(string substring, DamageElement expected)
        => Assert.Equal(expected, StandardTracker.GetElementFromText(substring));

    [Fact]
    public void SlashTakesPriorityOverElectricWhenBothSubstringsPresent()
    {
        // "shock" and "cut" both appear — slash is checked first in the
        // original's order, so it must win.
        Assert.Equal(
            DamageElement.Slash, StandardTracker.GetElementFromText("shock cut damage"));
    }

    [Fact]
    public void CombinedElementTextStillPicksFirstMatchInOrder()
    {
        // Retail's own combined-damage text (e.g. "fire/cold") is not
        // decomposed into two elements by GetElementFromText — first match
        // wins, same as the original. "fire" appears before "cold" so Fire
        // wins even though both substrings are present.
        Assert.Equal(
            DamageElement.Fire, StandardTracker.GetElementFromText("fire/cold damage"));
    }
}
