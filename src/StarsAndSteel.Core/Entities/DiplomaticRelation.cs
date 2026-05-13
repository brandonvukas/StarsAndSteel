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

    /// <summary>
    /// Phase 4e: directional sanction flag — when true, <see cref="FromPlayerId"/> is
    /// economically sanctioning <see cref="ToPlayerId"/>. Sanctions are asymmetric: A→B
    /// can be sanctioning while B→A is not. Each active inbound sanction reduces the
    /// target's per-tick money production by 25% (multiplicative, capped at 75% total
    /// loss / floor 25% retained — see <c>ResourceProductionStep</c>).
    /// </summary>
    public bool IsSanctioning { get; set; }

    public int LastChangedAtTick { get; set; }
}
