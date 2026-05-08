using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// A user's seat in a specific <see cref="GameWorld"/>. AI players have <see cref="UserId"/>
/// null and <see cref="IsAi"/> true. Resources are denormalized onto the row for cheap reads
/// during tick processing and snapshot serialization.
/// </summary>
public class Player
{
    public Guid Id { get; set; }

    /// <summary>Null for AI players.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public bool IsAi { get; set; }

    /// <summary>Required when <see cref="IsAi"/> is true; null for human players.</summary>
    public AiPersonality? AiPersonality { get; set; }

    public string NationName { get; set; } = default!;
    public string FlagPrimaryHex { get; set; } = default!;
    public string FlagSecondaryHex { get; set; } = default!;

    /// <summary>Flips to false when the last owned province falls.</summary>
    public bool IsAlive { get; set; } = true;

    // Denormalized resource columns. Long because endgame stockpiles can run high.
    public long Money { get; set; }
    public long Oil { get; set; }
    public long Steel { get; set; }
    public long Electronics { get; set; }
    public long Food { get; set; }
    public long Manpower { get; set; }

    public ICollection<Province> OwnedProvinces { get; set; } = new List<Province>();
    public ICollection<Unit> OwnedUnits { get; set; } = new List<Unit>();

    /// <summary>1:1 with <see cref="AiMemory"/> when <see cref="IsAi"/> is true; null otherwise.</summary>
    public AiMemory? AiMemory { get; set; }
}
