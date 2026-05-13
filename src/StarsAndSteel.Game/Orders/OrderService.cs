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
    NoCarrierWithSpareCapacity,     // 400 — Phase 2b
    NukesDisabledForWorld,          // 409 — Phase 3a; world has NukesEnabled=false
    MissileSiloMissing,             // 400 — Phase 3a; launch province has no MissileSilo
    RequiredTechMissing,            // 409 — Phase 3b; build requires an unlocked tech
    CyberOpsCenterMissing,          // 400 — Phase 3d; launch province has no CyberOperationsCenter
    CyberCannotTargetSelf,          // 400 — Phase 3d; attacker may not cyber their own province
    CyberTargetUnowned,             // 400 — Phase 3d; target province has no owner (nothing to drain)
    SabotageRequiresSpecialForces,  // 400 — Phase 3e; only SpecialForces units may sabotage
    SabotageTargetHasNoBuildings,   // 400 — Phase 3e; nothing to destroy
    SabotageTargetNotEnemy,         // 400 — Phase 3e; target province must be owned by an enemy
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
    CyberAttackOrder? CyberAttackOrder,
    OrderRejectionReason? Rejection,
    string? RejectionMessage)
{
    public static OrderValidationResult Accept(UnitOrder order) =>
        new(order, null, null, null, null);

    public static OrderValidationResult Accept(ConstructionOrder order) =>
        new(null, order, null, null, null);

    public static OrderValidationResult Accept(CyberAttackOrder order) =>
        new(null, null, order, null, null);

    public static OrderValidationResult Reject(OrderRejectionReason reason, string message) =>
        new(null, null, null, reason, message);

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

        if (unit.Domain != UnitDomain.Ground && unit.Domain != UnitDomain.Naval)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Only ground or naval units can move.");

        if (unit.IsInTransit)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitInTransit, "Unit is already in transit.");

        if (!adjacentProvinceIds.Contains(targetProvince.Id))
            return OrderValidationResult.Reject(OrderRejectionReason.TargetProvinceNotAdjacent, "Target province is not adjacent.");

        // Phase 2I: naval may only traverse coastal-to-coastal edges (no inland).
        if (unit.Domain == UnitDomain.Naval && !targetProvince.IsCoastal)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Naval units can only move to coastal provinces.");

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

        if (unit.Domain != UnitDomain.Ground && unit.Domain != UnitDomain.Naval)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Only ground or naval units can attack.");

        if (unit.IsInTransit)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitInTransit, "Unit is already in transit.");

        if (!adjacentProvinceIds.Contains(targetProvince.Id))
            return OrderValidationResult.Reject(OrderRejectionReason.TargetProvinceNotAdjacent, "Target province is not adjacent.");

        // Phase 2I: naval may only attack into a coastal province.
        if (unit.Domain == UnitDomain.Naval && !targetProvince.IsCoastal)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Naval units can only attack coastal provinces.");

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
    /// Phase 3e: a Special Forces ground unit conducts a covert sabotage raid
    /// against an adjacent enemy-owned province. Validation checks:
    /// <list type="bullet">
    ///   <item>Caller owns the unit; unit is <see cref="UnitType.SpecialForces"/> and not in transit.</item>
    ///   <item>Target is adjacent to the SF's current province.</item>
    ///   <item>Target has an owner that is not the caller (no friendly-fire sabotage).</item>
    ///   <item>Target has at least one building to destroy.</item>
    /// </list>
    /// The actual building loss + casualty roll happens in <c>SabotageStep</c> at
    /// resolution time so we don't need to re-load the target's buildings here for
    /// the destruction logic — only to verify there's anything worth sabotaging.
    /// </summary>
    public OrderValidationResult ValidateSabotage(
        Unit unit,
        Player caller,
        Province targetProvince,
        IReadOnlySet<Guid> adjacentProvinceIds,
        IReadOnlyCollection<Building> targetProvinceBuildings,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (unit.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitNotOwnedByCaller, "You do not own this unit.");

        if (unit.Type != UnitType.SpecialForces)
            return OrderValidationResult.Reject(OrderRejectionReason.SabotageRequiresSpecialForces,
                "Only Special Forces units can perform sabotage.");

        if (unit.IsInTransit)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitInTransit, "Unit is already in transit.");

        if (!adjacentProvinceIds.Contains(targetProvince.Id))
            return OrderValidationResult.Reject(OrderRejectionReason.TargetProvinceNotAdjacent, "Target province is not adjacent.");

        if (targetProvince.OwnerPlayerId is null || targetProvince.OwnerPlayerId == caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.SabotageTargetNotEnemy,
                "Target province must be owned by an enemy.");

        if (targetProvinceBuildings.Count == 0)
            return OrderValidationResult.Reject(OrderRejectionReason.SabotageTargetHasNoBuildings,
                "Target province has no buildings to sabotage.");

        return OrderValidationResult.Accept(new UnitOrder
        {
            Id = Guid.NewGuid(),
            UnitId = unit.Id,
            OrderType = OrderType.Sabotage,
            TargetProvinceId = targetProvince.Id,
            IssuedAtTick = currentTick + 1,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>
    /// Phase 3a: launch a stockpiled missile (CruiseMissile / NuclearMissile) at any
    /// province (global range — no adjacency check). The launching unit must:
    /// <list type="bullet">
    ///   <item>be owned by the caller and of <see cref="UnitDomain.Missile"/></item>
    ///   <item>be stationed at a province hosting a friendly <see cref="BuildingType.MissileSilo"/></item>
    ///   <item>not be in transit (defensive — missiles don't move conventionally)</item>
    /// </list>
    /// Nuclear warheads additionally require <see cref="GameWorld.NukesEnabled"/>.
    /// The order is consumed by <c>MissileImpactStep</c>, which deletes the missile stack
    /// and applies damage / radiation at the target.
    /// </summary>
    public OrderValidationResult ValidateMissileLaunch(
        Unit unit,
        Player caller,
        Province launchProvince,
        Province targetProvince,
        IReadOnlyCollection<Building> launchProvinceBuildings,
        bool nukesEnabledForWorld,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (unit.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitNotOwnedByCaller, "You do not own this unit.");

        if (unit.Domain != UnitDomain.Missile)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitDomainMismatch, "Only missile units can be launched.");

        if (unit.IsInTransit)
            return OrderValidationResult.Reject(OrderRejectionReason.UnitInTransit, "Unit is in transit.");

        if (unit.LocationProvinceId != launchProvince.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.UnknownProvince, "Missile is not at the specified launch province.");

        if (!launchProvinceBuildings.Any(b => b.Type == BuildingType.MissileSilo))
            return OrderValidationResult.Reject(OrderRejectionReason.MissileSiloMissing,
                "Launch province must have a Missile Silo.");

        if (unit.Type == UnitType.NuclearMissile && !nukesEnabledForWorld)
            return OrderValidationResult.Reject(OrderRejectionReason.NukesDisabledForWorld,
                "Nuclear weapons are disabled in this world.");

        return OrderValidationResult.Accept(new UnitOrder
        {
            Id = Guid.NewGuid(),
            UnitId = unit.Id,
            OrderType = OrderType.MissileLaunch,
            TargetProvinceId = targetProvince.Id,
            IssuedAtTick = currentTick + 1,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>
    /// Phase 3d: validate a player-level cyber attack. Requires:
    /// <list type="bullet">
    ///   <item>The launch province is owned by the caller.</item>
    ///   <item>The launch province has at least one <see cref="BuildingType.CyberOperationsCenter"/>.</item>
    ///   <item>The caller has unlocked the <c>cyber_warfare</c> tech.</item>
    ///   <item>The target province has an owner (cannot drain an unowned province).</item>
    ///   <item>The target's owner is not the caller (no self-cyber).</item>
    /// </list>
    /// Resource cost (money + electronics) is the controller's responsibility — pass
    /// <paramref name="caller"/> with sufficient resources or pre-check before calling.
    /// The actual effect (slow research vs. drain money) is rolled at resolution time
    /// by <c>CyberAttackStep</c>; this method only constructs the pending order row.
    /// </summary>
    public OrderValidationResult ValidateCyberAttack(
        Player caller,
        Province launchProvince,
        Province targetProvince,
        IReadOnlyCollection<Building> launchProvinceBuildings,
        IReadOnlyCollection<string> unlockedTechIds,
        int currentTick,
        GameWorldStatus worldStatus)
    {
        if (worldStatus == GameWorldStatus.Ended)
            return OrderValidationResult.Reject(OrderRejectionReason.GameEnded, "World has ended.");

        if (launchProvince.OwnerPlayerId != caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.ProvinceNotOwnedByCaller,
                "You do not own the launch province.");

        if (!launchProvinceBuildings.Any(b => b.Type == BuildingType.CyberOperationsCenter))
            return OrderValidationResult.Reject(OrderRejectionReason.CyberOpsCenterMissing,
                "Launch province must have a Cyber Operations Center.");

        if (!unlockedTechIds.Contains("cyber_warfare", StringComparer.Ordinal))
            return OrderValidationResult.Reject(OrderRejectionReason.RequiredTechMissing,
                "Cyber Warfare tech is required to launch cyber attacks.");

        if (targetProvince.OwnerPlayerId is null)
            return OrderValidationResult.Reject(OrderRejectionReason.CyberTargetUnowned,
                "Target province has no owner.");

        if (targetProvince.OwnerPlayerId == caller.Id)
            return OrderValidationResult.Reject(OrderRejectionReason.CyberCannotTargetSelf,
                "Cannot launch a cyber attack against your own province.");

        if (caller.Money < CyberAttackMoneyCost || caller.Electronics < CyberAttackElectronicsCost)
            return OrderValidationResult.Reject(OrderRejectionReason.InsufficientResources,
                $"Cyber attack requires {CyberAttackMoneyCost} money and {CyberAttackElectronicsCost} electronics.");

        return OrderValidationResult.Accept(new CyberAttackOrder
        {
            Id = Guid.NewGuid(),
            GameWorldId = launchProvince.GameWorldId,
            AttackerPlayerId = caller.Id,
            LaunchProvinceId = launchProvince.Id,
            TargetProvinceId = targetProvince.Id,
            EffectKind = null,
            IssuedAtTick = currentTick + 1,
            Status = OrderStatus.Pending,
        });
    }

    /// <summary>Money cost debited from the attacker on a successful CyberAttack submission (Phase 3d).</summary>
    public const long CyberAttackMoneyCost = 500;

    /// <summary>Electronics cost debited from the attacker on a successful CyberAttack submission (Phase 3d).</summary>
    public const long CyberAttackElectronicsCost = 200;

    /// <summary>
    /// Phase 3d: subtract the cyber-attack submission cost from the attacker. Mirrors
    /// <see cref="DebitForBuild"/> in shape — controller calls this on a tracked Player
    /// row right before <c>SaveChangesAsync</c>.
    /// </summary>
    public static void DebitForCyberAttack(Player caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Money -= CyberAttackMoneyCost;
        caller.Electronics -= CyberAttackElectronicsCost;
    }

    /// <summary>
    /// Air strike. Per docs/06: rejected if the air unit isn't stationed at a province with
    /// an Air Base. <paramref name="hostingBuildings"/> is the set of buildings at the unit's
    /// current location (the caller has loaded them).
    /// <para/>
    /// Phase 2b: a <see cref="UnitType.CarrierAirWing"/> may sortie WITHOUT an AirBase
    /// at its current province as long as its parent carrier is also there (which it
    /// always is, since wings move with the carrier — but we still verify defensively).
    /// </summary>
    public OrderValidationResult ValidateAirStrike(
        Unit unit,
        Player caller,
        Province targetProvince,
        IReadOnlyCollection<Building> hostingBuildings,
        IReadOnlyCollection<Unit> hostingUnits,
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

        // Phase 2b: a CarrierAirWing's "airbase" is its parent carrier — it doesn't
        // need a building at its current province. Verify the parent carrier is
        // present and alive at the same location (defensive — MovementStep keeps
        // them co-located, so this would only fail if data is corrupt).
        bool isCarrierWing = unit.Type == UnitType.CarrierAirWing;
        if (isCarrierWing)
        {
            var carrier = hostingUnits.FirstOrDefault(u =>
                u.Id == unit.ParentUnitId
                && u.Type == UnitType.AircraftCarrier
                && u.OwnerPlayerId == caller.Id
                && u.Strength > 0);
            if (carrier is null)
            {
                return OrderValidationResult.Reject(OrderRejectionReason.AirUnitNotAtAirBase,
                    "Carrier-air-wing has no parent carrier at its location.");
            }
        }
        else if (!hostingBuildings.Any(b => b.Type == BuildingType.AirBase))
        {
            return OrderValidationResult.Reject(OrderRejectionReason.AirUnitNotAtAirBase, "Air unit must be stationed at a province with an Air Base.");
        }

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
    /// <para/>
    /// <paramref name="provinceUnits"/> is the set of units currently in the build province
    /// (any owner). Used by Phase 2b to verify a friendly <see cref="UnitType.AircraftCarrier"/>
    /// with spare wing capacity exists when building a <see cref="UnitType.CarrierAirWing"/>.
    /// <paramref name="pendingCarrierWingOrders"/> are the in-flight wing builds targeting
    /// this province so spam-queueing wings can't bypass the cap.
    /// </summary>
    public OrderValidationResult ValidateBuildUnit(
        Player caller,
        Province province,
        UnitType unitType,
        int quantity,
        IReadOnlyCollection<Building> provinceBuildings,
        IReadOnlyCollection<Unit> provinceUnits,
        IReadOnlyCollection<ConstructionOrder> pendingCarrierWingOrders,
        int currentTick,
        GameWorldStatus worldStatus,
        IReadOnlyCollection<string>? unlockedTechIds = null)
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

        // Phase 3b: tech-gated units (Stealth Bomber, Stealth Drone, ...) require an
        // unlocked ResearchProgress row for spec.RequiredTechId. Null collection means
        // legacy callers/tests that pre-date this gate; we treat null as "no techs",
        // which means tech-gated units will be rejected unless the caller passes the
        // list. All Phase 3b+ callers must populate it.
        if (spec.RequiredTechId is not null)
        {
            var techs = unlockedTechIds ?? Array.Empty<string>();
            if (!techs.Contains(spec.RequiredTechId))
            {
                return OrderValidationResult.Reject(OrderRejectionReason.RequiredTechMissing,
                    $"{unitType} requires the '{spec.RequiredTechId}' tech to be researched.");
            }
        }

        // Phase 2b: CarrierAirWing requires a friendly carrier present with spare slot
        // capacity. We count both already-embarked wings AND in-flight wing build
        // orders at this province against each carrier (worst-case attribution: the
        // pending orders are unattributed, so we just require total free capacity
        // across all carriers >= 1 + pending count).
        if (spec.RequiresCarrier)
        {
            var carriers = provinceUnits
                .Where(u => u.Type == UnitType.AircraftCarrier
                         && u.OwnerPlayerId == caller.Id
                         && u.Strength > 0)
                .ToArray();
            if (carriers.Length == 0)
            {
                return OrderValidationResult.Reject(OrderRejectionReason.NoCarrierWithSpareCapacity,
                    $"{unitType} requires a friendly Aircraft Carrier docked at this province.");
            }

            int totalCapacity = carriers.Length * BuildCatalog.CarrierWingCapacity;
            int wingsHere = provinceUnits.Count(u => u.Type == UnitType.CarrierAirWing
                                                   && u.OwnerPlayerId == caller.Id
                                                   && u.Strength > 0
                                                   && u.ParentUnitId.HasValue
                                                   && carriers.Any(c => c.Id == u.ParentUnitId.Value));
            int pending = pendingCarrierWingOrders.Count(o =>
                o.ProvinceId == province.Id &&
                o.OwnerPlayerId == caller.Id &&
                o.UnitType == UnitType.CarrierAirWing &&
                o.Status != OrderStatus.Complete &&
                o.Status != OrderStatus.Cancelled);
            if (wingsHere + pending >= totalCapacity)
            {
                return OrderValidationResult.Reject(OrderRejectionReason.NoCarrierWithSpareCapacity,
                    "Carrier(s) at this province have no spare wing slots.");
            }
        }

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

        // Phase 2I: NavalYard is only buildable in coastal provinces.
        if (buildingType == BuildingType.NavalYard && !province.IsCoastal)
            return OrderValidationResult.Reject(OrderRejectionReason.RequiredBuildingMissing,
                "Naval Yard can only be built in a coastal province.");

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
