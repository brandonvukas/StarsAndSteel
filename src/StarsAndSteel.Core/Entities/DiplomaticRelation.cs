using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Pairwise relationship between two players. <see cref="TrustScore"/> drifts based on actions
/// (broken treaties, surprise attacks, gifted aid) and is consumed by the AI memory model.
/// </summary>
public class DiplomaticRelation
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid FromPlayerId { get; set; }
    public Player FromPlayer { get; set; } = default!;

    public Guid ToPlayerId { get; set; }
    public Player ToPlayer { get; set; } = default!;

    public DiplomaticStatus Status { get; set; } = DiplomaticStatus.Peace;

    /// <summary>-100..100. Used by AI to gate alliance proposals and surprise attacks.</summary>
    public int TrustScore { get; set; }

    public int LastChangedAtTick { get; set; }
}
