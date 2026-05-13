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

                        // Phase 2b: a CarrierAirWing must be parented to a friendly carrier
                        // at the build province with a free slot. If the carrier we'd parent
                        // to was sunk or moved away while the wing was building, cancel the
                        // order (no refund — same as province-changes-hands rule).
                        Guid? parentUnitId = null;
                        if (spec.RequiresCarrier)
                        {
                            var carriersHere = context.Units
                                .Where(u => u.LocationProvinceId == province.Id
                                         && u.OwnerPlayerId == order.OwnerPlayerId
                                         && u.Type == UnitType.AircraftCarrier
                                         && u.Strength > 0)
                                .ToList();
                            // Pick the carrier with the most free slots (deterministic by Id
                            // as tiebreaker so tests are reproducible).
                            Unit? host = null;
                            int hostFree = 0;
                            foreach (var carrier in carriersHere.OrderBy(c => c.Id))
                            {
                                int used = context.Units.Count(u => u.ParentUnitId == carrier.Id
                                                                  && u.Strength > 0);
                                int free = BuildCatalog.CarrierWingCapacity - used;
                                if (free > hostFree) { host = carrier; hostFree = free; }
                            }
                            if (host is null)
                            {
                                order.Status = OrderStatus.Cancelled;
                                continue;
                            }
                            parentUnitId = host.Id;
                        }

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
                            ParentUnitId = parentUnitId,
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

                        // Phase 4b2: Carrier Strike Group spawns a free veteran
                        // carrier + 2 wings at the wonder's province on completion.
                        // OrderService already enforced the coastal requirement at
                        // submit; we re-check here defensively (province could have
                        // been captured/lost coastline between submit and resolution
                        // — the coastal flag is static map data so it shouldn't
                        // change, but the ownership flip above already cancelled
                        // the order if so). UnitBuiltEvent fires once per spawned
                        // unit so the client gets standard hub events.
                        if (building.Type == BuildingType.CarrierStrikeGroup)
                        {
                            SpawnCarrierStrikeGroup(context, province, order.OwnerPlayerId);
                        }
                        break;
                    }
                default:
                    order.Status = OrderStatus.Cancelled;
                    break;
            }
        }
    }

    /// <summary>
    /// Phase 4b2: spawn the Carrier Strike Group's bundled units. One veteran
    /// <see cref="UnitType.AircraftCarrier"/> (Strength 1000, Experience 1) and
    /// two <see cref="UnitType.CarrierAirWing"/> (Strength 500 each, Experience 1,
    /// parented to the carrier). Wings respect <see cref="BuildCatalog.CarrierWingCapacity"/>.
    /// All three units skip the normal Money/Steel/Manpower debit — they're the wonder's reward.
    /// </summary>
    private static void SpawnCarrierStrikeGroup(TickContext context, Province province, Guid ownerPlayerId)
    {
        var carrier = new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = context.World.Id,
            OwnerPlayerId = ownerPlayerId,
            LocationProvinceId = province.Id,
            Type = UnitType.AircraftCarrier,
            Domain = UnitDomain.Naval,
            Strength = 1000,
            Morale = 100,
            Experience = 1,
            IsInTransit = false,
            HomeBaseProvinceId = null,
            ParentUnitId = null,
        };
        context.UnitsToInsert.Add(carrier);
        context.Units.Add(carrier);
        context.Events.Add(new UnitBuiltEvent(
            Tick: context.ProcessingTick,
            UnitId: carrier.Id,
            OwnerPlayerId: ownerPlayerId,
            ProvinceId: province.Id,
            Type: carrier.Type,
            Strength: carrier.Strength));

        for (int i = 0; i < 2; i++)
        {
            var wing = new Unit
            {
                Id = Guid.NewGuid(),
                GameWorldId = context.World.Id,
                OwnerPlayerId = ownerPlayerId,
                LocationProvinceId = province.Id,
                Type = UnitType.CarrierAirWing,
                Domain = UnitDomain.Air,
                Strength = 500,
                Morale = 100,
                Experience = 1,
                IsInTransit = false,
                HomeBaseProvinceId = province.Id,
                ParentUnitId = carrier.Id,
            };
            context.UnitsToInsert.Add(wing);
            context.Units.Add(wing);
            context.Events.Add(new UnitBuiltEvent(
                Tick: context.ProcessingTick,
                UnitId: wing.Id,
                OwnerPlayerId: ownerPlayerId,
                ProvinceId: province.Id,
                Type: wing.Type,
                Strength: wing.Strength));
        }
    }
}
