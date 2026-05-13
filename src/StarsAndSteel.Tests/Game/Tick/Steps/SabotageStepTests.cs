using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

/// <summary>
/// Phase 3e: cover the SabotageStep tick logic. POCO graphs only; uses a stub RNG
/// where useful so building selection is deterministic in tests that pin which
/// building gets destroyed.
/// </summary>
public class SabotageStepTests
{
    /// <summary>RNG stub returning the same fixed index every call.</summary>
    private sealed class FixedIndexRng : IRandomSource
    {
        private readonly int _index;
        public FixedIndexRng(int index) => _index = index;
        public long State => 0;
        public int NextInt(int exclusiveMax) => Math.Min(_index, exclusiveMax - 1);
        public double NextDouble() => 0;
    }

    private static TickContext BuildContext(
        GameWorld world,
        IList<Unit> units,
        IList<UnitOrder> unitOrders,
        IRandomSource? rng = null)
    {
        return new TickContext(
            world,
            processingTick: world.CurrentTick + 1,
            rng: rng ?? new DeterministicRandom(world.RngState),
            units: units,
            pendingUnitOrders: unitOrders,
            pendingConstructionOrders: new List<ConstructionOrder>(),
            adjacencies: new List<ProvinceAdjacency>());
    }

    private static UnitOrder SabotageOrder(Unit sf, Province target, int issuedAtTick = 1) => new()
    {
        Id = Guid.NewGuid(),
        UnitId = sf.Id,
        Unit = sf,
        OrderType = OrderType.Sabotage,
        TargetProvinceId = target.Id,
        TargetProvince = target,
        IssuedAtTick = issuedAtTick,
        Status = OrderStatus.Pending,
    };

    [Fact]
    public void Sabotage_destroys_one_building_and_inflicts_morale_loss_and_casualties()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        bobPr.MoraleLevel = 100;
        var b1 = AddBuilding(bobPr, BuildingType.SteelMill);
        var b2 = AddBuilding(bobPr, BuildingType.RecruitmentCenter);
        var sf = AddUnit(world, alice, alicePr, UnitType.SpecialForces, 1000);
        var order = SabotageOrder(sf, bobPr);
        // FixedIndexRng(0) → first building after deterministic Id sort gets destroyed.
        var ctx = BuildContext(world, new[] { sf }, new[] { order }, rng: new FixedIndexRng(0));

        new SabotageStep().Execute(ctx);

        bobPr.Buildings.Count.Should().Be(1); // one building destroyed
        ctx.BuildingsToDelete.Should().HaveCount(1);
        sf.Strength.Should().Be(1000 - 200); // SfStrengthLoss
        bobPr.MoraleLevel.Should().Be(90); // -10
        order.Status.Should().Be(OrderStatus.Complete);
        var ev = ctx.Events.OfType<SabotageResolvedEvent>().Should().ContainSingle().Subject;
        ev.DestroyedBuildingId.Should().NotBeNull();
        ev.SfStrengthLoss.Should().Be(200);
        ev.TargetMoraleLoss.Should().Be(10);
        // The destroyed building should be one of the two we placed.
        new[] { b1.Id, b2.Id }.Should().Contain(ev.DestroyedBuildingId!.Value);
    }

    [Fact]
    public void SF_unit_wiped_to_zero_strength_is_queued_for_deletion()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        AddBuilding(bobPr, BuildingType.SteelMill);
        // SF stack of 100 strength: takes 200 casualties, clamps to 0, deletion queued.
        var sf = AddUnit(world, alice, alicePr, UnitType.SpecialForces, 100);
        var order = SabotageOrder(sf, bobPr);
        var ctx = BuildContext(world, new[] { sf }, new[] { order });

        new SabotageStep().Execute(ctx);

        sf.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().Contain(sf);
        var ev = ctx.Events.OfType<SabotageResolvedEvent>().Single();
        ev.SfStrengthLoss.Should().Be(100); // clamped at zero so loss == sfBefore
    }

    [Fact]
    public void Order_fizzles_when_target_was_recaptured_by_attacker()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var building = AddBuilding(bobPr, BuildingType.SteelMill);
        var sf = AddUnit(world, alice, alicePr, UnitType.SpecialForces, 1000);
        var order = SabotageOrder(sf, bobPr);
        // Province captured between submission and resolution.
        bobPr.OwnerPlayerId = alice.Id;
        var ctx = BuildContext(world, new[] { sf }, new[] { order });

        new SabotageStep().Execute(ctx);

        bobPr.Buildings.Should().Contain(building); // untouched
        sf.Strength.Should().Be(1000); // no casualties on fizzle
        order.Status.Should().Be(OrderStatus.Complete);
        ctx.Events.OfType<SabotageResolvedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Order_cancels_when_SF_unit_died_this_tick()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        AddBuilding(bobPr, BuildingType.SteelMill);
        var sf = AddUnit(world, alice, alicePr, UnitType.SpecialForces, 0); // already dead
        var order = SabotageOrder(sf, bobPr);
        var ctx = BuildContext(world, new[] { sf }, new[] { order });

        new SabotageStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled);
        bobPr.Buildings.Should().HaveCount(1); // untouched
        ctx.Events.Should().BeEmpty();
    }

    [Fact]
    public void Step_ignores_non_sabotage_orders()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        AddBuilding(bobPr, BuildingType.SteelMill);
        var sf = AddUnit(world, alice, alicePr, UnitType.SpecialForces, 1000);
        // A Move order on the same SF unit — should be untouched by SabotageStep.
        var move = MoveOrder(sf, bobPr);
        var ctx = BuildContext(world, new[] { sf }, new[] { move });

        new SabotageStep().Execute(ctx);

        move.Status.Should().Be(OrderStatus.Pending);
        bobPr.Buildings.Should().HaveCount(1);
        sf.Strength.Should().Be(1000);
    }
}
