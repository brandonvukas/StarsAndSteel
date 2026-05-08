using System.Text.Json;
using System.Text.Json.Serialization;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Data.Seeding;

/// <summary>
/// Loads <c>shared/map-data.json</c> — the single source of truth for the world map — and exposes
/// it as flat row records ready for <c>migrationBuilder.InsertData</c>.
/// <para/>
/// The same JSON file is consumed by the client at build time (via the Vite <c>@shared</c> alias)
/// and copied to the server's bin directory by <c>StarsAndSteel.Data.csproj</c>. See
/// <c>docs/02-PROJECT-STRUCTURE.md</c>.
/// <para/>
/// Stable string IDs from the JSON (e.g. <c>"test-usa"</c>) are deterministically hashed to Guids
/// so re-running migrations on a fresh DB produces identical PKs. This matters for any test that
/// hard-codes a province ID and for manually inspecting the data in SSMS.
/// </summary>
public static class MapSeeder
{
    /// <summary>
    /// Reads <c>shared/map-data.json</c> from the directory of the executing assembly. The JSON
    /// is copied to <c>bin/.../shared/map-data.json</c> by the .csproj &lt;Content Include&gt;
    /// directive in StarsAndSteel.Data.
    /// </summary>
    public static MapSeedData Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "shared", "map-data.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"map-data.json not found at '{path}'. The Data project's <Content Include> " +
                "directive should copy it from the repo's shared/ folder during build.",
                path);
        }

        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<MapDataFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("map-data.json deserialized to null.");

        var provinceIdMap = doc.Provinces.ToDictionary(p => p.Id, p => DeterministicGuid(p.Id));

        var provinces = doc.Provinces.Select(p => new ProvinceRow(
            Id: provinceIdMap[p.Id],
            Name: p.Name,
            Type: Enum.Parse<ProvinceType>(p.Type),
            IsCoastal: p.IsCoastal,
            CenterX: p.CenterX,
            CenterY: p.CenterY,
            BasePopulation: p.BasePopulation,
            MoneyPerTick: p.BaseResourceOutput.MoneyPerTick,
            OilPerTick: p.BaseResourceOutput.OilPerTick,
            SteelPerTick: p.BaseResourceOutput.SteelPerTick,
            ElectronicsPerTick: p.BaseResourceOutput.ElectronicsPerTick,
            FoodPerTick: p.BaseResourceOutput.FoodPerTick,
            ManpowerPerTick: p.BaseResourceOutput.ManpowerPerTick
        )).ToList();

        var adjacencies = doc.Adjacencies.Select(a =>
        {
            var aGuid = provinceIdMap[a.ProvinceAId];
            var bGuid = provinceIdMap[a.ProvinceBId];

            // Enforce the ProvinceAId < ProvinceBId invariant from docs/03-DATABASE-SCHEMA.md.
            // Adjacency lookups assume the smaller Guid is in column A.
            if (aGuid.CompareTo(bGuid) > 0)
            {
                (aGuid, bGuid) = (bGuid, aGuid);
            }

            return new AdjacencyRow(aGuid, bGuid, a.TerrainCost, a.IsSeaCrossing);
        }).ToList();

        return new MapSeedData(provinces, adjacencies);
    }

    /// <summary>
    /// Hashes a stable string ID into a deterministic Guid so re-running the seeder produces the
    /// same PKs every time. Uses MD5 (chosen for the 128-bit output, not for security).
    /// </summary>
    private static Guid DeterministicGuid(string input)
    {
        // Namespace prefix prevents cross-collision with other ID spaces if we ever seed e.g.
        // tech IDs the same way.
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"sas-province:{input}"));
        return new Guid(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // --- DTO shapes mirroring shared/map-data.json -------------------------------

    private sealed class MapDataFile
    {
        public int Version { get; set; }
        public List<ProvinceJson> Provinces { get; set; } = new();
        public List<AdjacencyJson> Adjacencies { get; set; } = new();
    }

    private sealed class ProvinceJson
    {
        public string Id { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!;
        public bool IsCoastal { get; set; }
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public int BasePopulation { get; set; }
        public ResourceOutputJson BaseResourceOutput { get; set; } = new();
        public List<float[]> Polygon { get; set; } = new();
    }

    private sealed class ResourceOutputJson
    {
        public int MoneyPerTick { get; set; }
        public int OilPerTick { get; set; }
        public int SteelPerTick { get; set; }
        public int ElectronicsPerTick { get; set; }
        public int FoodPerTick { get; set; }
        public int ManpowerPerTick { get; set; }
    }

    private sealed class AdjacencyJson
    {
        public string ProvinceAId { get; set; } = default!;
        public string ProvinceBId { get; set; } = default!;
        public float TerrainCost { get; set; }
        public bool IsSeaCrossing { get; set; }
    }
}

/// <summary>Aggregate result returned by <see cref="MapSeeder.Load"/>.</summary>
public sealed record MapSeedData(IReadOnlyList<ProvinceRow> Provinces, IReadOnlyList<AdjacencyRow> Adjacencies);

/// <summary>Flat province row ready for InsertData. Mirrors the Provinces table columns minus FKs.</summary>
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

/// <summary>Flat adjacency row ready for InsertData. Caller guarantees ProvinceAId &lt; ProvinceBId.</summary>
public sealed record AdjacencyRow(
    Guid ProvinceAId,
    Guid ProvinceBId,
    float TerrainCost,
    bool IsSeaCrossing);
