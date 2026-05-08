namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Undirected edge between two provinces. Stored once per pair, not twice.
/// <para/>
/// Invariant enforced by configuration and seeders: <c>ProvinceAId &lt; ProvinceBId</c>
/// (Guid comparison). All adjacency lookups must go through a helper with
/// <c>WHERE ProvinceAId = @id OR ProvinceBId = @id</c> — never assume a direction.
/// </summary>
public class ProvinceAdjacency
{
    /// <summary>Composite PK part 1. Always the lexicographically smaller Guid of the pair.</summary>
    public Guid ProvinceAId { get; set; }
    public Province ProvinceA { get; set; } = default!;

    /// <summary>Composite PK part 2. Always the lexicographically larger Guid of the pair.</summary>
    public Guid ProvinceBId { get; set; }
    public Province ProvinceB { get; set; } = default!;

    /// <summary>Movement cost multiplier; 1.0 = normal terrain. Mountains/rivers raise it.</summary>
    public float TerrainCost { get; set; } = 1.0f;

    /// <summary>True for open-water edges; only naval and air units can traverse.</summary>
    public bool IsSeaCrossing { get; set; }
}
