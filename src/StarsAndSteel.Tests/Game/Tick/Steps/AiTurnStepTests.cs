using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class AiTurnStepTests
{
    [Fact]
    public void AiTurn_injects_attack_order_for_Hawk_player_into_pending_queue()
    {
        var world = NewWorld();
        var hawk = AddPlayer(world, "Hawk", 1000, 0, 1000, 0, 0, 1000);
        hawk.IsAi = true;
        hawk.AiPersonality = AiPersonality.Hawk;
        var enemy = AddPlayer(world, "Enemy");

        var hawkProv = AddProvince(world, hawk, "HawkLand");
        var enemyProv = AddProvince(world, enemy, "EnemyLand");
        AddUnit(world, hawk, hawkProv, UnitType.MechInfantry, 2000);
        AddUnit(world, enemy, enemyProv, UnitType.NationalGuard, 100);

        var units = world.Provinces.SelectMany(p => p.UnitsStationed).ToList();
        var ctx = Context(world,
            units: units,
            adjacencies: new[] { Adj(hawkProv, enemyProv) });

        new AiTurnStep().Execute(ctx);

        ctx.PendingUnitOrders.Should().ContainSingle()
            .Which.OrderType.Should().Be(OrderType.Attack);
        ctx.PendingConstructionOrders.Should().BeEmpty();
    }

    [Fact]
    public void AiTurn_is_noop_when_no_AI_players_present()
    {
        var world = NewWorld();
        AddPlayer(world, "Human");
        var ctx = Context(world);

        new AiTurnStep().Execute(ctx);

        ctx.PendingUnitOrders.Should().BeEmpty();
        ctx.PendingConstructionOrders.Should().BeEmpty();
    }
}
