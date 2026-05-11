namespace StarsAndSteel.Api.Research.Dtos;

/// <summary>Tech catalogue row exposed to clients (mirror of <see cref="StarsAndSteel.Game.Research.TechSpec"/>).</summary>
public sealed record TechSpecDto(
    string Id,
    string Name,
    string Category,
    string Summary,
    long MoneyCost,
    long ElectronicsCost,
    int TicksToResearch,
    IReadOnlyList<string> Prerequisites);

/// <summary>Per-player progress on one tech.</summary>
public sealed record ResearchProgressDto(
    string TechId,
    int ProgressPoints,
    int TicksToResearch,
    bool IsUnlocked);

/// <summary>Caller's whole research view in one world.</summary>
public sealed record ResearchStateDto(
    Guid CallerPlayerId,
    IReadOnlyList<TechSpecDto> Catalog,
    IReadOnlyList<ResearchProgressDto> MyProgress);

/// <summary>POST body for /api/worlds/{id}/research/start.</summary>
public sealed record StartResearchRequest(string TechId);

public sealed record ResearchStarted(string TechId, int TicksToResearch);
