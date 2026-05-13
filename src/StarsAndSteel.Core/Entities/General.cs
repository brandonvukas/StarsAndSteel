namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Phase 3f: a Theater Commander (general) is a non-combat persistent leader
/// figure a player recruits at fixed cost (no construction queue) and assigns
/// to a single province. While assigned, the general grants a defender combat
/// bonus to any battle resolved at that province where the assignment owner
/// is the defender (CombatStep multiplies defender effective strength by
/// <c>1.0 + Generals.DefenderCombatBonus</c>).
/// <para/>
/// MVP: one general per player at a time (enforced by
/// <c>GeneralsService.RecruitGeneral</c>). XP and named perks are deferred —
/// generals are a flat-bonus presence right now.
/// </summary>
public class General
{
    public Guid Id { get; set; }
    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid OwnerPlayerId { get; set; }
    public Player OwnerPlayer { get; set; } = default!;

    public string Name { get; set; } = default!;

    /// <summary>Null while unassigned; set to a friendly Province via the assign endpoint.</summary>
    public Guid? AssignedProvinceId { get; set; }
    public Province? AssignedProvince { get; set; }
    /// <summary>Reserved for Phase 3+: leadership XP gains from battles in the assigned province.</summary>
    public int XpLevel { get; set; }
}
