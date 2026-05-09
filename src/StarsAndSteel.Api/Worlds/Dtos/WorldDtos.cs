namespace StarsAndSteel.Api.Worlds.Dtos;

/// <summary>
/// Request body for <c>POST /api/worlds</c>. <see cref="MapSeed"/> is optional;
/// when null the server picks a random seed and returns it on the response so
/// the client can reproduce the world if desired. <see cref="AiOpponentCount"/>
/// is optional (default 0); MVP supports 0 or 1, where 1 seats a Hawk AI per
/// <c>docs/09-AI-OPPONENTS.md</c>.
/// </summary>
public sealed record CreateWorldRequest(string Name, int? MapSeed, int? AiOpponentCount = null);

/// <summary>
/// Request body for <c>POST /api/worlds/{id}/join</c>. The flag colors are
/// validated as <c>#rrggbb</c> hex strings (7 chars including <c>#</c>) per
/// the <see cref="StarsAndSteel.Core.Entities.Player"/> configuration.
/// </summary>
public sealed record JoinWorldRequest(
    string NationName,
    string FlagPrimaryHex,
    string FlagSecondaryHex);

/// <summary>
/// Summary row returned by <c>GET /api/worlds</c> and the create response.
/// Deliberately small — full-world snapshot has its own endpoint with fog-of-war
/// filtering (Phase 1G+).
/// </summary>
public sealed record WorldSummary(
    Guid Id,
    string Name,
    string Status,
    int CurrentTick,
    int TickIntervalSeconds,
    int MapSeed,
    int PlayerCount,
    int ProvinceCount,
    DateTime CreatedAt,
    DateTime? StartedAt);

/// <summary>
/// Returned by <c>POST /api/worlds/{id}/join</c>. Includes the assigned capital
/// so the client can immediately center the map on it.
/// </summary>
public sealed record JoinWorldResponse(
    Guid PlayerId,
    Guid GameWorldId,
    string NationName,
    Guid CapitalProvinceId,
    string CapitalProvinceName,
    long Money,
    long Oil,
    long Steel,
    long Electronics,
    long Food,
    long Manpower);
