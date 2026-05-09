using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class LogisticsUpkeepStepTests
{
    [Fact]
    public void Mech_infantry_drains_food_and_manpower_from_owner()
    {
        var world = NewWorld();
        // MechInfantry per UpkeepCatalog: Money=5, Oil=1, Food=5, Manpower=1 per 1000 strength.
        var alice = AddPlayer(world, "USA", money: 1000, oil: 100, steel: 0, electronics: 0, food: 100, manpower: 100);
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, strength: 1000);
        var ctx = Context(world, units: new[] { unit });

        new LogisticsUpkeepStep().Execute(ctx);

        alice.Money.Should().Be(1000 - 5);
        alice.Oil.Should().Be(100 - 1);
        alice.Food.Should().Be(100 - 5);
        alice.Manpower.Should().Be(100 - 1);
        unit.Morale.Should().Be(100); // fully paid → no morale hit
    }

    [Fact]
    public void Air_unit_drains_money_and_oil_only()
    {
        var world = NewWorld();
        // MultiroleFighter: Money=15, Oil=8, Food=0, Manpower=0
        var alice = AddPlayer(world, "USA", money: 1000, oil: 100, steel: 0, electronics: 0, food: 100, manpower: 100);
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MultiroleFighter, strength: 1000);
        var ctx = Context(world, units: new[] { unit });

        new LogisticsUpkeepStep().Execute(ctx);

        alice.Money.Should().Be(985);
        alice.Oil.Should().Be(92);
        alice.Food.Should().Be(100);
        alice.Manpower.Should().Be(100);
    }

    [Fact]
    public void Half_strength_stack_pays_half_upkeep_rounded_up()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA", money: 1000, oil: 100, steel: 0, electronics: 0, food: 100, manpower: 100);
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, strength: 500); // half
        var ctx = Context(world, units: new[] { unit });

        new LogisticsUpkeepStep().Execute(ctx);

        alice.Money.Should().Be(1000 - 3); // ceil(5 * 0.5) = 3
        alice.Food.Should().Be(100 - 3);   // ceil(5 * 0.5) = 3
        alice.Manpower.Should().Be(100 - 1); // ceil(1 * 0.5) = 1
    }

    [Fact]
    public void Insufficient_food_clamps_pool_and_hits_morale()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA", money: 1000, oil: 100, steel: 0, electronics: 0, food: 2, manpower: 100);
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, strength: 1000, morale: 100);
        var ctx = Context(world, units: new[] { unit });

        new LogisticsUpkeepStep().Execute(ctx);

        alice.Food.Should().Be(0);     // clamped
        unit.Morale.Should().Be(97);   // -3 for the food shortfall
    }

    [Fact]
    public void Multiple_shortfalls_stack_morale_hit()
    {
        var world = NewWorld();
        // No money, no food → expect -6 morale.
        var alice = AddPlayer(world, "USA", money: 0, oil: 100, steel: 0, electronics: 0, food: 0, manpower: 100);
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, strength: 1000, morale: 100);
        var ctx = Context(world, units: new[] { unit });

        new LogisticsUpkeepStep().Execute(ctx);

        unit.Morale.Should().Be(94);
    }

    [Fact]
    public void Destroyed_stack_pays_no_upkeep()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA", money: 1000, oil: 100, steel: 0, electronics: 0, food: 100, manpower: 100);
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, strength: 0);
        var ctx = Context(world, units: new[] { unit });

        new LogisticsUpkeepStep().Execute(ctx);

        alice.Money.Should().Be(1000);
        alice.Food.Should().Be(100);
    }
}
