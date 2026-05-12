using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Orders.Dtos;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
using StarsAndSteel.Game.Orders;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// Order submission endpoints (move, attack, airstrike, build-unit, build-building).
/// All five share the cutoff contract from <c>docs/06-BACKEND-API.md</c>:
/// <list type="number">
///   <item>Validate ownership and feasibility (delegated to <see cref="OrderService"/>).</item>
///   <item>Acquire the per-world tick lock briefly to read <c>world.CurrentTick</c>.</item>
///   <item>Stamp the order with <c>IssuedAtTick = world.CurrentTick + 1</c> and persist.</item>
/// </list>
/// This guarantees orders submitted while a tick is processing land in the next tick,
/// never the one currently running. See <c>docs/07-GAME-LOOP.md</c>
/// §"Concurrency &amp; determinism" for the full contract.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/orders")]
[Authorize]
public sealed class OrdersController : ControllerBase
{
    private readonly StarsAndSteelDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly OrderService _orderService;
    private readonly WorldLockRegistry _locks;
    private readonly IValidator<MoveOrderRequest> _moveValidator;
    private readonly IValidator<AttackOrderRequest> _attackValidator;
    private readonly IValidator<AirStrikeOrderRequest> _airStrikeValidator;
    private readonly IValidator<MissileLaunchOrderRequest> _missileLaunchValidator;
    private readonly IValidator<BuildUnitOrderRequest> _buildUnitValidator;
    private readonly IValidator<BuildBuildingOrderRequest> _buildBuildingValidator;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        StarsAndSteelDbContext db,
        UserManager<User> userManager,
        OrderService orderService,
        WorldLockRegistry locks,
        IValidator<MoveOrderRequest> moveValidator,
        IValidator<AttackOrderRequest> attackValidator,
        IValidator<AirStrikeOrderRequest> airStrikeValidator,
        IValidator<MissileLaunchOrderRequest> missileLaunchValidator,
        IValidator<BuildUnitOrderRequest> buildUnitValidator,
        IValidator<BuildBuildingOrderRequest> buildBuildingValidator,
        ILogger<OrdersController> logger)
    {
        _db = db;
        _userManager = userManager;
        _orderService = orderService;
        _locks = locks;
        _moveValidator = moveValidator;
        _attackValidator = attackValidator;
        _airStrikeValidator = airStrikeValidator;
        _missileLaunchValidator = missileLaunchValidator;
        _buildUnitValidator = buildUnitValidator;
        _buildBuildingValidator = buildBuildingValidator;
        _logger = logger;
    }

    [HttpPost("move")]
    public async Task<ActionResult<UnitOrderAccepted>> Move(
        Guid worldId,
        [FromBody] MoveOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (badRequest, fluentErrors) = await ValidateAsync(_moveValidator, request, cancellationToken);
        if (badRequest is not null) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, cancellationToken);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var unit = await _db.Units.FirstOrDefaultAsync(
                u => u.Id == request.UnitId && u.GameWorldId == worldId, cancellationToken);
            if (unit is null) return NotFound(new { error = "Unit not found in this world." });

            var target = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == request.TargetProvinceId && p.GameWorldId == worldId, cancellationToken);
            if (target is null) return NotFound(new { error = "Target province not found in this world." });

            // Adjacency from the unit's CURRENT location (not its owner's territory).
            var adjacencyAnchor = unit.LocationProvinceId;
            if (adjacencyAnchor is null)
            {
                return BadRequest(new { error = "Unit has no current location (in transit)." });
            }
            var adjacent = await GetAdjacentProvinceIdsAsync(adjacencyAnchor.Value, cancellationToken);

            var result = _orderService.ValidateMove(
                unit, ctx.Player!, target, adjacent, ctx.World!.CurrentTick, ctx.World.Status);

            return await PersistUnitOrderAsync(result, ctx.World, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("attack")]
    public async Task<ActionResult<UnitOrderAccepted>> Attack(
        Guid worldId,
        [FromBody] AttackOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (badRequest, _) = await ValidateAsync(_attackValidator, request, cancellationToken);
        if (badRequest is not null) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, cancellationToken);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var unit = await _db.Units.FirstOrDefaultAsync(
                u => u.Id == request.UnitId && u.GameWorldId == worldId, cancellationToken);
            if (unit is null) return NotFound(new { error = "Unit not found in this world." });

            var target = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == request.TargetProvinceId && p.GameWorldId == worldId, cancellationToken);
            if (target is null) return NotFound(new { error = "Target province not found in this world." });

            var adjacencyAnchor = unit.LocationProvinceId;
            if (adjacencyAnchor is null)
            {
                return BadRequest(new { error = "Unit has no current location (in transit)." });
            }
            var adjacent = await GetAdjacentProvinceIdsAsync(adjacencyAnchor.Value, cancellationToken);

            var result = _orderService.ValidateAttack(
                unit, ctx.Player!, target, adjacent, ctx.World!.CurrentTick, ctx.World.Status);

            return await PersistUnitOrderAsync(result, ctx.World, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("airstrike")]
    public async Task<ActionResult<UnitOrderAccepted>> AirStrike(
        Guid worldId,
        [FromBody] AirStrikeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (badRequest, _) = await ValidateAsync(_airStrikeValidator, request, cancellationToken);
        if (badRequest is not null) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, cancellationToken);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var unit = await _db.Units.FirstOrDefaultAsync(
                u => u.Id == request.UnitId && u.GameWorldId == worldId, cancellationToken);
            if (unit is null) return NotFound(new { error = "Unit not found in this world." });

            var target = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == request.TargetProvinceId && p.GameWorldId == worldId, cancellationToken);
            if (target is null) return NotFound(new { error = "Target province not found in this world." });

            // Air-strike eligibility looks at buildings + units at the unit's stationing
            // province. Phase 2b: a CarrierAirWing's "airbase" is its parent carrier, so
            // we also need the units at that province for the validator.
            var hostingProvinceId = unit.LocationProvinceId ?? unit.HomeBaseProvinceId;
            var hostingBuildings = hostingProvinceId is null
                ? Array.Empty<Building>()
                : await _db.Buildings
                    .Where(b => b.ProvinceId == hostingProvinceId.Value)
                    .ToArrayAsync(cancellationToken);
            var hostingUnits = hostingProvinceId is null
                ? Array.Empty<Unit>()
                : await _db.Units
                    .Where(u => u.LocationProvinceId == hostingProvinceId.Value && u.Strength > 0)
                    .ToArrayAsync(cancellationToken);

            var result = _orderService.ValidateAirStrike(
                unit, ctx.Player!, target, hostingBuildings, hostingUnits, ctx.World!.CurrentTick, ctx.World.Status);

            return await PersistUnitOrderAsync(result, ctx.World, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("launch-missile")]
    public async Task<ActionResult<UnitOrderAccepted>> LaunchMissile(
        Guid worldId,
        [FromBody] MissileLaunchOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (badRequest, _) = await ValidateAsync(_missileLaunchValidator, request, cancellationToken);
        if (badRequest is not null) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, cancellationToken);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var unit = await _db.Units.FirstOrDefaultAsync(
                u => u.Id == request.UnitId && u.GameWorldId == worldId, cancellationToken);
            if (unit is null) return NotFound(new { error = "Unit not found in this world." });

            if (unit.LocationProvinceId is null)
                return BadRequest(new { error = "Missile has no current location." });

            var launch = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == unit.LocationProvinceId.Value && p.GameWorldId == worldId, cancellationToken);
            if (launch is null) return NotFound(new { error = "Launch province not found in this world." });

            var target = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == request.TargetProvinceId && p.GameWorldId == worldId, cancellationToken);
            if (target is null) return NotFound(new { error = "Target province not found in this world." });

            var launchBuildings = await _db.Buildings
                .Where(b => b.ProvinceId == launch.Id)
                .ToArrayAsync(cancellationToken);

            var result = _orderService.ValidateMissileLaunch(
                unit, ctx.Player!, launch, target, launchBuildings,
                ctx.World!.NukesEnabled, ctx.World.CurrentTick, ctx.World.Status);

            return await PersistUnitOrderAsync(result, ctx.World, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("build-unit")]
    public async Task<ActionResult<ConstructionOrderAccepted>> BuildUnit(
        Guid worldId,
        [FromBody] BuildUnitOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (badRequest, _) = await ValidateAsync(_buildUnitValidator, request, cancellationToken);
        if (badRequest is not null) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        // The validator already proved this parses.
        var unitType = Enum.Parse<UnitType>(request.UnitType, ignoreCase: false);

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, cancellationToken);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var province = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == request.ProvinceId && p.GameWorldId == worldId, cancellationToken);
            if (province is null) return NotFound(new { error = "Province not found in this world." });

            var buildings = await _db.Buildings
                .Where(b => b.ProvinceId == province.Id)
                .ToArrayAsync(cancellationToken);

            // Phase 2b: carrier-wing builds need to see what's already in the province
            // (carriers + parented wings) and what wing builds are still in flight.
            var provinceUnits = await _db.Units
                .Where(u => u.LocationProvinceId == province.Id && u.Strength > 0)
                .ToArrayAsync(cancellationToken);
            var pendingWingOrders = await _db.ConstructionOrders
                .Where(o => o.GameWorldId == worldId
                         && o.ProvinceId == province.Id
                         && o.OrderType == OrderType.BuildUnit
                         && o.UnitType == UnitType.CarrierAirWing
                         && o.Status != OrderStatus.Complete
                         && o.Status != OrderStatus.Cancelled)
                .ToArrayAsync(cancellationToken);

            var result = _orderService.ValidateBuildUnit(
                ctx.Player!, province, unitType, request.Quantity,
                buildings, provinceUnits, pendingWingOrders,
                ctx.World!.CurrentTick, ctx.World.Status);

            return await PersistConstructionOrderAsync(result, ctx.Player!, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("build-building")]
    public async Task<ActionResult<ConstructionOrderAccepted>> BuildBuilding(
        Guid worldId,
        [FromBody] BuildBuildingOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (badRequest, _) = await ValidateAsync(_buildBuildingValidator, request, cancellationToken);
        if (badRequest is not null) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var buildingType = Enum.Parse<BuildingType>(request.BuildingType, ignoreCase: false);

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, cancellationToken);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var province = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == request.ProvinceId && p.GameWorldId == worldId, cancellationToken);
            if (province is null) return NotFound(new { error = "Province not found in this world." });

            var result = _orderService.ValidateBuildBuilding(
                ctx.Player!, province, buildingType, ctx.World!.CurrentTick, ctx.World.Status);

            return await PersistConstructionOrderAsync(result, ctx.Player!, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- Helpers ----------------------------------------------------------

    /// <summary>
    /// Loads the world + the calling user's player row. Returns a context object
    /// indicating whether to 404 (world missing) or 403 (user not in world).
    /// </summary>
    private async Task<CallerContext> LoadCallerContextAsync(Guid worldId, Guid userId, CancellationToken ct)
    {
        var world = await _db.GameWorlds.FirstOrDefaultAsync(w => w.Id == worldId, ct);
        if (world is null) return CallerContext.NotFoundResult;

        var player = await _db.Players.FirstOrDefaultAsync(
            p => p.GameWorldId == worldId && p.UserId == userId, ct);
        if (player is null) return CallerContext.ForbiddenResult;

        return new CallerContext(world, player, false, false);
    }

    private async Task<HashSet<Guid>> GetAdjacentProvinceIdsAsync(Guid anchorId, CancellationToken ct)
    {
        // ProvinceAdjacency stores ordered pairs (A < B). A province can appear on either side.
        var rows = await _db.ProvinceAdjacencies
            .AsNoTracking()
            .Where(a => a.ProvinceAId == anchorId || a.ProvinceBId == anchorId)
            .Select(a => new { a.ProvinceAId, a.ProvinceBId })
            .ToListAsync(ct);

        var result = new HashSet<Guid>(rows.Count);
        foreach (var r in rows)
        {
            result.Add(r.ProvinceAId == anchorId ? r.ProvinceBId : r.ProvinceAId);
        }
        return result;
    }

    private async Task<ActionResult<UnitOrderAccepted>> PersistUnitOrderAsync(
        OrderValidationResult result, GameWorld world, CancellationToken ct)
    {
        if (!result.IsAccepted)
        {
            return RejectionToActionResult<UnitOrderAccepted>(result);
        }

        var order = result.UnitOrder!;
        _db.UnitOrders.Add(order);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Unit order accepted: {OrderId} type={OrderType} unit={UnitId} world={WorldId} issuedAt={IssuedAtTick}",
            order.Id, order.OrderType, order.UnitId, world.Id, order.IssuedAtTick);

        return Ok(new UnitOrderAccepted(
            order.Id, order.UnitId, order.OrderType.ToString(),
            order.TargetProvinceId, order.IssuedAtTick));
    }

    private async Task<ActionResult<ConstructionOrderAccepted>> PersistConstructionOrderAsync(
        OrderValidationResult result, Player caller, CancellationToken ct)
    {
        if (!result.IsAccepted)
        {
            return RejectionToActionResult<ConstructionOrderAccepted>(result);
        }

        var order = result.ConstructionOrder!;
        OrderService.DebitForBuild(caller, order); // mutates tracked Player row
        _db.ConstructionOrders.Add(order);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Construction order accepted: {OrderId} type={OrderType} province={ProvinceId} player={PlayerId} ticksRemaining={TicksRemaining}",
            order.Id, order.OrderType, order.ProvinceId, order.OwnerPlayerId, order.TicksRemaining);

        return Ok(new ConstructionOrderAccepted(
            order.Id,
            order.ProvinceId,
            order.OrderType.ToString(),
            order.UnitType?.ToString(),
            order.OrderType == OrderType.BuildUnit ? order.Quantity : null,
            order.BuildingType?.ToString(),
            order.IssuedAtTick,
            order.TicksRemaining));
    }

    private ActionResult<T> RejectionToActionResult<T>(OrderValidationResult result)
    {
        var msg = result.RejectionMessage ?? "Order rejected.";
        return result.Rejection switch
        {
            OrderRejectionReason.UnitNotOwnedByCaller     => Forbid(),
            OrderRejectionReason.ProvinceNotOwnedByCaller => Forbid(),
            OrderRejectionReason.InsufficientResources    => Conflict(new { error = msg }),
            OrderRejectionReason.GameEnded                => Conflict(new { error = msg }),
            OrderRejectionReason.UnknownUnit              => NotFound(new { error = msg }),
            OrderRejectionReason.UnknownProvince          => NotFound(new { error = msg }),
            OrderRejectionReason.NukesDisabledForWorld    => Conflict(new { error = msg }),
            _ => BadRequest(new { error = msg }),
        };
    }

    private async Task<(ActionResult? BadRequest, IList<FluentValidation.Results.ValidationFailure>? Errors)>
        ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken ct)
    {
        var v = await validator.ValidateAsync(request, ct);
        if (v.IsValid) return (null, null);

        return (ValidationProblem(BuildModelState(v.Errors)), v.Errors);
    }

    private static ModelStateDictionary BuildModelState(IEnumerable<FluentValidation.Results.ValidationFailure> failures)
    {
        var modelState = new ModelStateDictionary();
        foreach (var failure in failures)
        {
            modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }
        return modelState;
    }

    private sealed record CallerContext(GameWorld? World, Player? Player, bool NotFound, bool Forbidden)
    {
        public static readonly CallerContext NotFoundResult = new(null, null, true, false);
        public static readonly CallerContext ForbiddenResult = new(null, null, false, true);
    }
}
