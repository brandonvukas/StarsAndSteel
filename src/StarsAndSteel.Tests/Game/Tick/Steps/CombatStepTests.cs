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
}
