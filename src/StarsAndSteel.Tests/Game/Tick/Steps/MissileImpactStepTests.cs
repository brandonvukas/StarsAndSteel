using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

/// <summary>
/// Phase 3a: cover the MissileImpactStep tick logic. These tests mirror the
/// AirStrikeStep pattern (POCO-graph fixtures, no DbContext) and pin the
/// concrete damage / radiation / cascade behaviour so future tuning of the
/// constants doesn't silently regress the carrier-cascade or domain-gating
/// invariants.
/// </summary>
public class MissileImpactStepTests
{
    [Fact]
    public void Cruise_missile_damages_enemy_units_and_consumes_itself()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var missile = AddUnit(world, alice, alicePr, UnitType.CruiseMissile, 1);
        var defender = AddUnit(world, bob, bobPr, UnitType.MechInfantry, 5000);
        var order = MissileLaunchOrder(missile, bobPr);
        var ctx = Context(world,
            units: new[] { missile, defender },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        // Damage applied to defender.
        defender.Strength.Should().BeLessThan(5000);
        // Missile consumed.
        missile.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().Contain(missile);
        order.Status.Should().Be(OrderStatus.Complete);
        // No fallout for conventional warhead.
        bobPr.RadiationLevel.Should().Be(0);
        var ev = ctx.Events.OfType<MissileImpactResolvedEvent>().Should().ContainSingle().Subject;
        ev.WasNuclear.Should().BeFalse();
        ev.RadiationApplied.Should().Be(0);
        ev.DefenderStrengthLoss.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Nuclear_missile_applies_radiation_morale_hit_and_obliterates_defenders()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        bobPr.MoraleLevel = 100;
        var nuke = AddUnit(world, alice, alicePr, UnitType.NuclearMissile, 1);
        // 5000 strength garrison gets vaporized by 15000 nuclear damage.
        var defender = AddUnit(world, bob, bobPr, UnitType.MechInfantry, 5000);
        var order = MissileLaunchOrder(nuke, bobPr);
        var ctx = Context(world,
            units: new[] { nuke, defender },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        defender.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().Contain(defender);
        bobPr.RadiationLevel.Should().Be(60);
        bobPr.MoraleLevel.Should().Be(50); // -50 morale per docs/04 nuke effect
        nuke.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().Contain(nuke);
        var ev = ctx.Events.OfType<MissileImpactResolvedEvent>().Should().ContainSingle().Subject;
        ev.WasNuclear.Should().BeTrue();
        ev.RadiationApplied.Should().Be(60);
    }

    [Fact]
    public void Radiation_caps_at_one_hundred_when_repeatedly_nuked()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        bobPr.RadiationLevel = 80; // already heavily contaminated
        var nuke = AddUnit(world, alice, alicePr, UnitType.NuclearMissile, 1);
        var order = MissileLaunchOrder(nuke, bobPr);
        var ctx = Context(world,
            units: new[] { nuke },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        bobPr.RadiationLevel.Should().Be(100);
        // Event reports only the *applied* delta, not the +60 nominal.
        var ev = ctx.Events.OfType<MissileImpactResolvedEvent>().Single();
        ev.RadiationApplied.Should().Be(20);
    }

    [Fact]
    public void Friendly_units_at_target_are_not_damaged()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var alicePr = AddProvince(world, alice, "AlicePr");
        // Alice nukes her own province (whoops). Engine deliberately spares friendly units.
        var nuke = AddUnit(world, alice, alicePr, UnitType.NuclearMissile, 1);
        var friendly = AddUnit(world, alice, alicePr, UnitType.MechInfantry, 5000);
        var order = MissileLaunchOrder(nuke, alicePr);
        var ctx = Context(world,
            units: new[] { nuke, friendly },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        friendly.Strength.Should().Be(5000); // untouched
        // Province still gets the radiation/morale hit though â€” the warhead detonated.
        alicePr.RadiationLevel.Should().Be(60);
    }

    [Fact]
    public void Sinking_a_carrier_via_missile_drags_its_wings()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        bobPr.IsCoastal = true;
        var nuke = AddUnit(world, alice, alicePr, UnitType.NuclearMissile, 1);
        var carrier = AddUnit(world, bob, bobPr, UnitType.AircraftCarrier, 1);
        var wing = AddUnit(world, bob, bobPr, UnitType.CarrierAirWing, 500, parentUnitId: carrier.Id);
        var order = MissileLaunchOrder(nuke, bobPr);
        var ctx = Context(world,
            units: new[] { nuke, carrier, wing },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        carrier.Strength.Should().Be(0);
        wing.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().Contain(new[] { carrier, wing });
        ctx.Events.OfType<UnitDestroyedEvent>()
            .Should().Contain(e => e.UnitId == wing.Id && e.Cause == "CarrierLost");
    }

    [Fact]
    public void Non_missile_unit_with_launch_order_is_cancelled_safely()
    {
        // Defense in depth: validation should already block this, but if a bad
        // order ever reaches the step we must not damage anything.
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var infantry = AddUnit(world, alice, alicePr, UnitType.MechInfantry, 1000);
        var defender = AddUnit(world, bob, bobPr, UnitType.MechInfantry, 5000);
        var order = MissileLaunchOrder(infantry, bobPr);
        var ctx = Context(world,
            units: new[] { infantry, defender },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled);
        defender.Strength.Should().Be(5000);
        infantry.Strength.Should().Be(1000);
        ctx.Events.OfType<MissileImpactResolvedEvent>().Should().BeEmpty();
    }

    // ---------- Phase 4b1: SDI wonder interception ----------

    [Fact]
    public void Without_SDI_missile_lands_normally()
    {
        // Baseline: defender takes loss, missile destroyed (existing behaviour).
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var missile = AddUnit(world, alice, alicePr, UnitType.CruiseMissile, 1);
        var defender = AddUnit(world, bob, bobPr, UnitType.MechInfantry, 5000);
        var order = MissileLaunchOrder(missile, bobPr);

        var ctx = Context(world,
            units: new[] { missile, defender },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        defender.Strength.Should().BeLessThan(5000);
        var ev = ctx.Events.OfType<MissileImpactResolvedEvent>().Should().ContainSingle().Subject;
        ev.DefenderStrengthLoss.Should().BeGreaterThan(0);
        // No interception event payload distinction (RadiationApplied=0 for cruise is normal).
        ctx.Events.OfType<UnitDestroyedEvent>()
            .Where(e => e.UnitId == missile.Id)
            .Should().ContainSingle()
            .Which.Cause.Should().Be("MissileLaunched");
    }

    [Fact]
    public void SDI_intercept_rate_is_roughly_50_percent_over_many_trials()
    {
        // Statistical test: with SDI present we expect ~50% intercepts. Run 200 launches
        // and assert the observed rate falls in [0.35, 0.65] — a wide band that is
        // virtually impossible to fail by chance for a true 50% Bernoulli.
        const int trials = 200;
        int intercepted = 0;
        for (int i = 0; i < trials; i++)
        {
            var world = NewWorld();
            var alice = AddPlayer(world, "Alice");
            var bob = AddPlayer(world, "Bob");
            var alicePr = AddProvince(world, alice, "AlicePr");
            var bobPr = AddProvince(world, bob, "BobPr");
            // Bob owns SDI on his own province.
            bobPr.Buildings.Add(new Building
            {
                Id = Guid.NewGuid(),
                ProvinceId = bobPr.Id, Province = bobPr,
                Type = BuildingType.StrategicDefenseInitiative,
                Level = 1,
            });
            var missile = AddUnit(world, alice, alicePr, UnitType.CruiseMissile, 1);
            var defender = AddUnit(world, bob, bobPr, UnitType.MechInfantry, 5000);
            var order = MissileLaunchOrder(missile, bobPr);

            // Vary the seed per trial so we sample the distribution.
            var ctx = Context(world,
                units: new[] { missile, defender },
                unitOrders: new[] { order },
                rngSeed: i + 1);

            new MissileImpactStep().Execute(ctx);

            // Interception is recognizable by a "MissileIntercepted" UnitDestroyed cause +
            // zero defender loss in the payload.
            bool wasIntercepted = ctx.Events.OfType<UnitDestroyedEvent>()
                .Any(e => e.UnitId == missile.Id && e.Cause == "MissileIntercepted");
            if (wasIntercepted)
            {
                intercepted++;
                defender.Strength.Should().Be(5000); // intercepts spare the defender
            }
            else
            {
                defender.Strength.Should().BeLessThan(5000); // landed missiles damage
            }
        }

        var rate = intercepted / (double)trials;
        rate.Should().BeInRange(0.35, 0.65,
            because: $"a true 50% intercept rate over {trials} trials should sit comfortably inside [0.35, 0.65]; observed {rate:P0}");
    }

    [Fact]
    public void SDI_only_protects_provinces_owned_by_the_SDI_player()
    {
        // Charlie owns SDI elsewhere; Bob is being targeted but doesn't own SDI.
        // Missile must always land regardless of seed.
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var charlie = AddPlayer(world, "Charlie");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var charliePr = AddProvince(world, charlie, "CharliePr");
        // Charlie's province carries SDI; Bob's doesn't. Missile aimed at Bob.
        charliePr.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(),
            ProvinceId = charliePr.Id, Province = charliePr,
            Type = BuildingType.StrategicDefenseInitiative,
            Level = 1,
        });
        var missile = AddUnit(world, alice, alicePr, UnitType.CruiseMissile, 1);
        var defender = AddUnit(world, bob, bobPr, UnitType.MechInfantry, 5000);
        var order = MissileLaunchOrder(missile, bobPr);

        var ctx = Context(world,
            units: new[] { missile, defender },
            unitOrders: new[] { order });

        new MissileImpactStep().Execute(ctx);

        // Missile lands (Charlie's SDI doesn't shield Bob).
        defender.Strength.Should().BeLessThan(5000);
        ctx.Events.OfType<UnitDestroyedEvent>()
            .Where(e => e.UnitId == missile.Id)
            .Should().ContainSingle()
            .Which.Cause.Should().Be("MissileLaunched");
    }
}

