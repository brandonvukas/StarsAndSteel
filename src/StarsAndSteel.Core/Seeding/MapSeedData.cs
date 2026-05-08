using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Seeding;

/// <summary>
/// Pure-data view of the world map produced by the loader in <c>StarsAndSteel.Data</c>
/// (which reads <c>shared/map-data.json</c>). Lives in Core so the <c>StarsAndSteel.Game</c>
/// project can consume it without taking a dependency on Data.
/// <para/>
/// The same JSON file is consumed by the client at build time (via the Vite <c>@shared</c>
/// alias) so the server and client cannot drift on what provinces exist.
/// </summary>
public sealed record MapSeedData(
    IReadOnlyList<ProvinceRow> Provinces,
    IReadOnlyList<AdjacencyRow> Adjacencies);

/// <summary>
/// Flat province row produced by the seeder. Mirrors the <c>Province</c> table columns
/// minus FKs (the consuming <c>WorldFactory</c> assigns <c>GameWorldId</c> per world).
/// </summary>
public sealed record ProvinceRow(
    Guid Id,
    string Name,
    ProvinceType Type,
    bool IsCoastal,
    float CenterX,
    float CenterY,
    int BasePopulation,
    int MoneyPerTick,
    int OilPerTick,
    int SteelPerTick,
    int ElectronicsPerTick,
    int FoodPerTick,
    int ManpowerPerTick);

/// <summary>
/// Flat adjacency row produced by the seeder. Caller guarantees
/// <c>ProvinceAId &lt; ProvinceBId</c> (Guid comparison) — see
/// <c>docs/03-DATABASE-SCHEMA.md</c>.
/// </summary>
public sealed record AdjacencyRow(
    Guid ProvinceAId,
    Guid ProvinceBId,
    float TerrainCost,
    bool IsSeaCrossing);
