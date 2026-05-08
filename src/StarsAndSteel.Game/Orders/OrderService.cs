using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Orders;

/// <summary>
/// Why an order submission failed. The controller maps these to HTTP status codes per
/// <c>docs/06-BACKEND-API.md</c> §"Order validation rules". This is exhaustive on purpose
/// so we can pattern-match without a default branch.
/// </summary>
public enum OrderRejectionReason
{
    UnitNotOwnedByCaller,           // 403
    UnitInTransit,                  // 400
    TargetProvinceNotAdjacent,      // 400
    AirUnitNotAtAirBase,            // 400
    UnitDomainMismatch,             // 400 — e.g. ground unit ordered to airstrike
    AirStrikeOutOfRange,            // 400 — Phase 2 (range model not in MVP)
    ProvinceNotOwnedByCaller,       // 403
    RequiredBuildingMissing,        // 400
    InsufficientResources,          // 409
    UnitNotInCatalogue,             // 400
    BuildingNotInCatalogue,         // 400
    QuantityOutOfRange,             // 400
    GameEnded,                      // 409
    UnknownUnit,                    // 404
    UnknownProvince,                // 404
    UnitTypeRequiresAirBase,        // 400
}

/// <summary>
/// Result of a pure order-validation pass. Exactly one of <see cref="Order"/> or
/// <see cref="Rejection"/> is set. The controller persists <see cref="Order"/> +
/// the resource debit in one SaveChanges, or maps <see cref="Rejection"/> to a
/// problem response.
/// </summary>
public sealed record OrderValidationResult(
    UnitOrder? UnitOrder,
    ConstructionOrder? ConstructionOrder,
    OrderRejectionReason? Rejection,
    string? RejectionMessage)
{
    public static OrderValidationResult Accept(UnitOrder order) =>
        new(order, null, null, null);

    public static OrderValidationResult Accept(ConstructionOrder order) =>
        new(null, order, null, null);

    public static OrderValidationResult Reject(OrderRejectionReason reason, string message) =>
        new(null, null, reason, message);

    public bool IsAccepted => Rejection is null;
}

/// <summary>
/// Pure (no DbContext, no I/O) validation + construction of <see cref="UnitOrder"/> and
/// <see cref="ConstructionOrder"/> rows. The controller is responsible for loading the
/// referenced entities, holding the per-world tick lock, persisting the result, and
/// debiting player resources for build orders.
/// <para/>
/// All methods return an <see cref="OrderValidationResult"/>; nothing throws on bad
/// input — that's the caller's signal to issue a 4xx.
/// </summary>
public sealed class OrderService
{
    /// <summary>
    /// Move a ground unit to an adjacent province. <paramref name="adjacentProvinceIds"/>
    /// is the precomputed set of provinces adjacent to <paramref name="unit"/>'s current
    /// location (queried by the controller from <c>ProvinceAdjacencies</c>).
    /// </summary>
    public OrderValidationResult ValidateMove(
        Unit unit,
        Player caller,
        Province targetProvince,
        IReadOnlySet<Guid> adjacentProvinceIds,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (unit.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitNotOwnedByCaller, "You do not own this unit.");

        if (unit.Domain != UnitDomain.Ground)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Only ground units can move.");

        if (unit.IsInTransit)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitInTransit, "Unit is already in transit.");

        if (!adjacentProvinceIds.Contains(targetProvince.Id))
            return OrderValidationResult.Reject(OrderRejectionReason.TargetProvinceNotAdjacent, "Target province is not adjacent.");

        return OrderValidationResult.Accept(new UnitOrder
        {
            Id = Guid.NewGuid(),
            UnitId = unit.Id,
            OrderType = OrderType.Move,
            TargetProvinceId = targetProvince.Id,
            IssuedAtTick = currentTick + 1,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>Ground attack into an adjacent province. Same shape as Move; combat resolves on arrival.</summary>
    public OrderValidationResult ValidateAttack(
        Unit unit,
        Player caller,
        Province targetProvince,
        IReadOnlySet<Guid> adjacentProvinceIds,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (unit.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitNotOwnedByCaller, "You do not own this unit.");

        if (unit.Domain != UnitDomain.Ground)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Only ground units can attack.");

        if (unit.IsInTransit)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitInTransit, "Unit is already in transit.");

        if (!adjacentProvinceIds.Contains(targetProvince.Id))
            return OrderValidationResult.Reject(OrderRejectionReason.TargetProvinceNotAdjacent, "Target province is not adjacent.");

        return OrderValidationResult.Accept(new UnitOrder
        {
            Id = Guid.NewGuid(),
            UnitId = unit.Id,
            OrderType = OrderType.Attack,
            TargetProvinceId = targetProvince.Id,
            IssuedAtTick = currentTick + 1,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>
    /// Air strike. Per docs/06: rejected if the air unit isn't stationed at a province with
    /// an Air Base. <paramref name="hostingBuildings"/> is the set of buildings at the unit's
    /// current location (the caller has loaded them).
    /// </summary>
    public OrderValidationResult ValidateAirStrike(
        Unit unit,
        Player caller,
        Province targetProvince,
        IReadOnlyCollection<Building> hostingBuildings,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (unit.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitNotOwnedByCaller, "You do not own this unit.");

        if (unit.Domain != UnitDomain.Air)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Only air units can perform air strikes.");

        if (unit.IsInTransit)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitInTransit, "Unit is already in transit.");

        if (!hostingBuildings.Any(b => b.Type == BuildingType.AirBase))
            return OrderValidationResult.Reject(OrderRejectionReason.AirUnitNotAtAirBase, "Air unit must be stationed at a province with an Air Base.");

        // Range check is Phase 2 (no per-air-unit range model in MVP). Documented in docs/06.

        return OrderValidationResult.Accept(new UnitOrder
        {
            Id = Guid.NewGuid(),
            UnitId = unit.Id,
            OrderType = OrderType.AirStrike,
            TargetProvinceId = targetProvince.Id,
            IssuedAtTick = currentTick + 1,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>
    /// Validate + construct (but do not persist) a build-unit order. Caller debits resources
    /// from <paramref name="caller"/> on accept.
    /// </summary>
    public OrderValidationResult ValidateBuildUnit(
        Player caller,
        Province province,
        UnitType unitType,
        int quantity,
        IReadOnlyCollection<Building> provinceBuildings,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (province.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.ProvinceNotOwnedByCaller, "You do not own this province.");

        if (!BuildCatalog.IsUnitBuildable(unitType))
            return OrderValidationResult.Reject(OrderRejectionReason.UnitNotInCatalogue, $"Unit type {unitType} is not buildable in MVP.");

        if (quantity < 1 || quantity > 10000)
            return OrderValidationResult.Reject(OrderRejectionReason.QuantityOutOfRange, "Quantity must be between 1 and 10000.");

        var spec = BuildCatalog.GetUnit(unitType);

        // Required building at this province.
        if (!provinceBuildings.Any(b => b.Type == spec.RequiredBuilding))
            return OrderValidationResult.Reject(OrderRejectionReason.RequiredBuildingMissing,
                $"Province requires a {spec.RequiredBuilding} to build {unitType}.");

        // Costs scale linearly with stack size (1000-strength baseline).
        var costFactor = quantity / 1000.0;
        long money       = (long)Math.Ceiling(spec.Money       * costFactor);
        long oil         = (long)Math.Ceiling(spec.Oil         * costFactor);
        long steel       = (long)Math.Ceiling(spec.Steel       * costFactor);
        long electronics = (long)Math.Ceiling(spec.Electronics * costFactor);
        long food        = (long)Math.Ceiling(spec.Food        * costFactor);
        long manpower    = (long)Math.Ceiling(spec.Manpower    * costFactor);

        if (caller.Money < money || caller.Oil < oil || caller.Steel < steel ||
            caller.Electronics < electronics || caller.Food < food || caller.Manpower < manpower)
        {
            return OrderValidationResult.Reject(OrderRejectionReason.InsufficientResources,
                $"Insufficient resources to build {quantity}x {unitType}.");
        }

        // Caller must apply the debit.
        return OrderValidationResult.Accept(new ConstructionOrder
        {
            Id = Guid.NewGuid(),
            GameWorldId = province.GameWorldId,
            OwnerPlayerId = caller.Id,
            ProvinceId = province.Id,
            OrderType = OrderType.BuildUnit,
            UnitType = unitType,
            Quantity = quantity,
            BuildingType = null,
            IssuedAtTick = currentTick + 1,
            TicksRemaining = spec.TicksToBuild,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>Validate + construct (but do not persist) a build-building order.</summary>
    public OrderValidationResult ValidateBuildBuilding(
        Player caller,
        Province province,
        BuildingType buildingType,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (province.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.ProvinceNotOwnedByCaller, "You do not own this province.");

        if (!BuildCatalog.IsBuildingBuildable(buildingType))
            return OrderValidationResult.Reject(OrderRejectionReason.BuildingNotInCatalogue, $"Building type {buildingType} is not buildable in MVP.");

        var spec = BuildCatalog.GetBuilding(buildingType);

        if (caller.Money < spec.Money || caller.Oil < spec.Oil || caller.Steel < spec.Steel ||
            caller.Electronics < spec.Electronics || caller.Food < spec.Food || caller.Manpower < spec.Manpower)
        {
            return OrderValidationResult.Reject(OrderRejectionReason.InsufficientResources,
                $"Insufficient resources to build {buildingType}.");
        }

        return OrderValidationResult.Accept(new ConstructionOrder
        {
            Id = Guid.NewGuid(),
            GameWorldId = province.GameWorldId,
            OwnerPlayerId = caller.Id,
            ProvinceId = province.Id,
            OrderType = OrderType.BuildBuilding,
            UnitType = null,
            Quantity = 1,
            BuildingType = buildingType,
            IssuedAtTick = currentTick + 1,
            TicksRemaining = spec.TicksToBuild,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>
    /// Apply the resource debit corresponding to an accepted ConstructionOrder. Mutates the
    /// player row in place. Caller is responsible for SaveChanges.
    /// </summary>
    public static void DebitForBuild(Player caller, ConstructionOrder order)
    {
        if (order.OrderType == OrderType.BuildUnit)
        {
            var spec = BuildCatalog.GetUnit(order.UnitType!.Value);
            var f = order.Quantity / 1000.0;
            caller.Money       -= (long)Math.Ceiling(spec.Money       * f);
            caller.Oil         -= (long)Math.Ceiling(spec.Oil         * f);
            caller.Steel       -= (long)Math.Ceiling(spec.Steel       * f);
            caller.Electronics -= (long)Math.Ceiling(spec.Electronics * f);
            caller.Food        -= (long)Math.Ceiling(spec.Food        * f);
            caller.Manpower    -= (long)Math.Ceiling(spec.Manpower    * f);
        }
        else if (order.OrderType == OrderType.BuildBuilding)
        {
            var spec = BuildCatalog.GetBuilding(order.BuildingType!.Value);
            caller.Money       -= spec.Money;
            caller.Oil         -= spec.Oil;
            caller.Steel       -= spec.Steel;
            caller.Electronics -= spec.Electronics;
            caller.Food        -= spec.Food;
            caller.Manpower    -= spec.Manpower;
        }
        else
        {
            throw new ArgumentException($"DebitForBuild called with non-build order type {order.OrderType}.", nameof(order));
        }
    }
}
