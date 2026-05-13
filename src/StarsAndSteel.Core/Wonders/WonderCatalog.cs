using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Wonders;

/// <summary>
/// Phase 4b1: catalogue of one-per-game wonders + their human-facing metadata.
/// Wonders piggy-back on <see cref="BuildingType"/> + the regular build-building
/// order pipeline, so this catalogue only carries flavour text and the
/// uniqueness predicate. Build costs / tick durations live in <c>BuildCatalog</c>
/// alongside every other building. Effects live in their dedicated tick step
/// (e.g. <c>ResourceProductionStep</c> for Hoover Dam, <c>MissileImpactStep</c>
/// for SDI) so each effect stays close to the system it perturbs.
/// </summary>
public static class WonderCatalog
{
    /// <summary>Static metadata for one wonder.</summary>
    public sealed record WonderInfo(
        BuildingType Type,
        string Name,
        string Summary);

    private static readonly IReadOnlyList<WonderInfo> _all = new[]
    {
        new WonderInfo(
            BuildingType.HooverDamReborn,
            "Hoover Dam Reborn",
            "Permanent +50% production from every province you control. The legend, restored — and the grid she powers."),
        new WonderInfo(
            BuildingType.StrategicDefenseInitiative,
            "Strategic Defense Initiative",
            "50% chance to intercept each incoming missile (cruise or nuclear) targeting any of your provinces. Star Wars, but real."),
    };

    /// <summary>All wonders, in catalogue order.</summary>
    public static IReadOnlyList<WonderInfo> All => _all;

    /// <summary>True if this building type is a wonder (one-per-game, special effects).</summary>
    public static bool IsWonder(BuildingType type) =>
        type == BuildingType.HooverDamReborn
        || type == BuildingType.StrategicDefenseInitiative;

    /// <summary>Lookup metadata for a wonder; null for non-wonders.</summary>
    public static WonderInfo? TryGet(BuildingType type) =>
        _all.FirstOrDefault(w => w.Type == type);
}
