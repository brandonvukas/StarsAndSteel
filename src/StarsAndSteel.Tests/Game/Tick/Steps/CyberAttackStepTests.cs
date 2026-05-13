using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

/// <summary>
/// Phase 3d: cover the CyberAttackStep tick logic. Uses a stub <see cref="IRandomSource"/>
/// so the rolled effect (DrainMoney vs SlowResearch) is deterministic per test, decoupling
/// the assertion from the per-world RNG state.
/// </summary>
public class CyberAttackStepTests
{
    /// <summary>Stub RNG that returns a fixed <c>NextDouble</c> so the 50/50 effect roll
    /// resolves to a known branch (&lt; 0.5 = SlowResearch, &gt;= 0.5 = DrainMoney).</summary>
    private sealed class FixedRng : IRandomSource
    {
        private readonly double _value;
        public FixedRng(double value) => _value = value;
        public long State => 0;
        public int NextInt(int exclusiveMax) => 0;
        public double NextDouble() => _value;
    }

    private static TickContext BuildContext(
        GameWorld world,
        IList<CyberAttackOrder> cyberOrders,
        IList<ResearchProgress>? activeResearch = null,
        double rngRoll = 0.75)
    {
        return new TickContext(
            world,
            processingTick: world.CurrentTick + 1,
            rng: new FixedRng(rngRoll),
            units: new List<Unit>(),
            pendingUnitOrders: new List<UnitOrder>(),
            pendingConstructionOrders: new List<ConstructionOrder>(),
            adjacencies: new List<ProvinceAdjacency>(),
            pendingTreatyOffers: null,
            relations: null,
            activeResearch: activeResearch ?? new List<ResearchProgress>(),
            pendingCyberAttackOrders: cyberOrders);
    }

    private static CyberAttackOrder MakeOrder(GameWorld world, Player attacker, Province launch, Province target) => new()
    {
        Id = Guid.NewGuid(),
        GameWorldId = world.Id,
        AttackerPlayerId = attacker.Id,
        LaunchProvinceId = launch.Id,
        TargetProvinceId = target.Id,
        EffectKind = null,
        IssuedAtTick = world.CurrentTick + 1,
        Status = OrderStatus.Pending,
    };

    [Fact]
    public void DrainMoney_subtracts_fixed_amount_from_target_owner()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob", money: 10_000, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var order = MakeOrder(world, alice, alicePr, bobPr);
        // 0.75 >= 0.5 → DrainMoney
        var ctx = BuildContext(world, new[] { order }, rngRoll: 0.75);

        new CyberAttackStep().Execute(ctx);

        bob.Money.Should().Be(10_000 - 1500);
        order.Status.Should().Be(OrderStatus.Complete);
        order.EffectKind.Should().Be(CyberEffectKind.DrainMoney);
        var ev = ctx.Events.OfType<CyberAttackResolvedEvent>().Should().ContainSingle().Subject;
        ev.EffectKind.Should().Be(CyberEffectKind.DrainMoney);
        ev.MoneyDrained.Should().Be(1500);
        ev.TargetPlayerId.Should().Be(bob.Id);
    }

    [Fact]
    public void DrainMoney_clamps_at_zero()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob", money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var order = MakeOrder(world, alice, alicePr, bobPr);
        var ctx = BuildContext(world, new[] { order }, rngRoll: 0.9);

        new CyberAttackStep().Execute(ctx);

        bob.Money.Should().Be(0);
        var ev = ctx.Events.OfType<CyberAttackResolvedEvent>().Single();
        ev.MoneyDrained.Should().Be(100);
    }

    [Fact]
    public void SlowResearch_subtracts_progress_from_highest_progress_row()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var lowProgress = new ResearchProgress { Id = Guid.NewGuid(), PlayerId = bob.Id, TechId = "advanced_armor", ProgressPoints = 100, IsUnlocked = false };
        var highProgress = new ResearchProgress { Id = Guid.NewGuid(), PlayerId = bob.Id, TechId = "smart_munitions", ProgressPoints = 500, IsUnlocked = false };
        var order = MakeOrder(world, alice, alicePr, bobPr);
        // 0.25 < 0.5 → SlowResearch
        var ctx = BuildContext(world, new[] { order },
            activeResearch: new[] { lowProgress, highProgress },
            rngRoll: 0.25);

        new CyberAttackStep().Execute(ctx);

        highProgress.ProgressPoints.Should().Be(500 - 200);
        lowProgress.ProgressPoints.Should().Be(100); // untouched
        order.EffectKind.Should().Be(CyberEffectKind.SlowResearch);
        var ev = ctx.Events.OfType<CyberAttackResolvedEvent>().Single();
        ev.AffectedTechId.Should().Be("smart_munitions");
        ev.ResearchPointsLost.Should().Be(200);
    }

    [Fact]
    public void SlowResearch_no_op_when_target_has_no_active_research()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob", money: 10_000, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var order = MakeOrder(world, alice, alicePr, bobPr);
        var ctx = BuildContext(world, new[] { order }, rngRoll: 0.1);

        new CyberAttackStep().Execute(ctx);

        // Effect was rolled to SlowResearch but no rows existed → no-op (no money drain
        // either; we don't reroll, that would let the attacker game the effect).
        bob.Money.Should().Be(10_000);
        order.Status.Should().Be(OrderStatus.Complete);
        order.EffectKind.Should().Be(CyberEffectKind.SlowResearch);
        var ev = ctx.Events.OfType<CyberAttackResolvedEvent>().Single();
        ev.ResearchPointsLost.Should().Be(0);
        ev.AffectedTechId.Should().BeNull();
    }

    [Fact]
    public void Order_fizzles_when_target_province_has_no_owner_at_resolve_time()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var alicePr = AddProvince(world, alice, "AlicePr");
        // Target province captured/abandoned between submission and resolution.
        var orphan = AddProvince(world, owner: null, "Orphan");
        var order = MakeOrder(world, alice, alicePr, orphan);
        var ctx = BuildContext(world, new[] { order });

        new CyberAttackStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Complete);
        order.EffectKind.Should().BeNull(); // never rolled — target was unowned
        ctx.Events.OfType<CyberAttackResolvedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Skips_orders_with_non_pending_status()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob", money: 10_000, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var order = MakeOrder(world, alice, alicePr, bobPr);
        order.Status = OrderStatus.Complete; // already resolved
        var ctx = BuildContext(world, new[] { order }, rngRoll: 0.75);

        new CyberAttackStep().Execute(ctx);

        bob.Money.Should().Be(10_000); // untouched
        ctx.Events.Should().BeEmpty();
    }
}
