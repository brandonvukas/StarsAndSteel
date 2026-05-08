namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Per-player progress toward a tech in the static catalogue. <see cref="TechId"/> is a string
/// keyed to a code-side catalogue (not a foreign key) so we can rebalance the tree without
/// schema changes.
/// </summary>
public class ResearchProgress
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = default!;

    /// <summary>Catalogue key, e.g. <c>"advanced_armor"</c>.</summary>
    public string TechId { get; set; } = default!;

    public int ProgressPoints { get; set; }
    public bool IsUnlocked { get; set; }
}
