using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.Wonders.Dtos;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Wonders;
using StarsAndSteel.Data;
using StarsAndSteel.Game.Orders;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// Phase 4b1: Wonders catalogue + per-world status. Read-only — wonders are
/// built via the regular <c>POST /api/worlds/{id}/orders/build-building</c>
/// endpoint with one of the wonder <see cref="BuildingType"/> values; this
/// controller just exposes "what wonders exist, what each does, and who's
/// claimed them so far". The client uses it to render the Wonders panel.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/wonders")]
[Authorize]
public sealed class WondersController : ControllerBase
{
    private readonly StarsAndSteelDbContext _db;
    private readonly UserManager<User> _userManager;

    public WondersController(StarsAndSteelDbContext db, UserManager<User> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    /// <summary>
    /// Returns one row per wonder in <see cref="WonderCatalog"/> with its
    /// per-world status (Available / InProgress / Built) and, when claimed,
    /// who claimed it + where. Caller must be a player in the world (else 403).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WonderRow>>> GetCatalog(Guid worldId, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var caller = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameWorldId == worldId && p.UserId == user.Id, ct);
        if (caller is null) return Forbid();

        var wonderTypes = WonderCatalog.All.Select(w => w.Type).ToHashSet();

        // Built wonders (one row per existing building of a wonder type) joined to
        // its province + owner. EF can do this with a single LEFT JOIN.
        var builtRows = await (
            from b in _db.Buildings.AsNoTracking()
            join p in _db.Provinces.AsNoTracking() on b.ProvinceId equals p.Id
            where p.GameWorldId == worldId && wonderTypes.Contains(b.Type)
            join player in _db.Players.AsNoTracking() on p.OwnerPlayerId equals player.Id into pj
            from player in pj.DefaultIfEmpty()
            select new
            {
                b.Type,
                ProvinceId = (Guid?)p.Id,
                ProvinceName = (string?)p.Name,
                OwnerPlayerId = (Guid?)(player == null ? null : player.Id),
                OwnerNationName = player == null ? null : player.NationName,
            })
            .ToListAsync(ct);

        // In-progress wonder builds (no Built row yet — uniqueness check below).
        var inProgressRows = await (
            from o in _db.ConstructionOrders.AsNoTracking()
            where o.GameWorldId == worldId
                  && o.OrderType == OrderType.BuildBuilding
                  && o.BuildingType != null
                  && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.InProgress)
                  && wonderTypes.Contains(o.BuildingType.Value)
            join p in _db.Provinces.AsNoTracking() on o.ProvinceId equals p.Id
            join player in _db.Players.AsNoTracking() on o.OwnerPlayerId equals player.Id
            select new
            {
                Type = o.BuildingType!.Value,
                ProvinceId = (Guid?)p.Id,
                ProvinceName = (string?)p.Name,
                OwnerPlayerId = (Guid?)player.Id,
                OwnerNationName = (string?)player.NationName,
                o.TicksRemaining,
            })
            .ToListAsync(ct);

        var builtByType = builtRows.ToDictionary(r => r.Type);
        var inProgressByType = inProgressRows
            .GroupBy(r => r.Type)
            .ToDictionary(g => g.Key, g => g.First()); // first by EF ordering

        var result = new List<WonderRow>(WonderCatalog.All.Count);
        foreach (var info in WonderCatalog.All)
        {
            var spec = BuildCatalog.GetBuilding(info.Type);
            var cost = new WonderCost(spec.Money, spec.Oil, spec.Steel, spec.Electronics, spec.Food, spec.Manpower);

            if (builtByType.TryGetValue(info.Type, out var b))
            {
                result.Add(new WonderRow(
                    info.Type.ToString(), info.Name, info.Summary, cost, spec.TicksToBuild,
                    WonderStatus.Built, b.OwnerPlayerId, b.OwnerNationName, b.ProvinceId, b.ProvinceName, null));
                continue;
            }
            if (inProgressByType.TryGetValue(info.Type, out var ip))
            {
                result.Add(new WonderRow(
                    info.Type.ToString(), info.Name, info.Summary, cost, spec.TicksToBuild,
                    WonderStatus.InProgress, ip.OwnerPlayerId, ip.OwnerNationName, ip.ProvinceId, ip.ProvinceName, ip.TicksRemaining));
                continue;
            }
            result.Add(new WonderRow(
                info.Type.ToString(), info.Name, info.Summary, cost, spec.TicksToBuild,
                WonderStatus.Available, null, null, null, null, null));
        }

        return Ok(result);
    }
}
