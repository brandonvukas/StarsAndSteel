using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Wonders.Dtos;

/// <summary>
/// One row per wonder in the global catalogue. Combines the static metadata
/// from <c>WonderCatalog</c> with the per-world status (built / in-progress /
/// available) so the client renders the panel from a single fetch.
/// </summary>
public sealed record WonderRow(
    /// <summary>Underlying BuildingType enum value rendered as a string for the client.</summary>
    string Type,
    string Name,
    string Summary,
    /// <summary>BuildCatalog cost (per-resource) so the panel can show "you can/can't afford it".</summary>
    WonderCost Cost,
    int TicksToBuild,
    /// <summary>Status from this world's perspective.</summary>
    WonderStatus Status,
    /// <summary>Player who built or is building this wonder; null when Available.</summary>
    Guid? OwnerPlayerId,
    /// <summary>Owner's nation name; null when Available.</summary>
    string? OwnerNationName,
    /// <summary>Province where the wonder lives (built or under construction); null when Available.</summary>
    Guid? ProvinceId,
    /// <summary>Province name for display; null when Available.</summary>
    string? ProvinceName,
    /// <summary>Ticks remaining when InProgress; null otherwise.</summary>
    int? TicksRemaining);

public sealed record WonderCost(
    long Money, long Oil, long Steel, long Electronics, long Food, long Manpower);

public enum WonderStatus
{
    /// <summary>Nobody has built or started this wonder yet.</summary>
    Available = 0,
    /// <summary>Someone has a build order in flight.</summary>
    InProgress = 1,
    /// <summary>Someone has finished it; it's gone for the rest of the game.</summary>
    Built = 2,
}
