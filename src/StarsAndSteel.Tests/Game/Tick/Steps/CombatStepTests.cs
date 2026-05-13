using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class CombatStepTests
{
    [Fact]
    public void No_combat_when_only_one_owner_in_province()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var p = AddProvince(world, alice, "Cap");
        var u1 = AddUnit(world, alice, p, UnitType.MechInfantry, 1000);
        var u2 = AddUnit(world, alice, p, UnitType.MainBattleTank, 500);
        var ctx = Context(world, units: new[] { u1, u2 });

        new CombatStep().Execute(ctx);

        ctx.Events.OfType<CombatResolvedEvent>().Should().BeEmpty();
        u1.Strength.Should().Be(1000);
        u2.Strength.Should().Be(500);
    }

    [Fact]
    public void Two_owners_co_located_resolves_combat_with_both_taking_losses()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var p = AddProvince(world, bob, "BobCap");
        var attacker = AddUnit(world, alice, p, UnitType.MainBattleTank, 2000);
        var defender = AddUnit(world, bob, p, UnitType.MechInfantry, 2000);
        var ctx = Context(world, units: new[] { attacker, defender });

        new CombatStep().Execute(ctx);

        var ev = ctx.Events.OfType<CombatResolvedEvent>().Should().ContainSingle().Subject;
        ev.AttackerPlayerId.Should().Be(alice.Id);
        ev.DefenderPlayerId.Should().Be(bob.Id);
        (ev.AttackerStrengthLoss + ev.DefenderStrengthLoss).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Defender_wiped_flips_province_ownership_to_attacker()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var p = AddProvince(world, bob, "BobCap");
        // Massively overwhelming attacker, paper-thin defender.
        var attacker = AddUnit(world, alice, p, UnitType.StealthBomber, 10_000); // not relevant — air won't fight ground melee in this step
        var groundAttacker = AddUnit(world, alice, p, UnitType.MainBattleTank, 10_000);
        var defender = AddUnit(world, bob, p, UnitType.NationalGuard, 100);
        var ctx = Context(world, units: new[] { attacker, groundAttacker, defender });

        new CombatStep().Execute(ctx);

        defender.Strength.Should().Be(0);
        p.OwnerPlayerId.Should().Be(alice.Id);
        ctx.Events.OfType<ProvinceCapturedEvent>().Should().ContainSingle()
            .Which.ToPlayerId.Should().Be(alice.Id);
        bob.OwnedProvinces.Should().NotContain(p);
        alice.OwnedProvinces.Should().Contain(p);
    }

    [Fact]
    public void Air_units_alone_in_province_do_not_trigger_ground_combat()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var p = AddProvince(world, bob, "BobCap");
        var aliceAir = AddUnit(world, alice, p, UnitType.MultiroleFighter, 1000);
        var bobInf = AddUnit(world, bob, p, UnitType.MechInfantry, 1000);
        var ctx = Context(world, units: new[] { aliceAir, bobInf });

        new CombatStep().Execute(ctx);

        // CombatStep groups only ground units; alice has none here.
        ctx.Events.OfType<CombatResolvedEvent>().Should().BeEmpty();
        bobInf.Strength.Should().Be(1000);
    }

    // ---- Phase 3f: defender bonus from assigned general ----

    [Fact]
    public void Defender_general_at_province_increases_attacker_losses()
    {
        // Run two identical battles with the same RNG seed; only difference is
        // whether bob has a general parked at the contested province. Attacker
        // (alice) should suffer more strength loss in the bonus run.
        const long seed = 42;

        // Run A: no general.
        var worldA = NewWorld((int)seed);
        var aliceA = AddPlayer(worldA, "Alice");
        var bobA = AddPlayer(worldA, "Bob");
        var pA = AddProvince(worldA, bobA, "Battle");
        var attackerA = AddUnit(worldA, aliceA, pA, UnitType.MainBattleTank, 2000);
        var defenderA = AddUnit(worldA, bobA, pA, UnitType.MechInfantry, 2000);
        var ctxA = Context(worldA, units: new[] { attackerA, defenderA }, rngSeed: seed);
        new CombatStep().Execute(ctxA);

        // Run B: identical, plus bob has a general assigned to pB.
        var worldB = NewWorld((int)seed);
        var aliceB = AddPlayer(worldB, "Alice");
        var bobB = AddPlayer(worldB, "Bob");
        var pB = AddProvince(worldB, bobB, "Battle");
        var attackerB = AddUnit(worldB, aliceB, pB, UnitType.MainBattleTank, 2000);
        var defenderB = AddUnit(worldB, bobB, pB, UnitType.MechInfantry, 2000);
        var bobGeneral = new General
        {
            Id = Guid.NewGuid(), GameWorldId = worldB.Id,
            OwnerPlayerId = bobB.Id, Name = "Patton",
            AssignedProvinceId = pB.Id,
        };
        var ctxB = Context(worldB, units: new[] { attackerB, defenderB }, rngSeed: seed,
            generals: new List<General> { bobGeneral });
        new CombatStep().Execute(ctxB);

        var lossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        var lossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;

        lossB.Should().BeGreaterThan(lossA,
            "the defender's general should magnify defender outgoing damage");
    }

    [Fact]
    public void Generals_at_other_provinces_do_not_apply_their_bonus()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var battleProvince = AddProvince(world, bob, "Battle");
        var elsewhere = AddProvince(world, bob, "Elsewhere");
        var attacker = AddUnit(world, alice, battleProvince, UnitType.MainBattleTank, 2000);
        var defender = AddUnit(world, bob, battleProvince, UnitType.MechInfantry, 2000);
        // General is parked at the OTHER province — it should not boost this combat.
        var general = new General
        {
            Id = Guid.NewGuid(), GameWorldId = world.Id,
            OwnerPlayerId = bob.Id, Name = "Far Away",
            AssignedProvinceId = elsewhere.Id,
        };

        var ctx = Context(world, units: new[] { attacker, defender }, rngSeed: 42,
            generals: new List<General> { general });

        new CombatStep().Execute(ctx);

        // Re-run identical battle with no general at all and compare.
        var world2 = NewWorld();
        var alice2 = AddPlayer(world2, "Alice");
        var bob2 = AddPlayer(world2, "Bob");
        var bp2 = AddProvince(world2, bob2, "Battle");
        var attacker2 = AddUnit(world2, alice2, bp2, UnitType.MainBattleTank, 2000);
        var defender2 = AddUnit(world2, bob2, bp2, UnitType.MechInfantry, 2000);
        var ctx2 = Context(world2, units: new[] { attacker2, defender2 }, rngSeed: 42);

        new CombatStep().Execute(ctx2);

        var lossWith = ctx.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        var lossWithout = ctx2.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        lossWith.Should().Be(lossWithout, "a general elsewhere doesn't help this battle");
    }

    [Fact]
    public void Attacker_general_at_contested_province_does_not_boost_attacker()
    {
        // Edge case: the attacker has a general assigned at a province the defender
        // owns (e.g. attacker took the territory previously, lost it, and the
        // general's assignment was set-null then re-set — or the data is mid-flip).
        // CombatStep applies the bonus ONLY when the general's owner is the defender.
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var contested = AddProvince(world, bob, "Contested");
        var attacker = AddUnit(world, alice, contested, UnitType.MainBattleTank, 2000);
        var defender = AddUnit(world, bob, contested, UnitType.MechInfantry, 2000);
        // Alice's general assigned to bob's province (impossible via the API — service
        // gates on caller-owned province — but defensively we want CombatStep to not
        // confuse "any general here" with "defender's general here").
        var aliceGeneral = new General
        {
            Id = Guid.NewGuid(), GameWorldId = world.Id,
            OwnerPlayerId = alice.Id, Name = "Forward Liaison",
            AssignedProvinceId = contested.Id,
        };

        var ctx = Context(world, units: new[] { attacker, defender }, rngSeed: 42,
            generals: new List<General> { aliceGeneral });

        new CombatStep().Execute(ctx);

        // Compare to no-general baseline.
        var world2 = NewWorld();
        var alice2 = AddPlayer(world2, "Alice");
        var bob2 = AddPlayer(world2, "Bob");
        var c2 = AddProvince(world2, bob2, "Contested");
        var a2 = AddUnit(world2, alice2, c2, UnitType.MainBattleTank, 2000);
        var d2 = AddUnit(world2, bob2, c2, UnitType.MechInfantry, 2000);
        var ctx2 = Context(world2, units: new[] { a2, d2 }, rngSeed: 42);
        new CombatStep().Execute(ctx2);

        var ev = ctx.Events.OfType<CombatResolvedEvent>().Single();
        var ev2 = ctx2.Events.OfType<CombatResolvedEvent>().Single();
        ev.AttackerStrengthLoss.Should().Be(ev2.AttackerStrengthLoss,
            "alice's general at bob's province must not buff alice's own attack");
    }

    // ---- Phase 3g: doctrine techs (combined_arms, defense_in_depth) ----

    [Fact]
    public void Defense_in_depth_increases_attacker_losses()
    {
        // Same RNG seed both runs; only difference is defender has the doctrine tech.
        const long seed = 42;

        var worldA = NewWorld((int)seed);
        var aliceA = AddPlayer(worldA, "Alice");
        var bobA = AddPlayer(worldA, "Bob");
        var pA = AddProvince(worldA, bobA, "Battle");
        var atkA = AddUnit(worldA, aliceA, pA, UnitType.MainBattleTank, 2000);
        var defA = AddUnit(worldA, bobA, pA, UnitType.MechInfantry, 2000);
        var ctxA = Context(worldA, units: new[] { atkA, defA }, rngSeed: seed);
        new CombatStep().Execute(ctxA);

        var worldB = NewWorld((int)seed);
        var aliceB = AddPlayer(worldB, "Alice");
        var bobB = AddPlayer(worldB, "Bob");
        var pB = AddProvince(worldB, bobB, "Battle");
        var atkB = AddUnit(worldB, aliceB, pB, UnitType.MainBattleTank, 2000);
        var defB = AddUnit(worldB, bobB, pB, UnitType.MechInfantry, 2000);
        var ctxB = Context(worldB, units: new[] { atkB, defB }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress> { UnlockedTech(bobB, "defense_in_depth") });
        new CombatStep().Execute(ctxB);

        var lossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        var lossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;

        lossB.Should().BeGreaterThan(lossA,
            "defense_in_depth should magnify defender outgoing damage");
    }

    [Fact]
    public void Combined_arms_tech_boosts_combined_arms_side_only()
    {
        // Construct a side that satisfies the combined-arms composition (ground + air + AA).
        // Without the tech, that side gets the default 1.20 multiplier; with the tech, 1.25.
        // We give bob (defender) the qualifying composition + the tech and confirm
        // attacker losses rise vs a baseline run where bob has no tech.
        const long seed = 7;

        var worldA = NewWorld((int)seed);
        var aliceA = AddPlayer(worldA, "Alice");
        var bobA = AddPlayer(worldA, "Bob");
        var pA = AddProvince(worldA, bobA, "Battle");
        var atkA = AddUnit(worldA, aliceA, pA, UnitType.MainBattleTank, 10_000);
        var defGroundA = AddUnit(worldA, bobA, pA, UnitType.MechInfantry, 10_000);
        var defAirA   = AddUnit(worldA, bobA, pA, UnitType.MultiroleFighter, 5_000);
        var defAaA    = AddUnit(worldA, bobA, pA, UnitType.AABattery, 3_000);
        var ctxA = Context(worldA, units: new[] { atkA, defGroundA, defAirA, defAaA }, rngSeed: seed);
        new CombatStep().Execute(ctxA);

        var worldB = NewWorld((int)seed);
        var aliceB = AddPlayer(worldB, "Alice");
        var bobB = AddPlayer(worldB, "Bob");
        var pB = AddProvince(worldB, bobB, "Battle");
        var atkB = AddUnit(worldB, aliceB, pB, UnitType.MainBattleTank, 10_000);
        var defGroundB = AddUnit(worldB, bobB, pB, UnitType.MechInfantry, 10_000);
        var defAirB   = AddUnit(worldB, bobB, pB, UnitType.MultiroleFighter, 5_000);
        var defAaB    = AddUnit(worldB, bobB, pB, UnitType.AABattery, 3_000);
        var ctxB = Context(worldB, units: new[] { atkB, defGroundB, defAirB, defAaB }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress> { UnlockedTech(bobB, "combined_arms") });
        new CombatStep().Execute(ctxB);

        var lossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        var lossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;

        lossB.Should().BeGreaterThan(lossA,
            "combined_arms raises bob's combined-arms multiplier 1.20 → 1.25");
    }

    [Fact]
    public void Combined_arms_tech_does_nothing_without_qualifying_composition()
    {
        // Bob has the tech but no AA + no air — combined-arms condition false on his side,
        // so no multiplier is applied either way. Attacker losses should match baseline.
        const long seed = 3;

        var worldA = NewWorld((int)seed);
        var aliceA = AddPlayer(worldA, "Alice");
        var bobA = AddPlayer(worldA, "Bob");
        var pA = AddProvince(worldA, bobA, "Battle");
        var atkA = AddUnit(worldA, aliceA, pA, UnitType.MainBattleTank, 2000);
        var defA = AddUnit(worldA, bobA, pA, UnitType.MechInfantry, 2000);
        var ctxA = Context(worldA, units: new[] { atkA, defA }, rngSeed: seed);
        new CombatStep().Execute(ctxA);

        var worldB = NewWorld((int)seed);
        var aliceB = AddPlayer(worldB, "Alice");
        var bobB = AddPlayer(worldB, "Bob");
        var pB = AddProvince(worldB, bobB, "Battle");
        var atkB = AddUnit(worldB, aliceB, pB, UnitType.MainBattleTank, 2000);
        var defB = AddUnit(worldB, bobB, pB, UnitType.MechInfantry, 2000);
        var ctxB = Context(worldB, units: new[] { atkB, defB }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress> { UnlockedTech(bobB, "combined_arms") });
        new CombatStep().Execute(ctxB);

        var lossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        var lossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;

        lossB.Should().Be(lossA,
            "without ground+air+AA on bob's side the combined-arms boost can't trigger");
    }

    // ---- Phase 4f: maneuver_warfare doctrine ----

    /// <summary>
    /// Helper: pre-seed a <see cref="UnitMovedEvent"/> for the attacker stack into
    /// the context's Events list so CombatStep sees it as "moved into the contested
    /// province this tick" without having to wire MovementStep into the test.
    /// </summary>
    private static void SeedMovedInto(global::StarsAndSteel.Game.Tick.TickContext ctx, Unit attacker, Guid fromProvinceId, Guid toProvinceId)
    {
        ctx.Events.Add(new UnitMovedEvent(
            Tick: ctx.ProcessingTick,
            UnitId: attacker.Id,
            OwnerPlayerId: attacker.OwnerPlayerId,
            FromProvinceId: fromProvinceId,
            ToProvinceId: toProvinceId));
    }

    [Fact]
    public void Maneuver_warfare_increases_defender_losses_when_attacker_moved_in()
    {
        const long seed = 42;

        // Run A: no tech.
        var worldA = NewWorld((int)seed);
        var aliceA = AddPlayer(worldA, "Alice");
        var bobA = AddPlayer(worldA, "Bob");
        var fromA = AddProvince(worldA, aliceA, "From");
        var pA = AddProvince(worldA, bobA, "Battle");
        var atkA = AddUnit(worldA, aliceA, pA, UnitType.MainBattleTank, 2000);
        var defA = AddUnit(worldA, bobA, pA, UnitType.MechInfantry, 2000);
        var ctxA = Context(worldA, units: new[] { atkA, defA }, rngSeed: seed);
        SeedMovedInto(ctxA, atkA, fromA.Id, pA.Id);
        new CombatStep().Execute(ctxA);

        // Run B: alice has maneuver_warfare AND moved in this tick.
        var worldB = NewWorld((int)seed);
        var aliceB = AddPlayer(worldB, "Alice");
        var bobB = AddPlayer(worldB, "Bob");
        var fromB = AddProvince(worldB, aliceB, "From");
        var pB = AddProvince(worldB, bobB, "Battle");
        var atkB = AddUnit(worldB, aliceB, pB, UnitType.MainBattleTank, 2000);
        var defB = AddUnit(worldB, bobB, pB, UnitType.MechInfantry, 2000);
        var ctxB = Context(worldB, units: new[] { atkB, defB }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress> { UnlockedTech(aliceB, "maneuver_warfare") });
        SeedMovedInto(ctxB, atkB, fromB.Id, pB.Id);
        new CombatStep().Execute(ctxB);

        var defLossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;
        var defLossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;

        defLossB.Should().BeGreaterThan(defLossA,
            "maneuver_warfare should magnify attacker outgoing damage when attacker moved in");
    }

    [Fact]
    public void Maneuver_warfare_does_nothing_if_attacker_did_not_move_in()
    {
        // Tech but no UnitMovedEvent -> no bonus. Defender losses match baseline.
        const long seed = 9;

        var worldA = NewWorld((int)seed);
        var aliceA = AddPlayer(worldA, "Alice");
        var bobA = AddPlayer(worldA, "Bob");
        var pA = AddProvince(worldA, bobA, "Battle");
        var atkA = AddUnit(worldA, aliceA, pA, UnitType.MainBattleTank, 2000);
        var defA = AddUnit(worldA, bobA, pA, UnitType.MechInfantry, 2000);
        var ctxA = Context(worldA, units: new[] { atkA, defA }, rngSeed: seed);
        new CombatStep().Execute(ctxA);

        var worldB = NewWorld((int)seed);
        var aliceB = AddPlayer(worldB, "Alice");
        var bobB = AddPlayer(worldB, "Bob");
        var pB = AddProvince(worldB, bobB, "Battle");
        var atkB = AddUnit(worldB, aliceB, pB, UnitType.MainBattleTank, 2000);
        var defB = AddUnit(worldB, bobB, pB, UnitType.MechInfantry, 2000);
        var ctxB = Context(worldB, units: new[] { atkB, defB }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress> { UnlockedTech(aliceB, "maneuver_warfare") });
        // No SeedMovedInto: attacker is "in place" — bonus must NOT apply.
        new CombatStep().Execute(ctxB);

        var defLossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;
        var defLossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;

        defLossB.Should().Be(defLossA,
            "maneuver_warfare requires a UnitMovedEvent into the contested province this tick");
    }

    [Fact]
    public void Maneuver_warfare_does_not_buff_defender()
    {
        // Defender holds the tech and a (spurious) UnitMovedEvent points at the
        // contested province with the defender's own unit. CombatStep checks
        // attacker membership, not just any owner — so no bonus should apply.
        const long seed = 11;

        var worldA = NewWorld((int)seed);
        var aliceA = AddPlayer(worldA, "Alice");
        var bobA = AddPlayer(worldA, "Bob");
        var pA = AddProvince(worldA, bobA, "Battle");
        var atkA = AddUnit(worldA, aliceA, pA, UnitType.MainBattleTank, 2000);
        var defA = AddUnit(worldA, bobA, pA, UnitType.MechInfantry, 2000);
        var ctxA = Context(worldA, units: new[] { atkA, defA }, rngSeed: seed);
        new CombatStep().Execute(ctxA);

        var worldB = NewWorld((int)seed);
        var aliceB = AddPlayer(worldB, "Alice");
        var bobB = AddPlayer(worldB, "Bob");
        var pB = AddProvince(worldB, bobB, "Battle");
        var atkB = AddUnit(worldB, aliceB, pB, UnitType.MainBattleTank, 2000);
        var defB = AddUnit(worldB, bobB, pB, UnitType.MechInfantry, 2000);
        // Bob (defender) has the tech.
        var ctxB = Context(worldB, units: new[] { atkB, defB }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress> { UnlockedTech(bobB, "maneuver_warfare") });
        // Spurious moved-event for the DEFENDER unit — must be ignored by attacker check.
        SeedMovedInto(ctxB, defB, pB.Id, pB.Id);
        new CombatStep().Execute(ctxB);

        var atkLossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        var atkLossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().AttackerStrengthLoss;
        var defLossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;
        var defLossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;

        atkLossB.Should().Be(atkLossA, "maneuver_warfare on the defender must not buff defender");
        defLossB.Should().Be(defLossA, "and must not change attacker outgoing damage either");
    }

    [Fact]
    public void Maneuver_warfare_stacks_with_combined_arms()
    {
        // Same battle three ways, same RNG seed:
        //   A: no techs, no moved-in event -> baseline.
        //   B: maneuver_warfare + moved-in -> defender loss > A.
        //   C: maneuver_warfare + combined_arms (qualifying composition) + moved-in
        //      -> defender loss > B (multipliers stack multiplicatively).
        const long seed = 17;

        // Run A
        var wA = NewWorld((int)seed);
        var alA = AddPlayer(wA, "Alice");
        var bA = AddPlayer(wA, "Bob");
        var fA = AddProvince(wA, alA, "From");
        var pA = AddProvince(wA, bA, "Battle");
        var atkGroundA = AddUnit(wA, alA, pA, UnitType.MainBattleTank, 5000);
        var defA = AddUnit(wA, bA, pA, UnitType.MechInfantry, 5000);
        var ctxA = Context(wA, units: new[] { atkGroundA, defA }, rngSeed: seed);
        new CombatStep().Execute(ctxA);

        // Run B: maneuver only.
        var wB = NewWorld((int)seed);
        var alB = AddPlayer(wB, "Alice");
        var bB = AddPlayer(wB, "Bob");
        var fB = AddProvince(wB, alB, "From");
        var pB = AddProvince(wB, bB, "Battle");
        var atkGroundB = AddUnit(wB, alB, pB, UnitType.MainBattleTank, 5000);
        var defB = AddUnit(wB, bB, pB, UnitType.MechInfantry, 5000);
        var ctxB = Context(wB, units: new[] { atkGroundB, defB }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress> { UnlockedTech(alB, "maneuver_warfare") });
        SeedMovedInto(ctxB, atkGroundB, fB.Id, pB.Id);
        new CombatStep().Execute(ctxB);

        // Run C: maneuver + combined_arms with qualifying ground+air+AA composition.
        var wC = NewWorld((int)seed);
        var alC = AddPlayer(wC, "Alice");
        var bC = AddPlayer(wC, "Bob");
        var fC = AddProvince(wC, alC, "From");
        var pC = AddProvince(wC, bC, "Battle");
        var atkGroundC = AddUnit(wC, alC, pC, UnitType.MainBattleTank, 5000);
        var atkAirC    = AddUnit(wC, alC, pC, UnitType.MultiroleFighter, 1000);
        var atkAaC     = AddUnit(wC, alC, pC, UnitType.AABattery, 500);
        var defC       = AddUnit(wC, bC, pC, UnitType.MechInfantry, 5000);
        var ctxC = Context(wC, units: new[] { atkGroundC, atkAirC, atkAaC, defC }, rngSeed: seed,
            unlockedResearch: new List<ResearchProgress>
            {
                UnlockedTech(alC, "maneuver_warfare"),
                UnlockedTech(alC, "combined_arms"),
            });
        SeedMovedInto(ctxC, atkGroundC, fC.Id, pC.Id);
        new CombatStep().Execute(ctxC);

        var defLossA = ctxA.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;
        var defLossB = ctxB.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;
        var defLossC = ctxC.Events.OfType<CombatResolvedEvent>().Single().DefenderStrengthLoss;

        defLossB.Should().BeGreaterThan(defLossA, "maneuver alone boosts defender losses");
        defLossC.Should().BeGreaterThan(defLossB, "combined_arms on top of maneuver stacks multiplicatively");
    }
}
