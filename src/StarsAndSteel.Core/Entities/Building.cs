using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Improvement built on a <see cref="Province"/>. Modifies that province's resource output and/or
/// unlocks unit-construction options (e.g. AirBase enables aircraft production).
/// </summary>
public class Building
{
    public Guid Id { get; set; }

    public Guid ProvinceId { get; set; }
    public Province Province { get; set; } = default!;

    public BuildingType Type { get; set; }

    /// <summary>1-5. Higher levels cost more and produce more.</summary>
    public int Level { get; set; } = 1;

    public int ConstructedAtTick { get; set; }
}
