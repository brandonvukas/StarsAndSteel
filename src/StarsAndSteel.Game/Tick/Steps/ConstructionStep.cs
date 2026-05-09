using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Orders;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Step 8 of the tick pipeline (docs/07 §"ConstructionStep"). Decrements
/// <see cref="ConstructionOrder.TicksRemaining"/> on every in-progress build order; on the
/// tick it hits zero, instantiates the unit or building, queues it for insert via
/// <see cref="TickContext.UnitsToInsert"/> / <see cref="TickContext.BuildingsToInsert"/>,
/// and marks the order <see cref="OrderStatus.Complete"/>.
/// <para/>
/// MVP: a build order's first tick of progress is the tick after submission. We tick down
/// even on the first eligible tick so a 1-tick build completes the same tick it becomes
/// eligible (matches the expectation that "5 ticks to build" actually takes 5 game ticks).
/// </summary>
public sealed class ConstructionStep : ITickStep
{
    public string Name => "Construction";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var order in context.PendingConstructionOrders)
        {
            if (order.Status == OrderStatus.Complete || order.Status == OrderStatus.Cancelled) continue;

            // Mark moved-into-progress (purely informational).
            if (order.Status == OrderStatus.Pending) order.Status = OrderStatus.InProgress;

            order.TicksRemaining = Math.Max(0, order.TicksRemaining - 1);
            if (order.TicksRemaining > 0) continue;

            // Completion. Find the destination province from the in-memory world graph.
            var province = context.World.Provinces.FirstOrDefault(p => p.Id == order.ProvinceId);
            if (province is null) { order.Status = OrderStatus.Cancelled; continue; }

            // If the province changed hands while we were building, cancel the order.
            // (No refund — MVP. Refunds land with the cancellation endpoint in Phase 2.)
            if (province.OwnerPlayerId != order.OwnerPlayerId) { order.Status = OrderStatus.Cancelled; continue; }

            switch (order.OrderType)
            {
                case OrderType.BuildUnit when order.UnitType.HasValue:
                    {
                        var spec = BuildCatalog.GetUnit(order.UnitType.Value);
                        var unit = new Unit
                        {
                            Id = Guid.NewGuid(),
                            GameWorldId = context.World.Id,
                            OwnerPlayerId = order.OwnerPlayerId,
                            LocationProvinceId = province.Id,
                            Type = order.UnitType.Value,
                            Domain = spec.Domain,
                            Strength = order.Quantity,
                            Morale = 100,
                            Experience = 0,
                            IsInTransit = false,
                            HomeBaseProvinceId = spec.Domain == UnitDomain.Air ? province.Id : null,
                        };
                        context.UnitsToInsert.Add(unit);
                        context.Units.Add(unit); // keep in-memory consistent for subsequent steps in this tick (none yet, but future-proof)
                        order.Status = OrderStatus.Complete;
                        context.Events.Add(new UnitBuiltEvent(
                            Tick: context.ProcessingTick,
                            UnitId: unit.Id,
                            OwnerPlayerId: unit.OwnerPlayerId,
                            ProvinceId: province.Id,
                            Type: unit.Type,
                            Strength: unit.Strength));
                        break;
                    }
                case OrderType.BuildBuilding when order.BuildingType.HasValue:
                    {
                        var building = new Building
                        {
                            Id = Guid.NewGuid(),
                            ProvinceId = province.Id,
                            Type = order.BuildingType.Value,
                            Level = 1,
                            ConstructedAtTick = context.ProcessingTick,
                        };
                        context.BuildingsToInsert.Add(building);
                        province.Buildings.Add(building); // in-memory consistency
                        order.Status = OrderStatus.Complete;
                        context.Events.Add(new BuildingCompletedEvent(
                            Tick: context.ProcessingTick,
                            BuildingId: building.Id,
                            OwnerPlayerId: order.OwnerPlayerId,
                            ProvinceId: province.Id,
                            Type: building.Type,
                            Level: building.Level));
                        break;
                    }
                default:
                    order.Status = OrderStatus.Cancelled;
                    break;
            }
        }
    }
}
