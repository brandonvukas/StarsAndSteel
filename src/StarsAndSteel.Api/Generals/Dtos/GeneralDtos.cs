namespace StarsAndSteel.Api.Generals.Dtos;

/// <summary>
/// Phase 3f: recruit a theater commander (general) for the caller in this world.
/// Costs a fixed amount of money (see <c>GeneralsService.RecruitMoneyCost</c>).
/// MVP allows only one general per player.
/// </summary>
public sealed record RecruitGeneralRequest(string Name);

/// <summary>
/// Phase 3f: assign (or reassign) an existing general to one of the caller's
/// friendly provinces. While assigned, defenders at that province get a flat
/// effective-strength bonus during ground combat resolution.
/// </summary>
public sealed record AssignGeneralRequest(Guid ProvinceId);

/// <summary>Snapshot row for GET /generals.</summary>
public sealed record GeneralDto(
    Guid Id,
    Guid OwnerPlayerId,
    string Name,
    Guid? AssignedProvinceId,
    int XpLevel);

/// <summary>Returned from successful POST /generals.</summary>
public sealed record GeneralRecruited(
    Guid GeneralId,
    string Name,
    long MoneyDelta);

/// <summary>Returned from successful POST /generals/{id}/assign.</summary>
public sealed record GeneralAssigned(
    Guid GeneralId,
    Guid ProvinceId);
