namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Per-AI-player persisted memory. One row per AI <see cref="Player"/>. Stored as JSON so we
/// can iterate on AI internals without schema migrations; fields get promoted to columns
/// only if they need to be queried. See <c>docs/09-AI-OPPONENTS.md</c>.
/// </summary>
public class AiMemory
{
    /// <summary>Primary key and FK to <see cref="Player.Id"/>; relationship is 1:1.</summary>
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = default!;

    /// <summary>Serialized memory: grudges, current target, mode, etc. <c>nvarchar(max)</c>.</summary>
    public string MemoryJson { get; set; } = "{}";
}
