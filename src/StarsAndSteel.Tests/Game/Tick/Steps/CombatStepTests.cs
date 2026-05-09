using FluentAssertions;
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
}
