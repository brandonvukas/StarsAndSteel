using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class MovementStepTests
{
    [Fact]
    public void Move_order_relocates_unit_to_adjacent_province_and_marks_complete()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var src = AddProvince(world, alice, "Src");
        var dst = AddProvince(world, alice, "Dst");
        var unit = AddUnit(world, alice, src, UnitType.MechInfantry, strength: 1000);
        var order = MoveOrder(unit, dst);
        var ctx = Context(world,
            units: new[] { unit },
            unitOrders: new[] { order },
            adjacencies: new[] { Adj(src, dst) });

        new MovementStep().Execute(ctx);

        unit.LocationProvinceId.Should().Be(dst.Id);
        order.Status.Should().Be(OrderStatus.Complete);
        ctx.Events.OfType<UnitMovedEvent>().Should().ContainSingle()
            .Which.ToProvinceId.Should().Be(dst.Id);
    }

    [Fact]
    public void Move_to_non_adjacent_province_is_cancelled()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var src = AddProvince(world, alice, "Src");
        var dst = AddProvince(world, alice, "Dst");
        var unit = AddUnit(world, alice, src, UnitType.MechInfantry, 1000);
        var order = MoveOrder(unit, dst);
        var ctx = Context(world,
            units: new[] { unit },
            unitOrders: new[] { order },
            adjacencies: Array.Empty<Core.Entities.ProvinceAdjacency>());

        new MovementStep().Execute(ctx);

        unit.LocationProvinceId.Should().Be(src.Id);
        order.Status.Should().Be(OrderStatus.Cancelled);
        ctx.Events.OfType<UnitMovedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Air_unit_cannot_move()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var src = AddProvince(world, alice, "Src");
        var dst = AddProvince(world, alice, "Dst");
        var unit = AddUnit(world, alice, src, UnitType.CombatDrone, 500);
        var order = MoveOrder(unit, dst);
        var ctx = Context(world,
            units: new[] { unit },
            unitOrders: new[] { order },
            adjacencies: new[] { Adj(src, dst) });

        new MovementStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled);
        unit.LocationProvinceId.Should().Be(src.Id);
    }

    [Fact]
    public void Attack_order_also_relocates_unit()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var aliceProv = AddProvince(world, alice, "AlicePr");
        var bobProv = AddProvince(world, bob, "BobPr");
        var attacker = AddUnit(world, alice, aliceProv, UnitType.MainBattleTank, 1000);
        var order = AttackOrder(attacker, bobProv);
        var ctx = Context(world,
            units: new[] { attacker },
            unitOrders: new[] { order },
            adjacencies: new[] { Adj(aliceProv, bobProv) });

        new MovementStep().Execute(ctx);

        attacker.LocationProvinceId.Should().Be(bobProv.Id);
        order.Status.Should().Be(OrderStatus.Complete);
    }
}
