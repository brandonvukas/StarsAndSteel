using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Generals;

/// <summary>
/// Why a general action was rejected. Mirrors <see cref="Orders.OrderRejectionReason"/> and
/// <see cref="Research.ResearchRejectionReason"/> in shape — exhaustive enum so the
/// controller can pattern-match without a default branch.
/// </summary>
public enum GeneralsRejectionReason
{
    GameEnded,                     // 409
    InsufficientResources,         // 409
    AlreadyHasGeneral,             // 409 — Phase 3f MVP enforces one general per player
    UnknownGeneral,                // 404
    GeneralNotOwnedByCaller,       // 403
    UnknownProvince,               // 404
    ProvinceNotOwnedByCaller,      // 403 — assignment requires friendly province
    NameTooLongOrEmpty,            // 400 — defensive, validator already catches this
}

/// <summary>
/// Result of a pure generals operation. Exactly one of <see cref="General"/> or
/// <see cref="Rejection"/> is set. The controller persists the new/updated row +
/// any resource debit in one SaveChanges, or maps <see cref="Rejection"/> to a
/// problem response.
/// </summary>
public sealed record GeneralsResult(
    General? General,
    bool DebitMoney,
    long MoneyDelta,
    GeneralsRejectionReason? Rejection,
    string? RejectionMessage)
{
    public static GeneralsResult AcceptRecruit(General row, long moneyDelta) =>
        new(row, moneyDelta > 0, moneyDelta, null, null);

    public static GeneralsResult AcceptAssign(General row) =>
        new(row, false, 0, null, null);

    public static GeneralsResult Reject(GeneralsRejectionReason reason, string message) =>
        new(null, false, 0, reason, message);

    public bool IsAccepted => Rejection is null;
}

/// <summary>
/// Pure (no DbContext, no I/O) recruit + assign for theater commanders (Phase 3f).
/// A general is a non-combat persistent leader figure a player buys for a fixed
/// money cost and pins to one friendly province; while assigned, defenders at
/// that province get a flat <see cref="DefenderCombatBonus"/> on effective
/// strength via the <c>CombatResolver.ResolveGround</c> overload.
/// <para/>
/// MVP scope:
/// <list type="bullet">
///   <item>One general per player at a time (enforced here, not in the schema).</item>
///   <item>Instant recruit — no construction queue (different from units / buildings).</item>
///   <item>Free re-assignment between owned provinces (no cooldown).</item>
///   <item>XP / named perks deferred — generals are a flat-bonus presence right now.</item>
/// </list>
/// </summary>
public sealed class GeneralsService
{
    /// <summary>Money cost to recruit a general. Cheap by design — one-shot purchase per player.</summary>
    public const long RecruitMoneyCost = 2_500;

    /// <summary>Defender effective-strength bonus while a general is assigned to the province (15%).</summary>
    public const double DefenderCombatBonus = 0.15;

    /// <summary>
    /// Recruit a brand-new general for <paramref name="caller"/>. The general is created
    /// unassigned (<see cref="General.AssignedProvinceId"/> = null) — the caller picks a
    /// province via <see cref="AssignGeneral"/> in a separate request.
    /// </summary>
    /// <param name="caller">The recruiting player. Money is debited on accept.</param>
    /// <param name="callerExistingGenerals">All existing generals owned by <paramref name="caller"/>; used to enforce the one-per-player cap.</param>
    /// <param name="name">Display name (1-80 chars). Validator should pre-check.</param>
    /// <param name="worldStatus">World status — Ended worlds reject all writes.</param>
    public GeneralsResult RecruitGeneral(
        Player caller,
        IReadOnlyCollection<General> callerExistingGenerals,
        string name,
        GameWorldStatus worldStatus)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(callerExistingGenerals);
        ArgumentNullException.ThrowIfNull(name);

        if (worldStatus == GameWorldStatus.Ended)
            return GeneralsResult.Reject(GeneralsRejectionReason.GameEnded, "World has ended.");

        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 80)
            return GeneralsResult.Reject(GeneralsRejectionReason.NameTooLongOrEmpty,
                "General name must be 1-80 characters.");

        if (callerExistingGenerals.Count > 0)
            return GeneralsResult.Reject(GeneralsRejectionReason.AlreadyHasGeneral,
                "You already have a general; only one is allowed in this MVP.");

        if (caller.Money < RecruitMoneyCost)
            return GeneralsResult.Reject(GeneralsRejectionReason.InsufficientResources,
                $"Recruiting a general requires {RecruitMoneyCost} money.");

        return GeneralsResult.AcceptRecruit(
            new General
            {
                Id = Guid.NewGuid(),
                GameWorldId = caller.GameWorldId,
                OwnerPlayerId = caller.Id,
                Name = trimmed,
                AssignedProvinceId = null,
                XpLevel = 0,
            },
            RecruitMoneyCost);
    }

    /// <summary>
    /// Assign (or reassign) <paramref name="general"/> to a friendly province.
    /// <paramref name="targetProvince"/> must be owned by <paramref name="caller"/> —
    /// you can't park a general on neutral or enemy soil.
    /// </summary>
    public GeneralsResult AssignGeneral(
        Player caller,
        General general,
        Province targetProvince,
        GameWorldStatus worldStatus)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(general);
        ArgumentNullException.ThrowIfNull(targetProvince);

        if (worldStatus == GameWorldStatus.Ended)
            return GeneralsResult.Reject(GeneralsRejectionReason.GameEnded, "World has ended.");

        if (general.OwnerPlayerId != caller.Id)
            return GeneralsResult.Reject(GeneralsRejectionReason.GeneralNotOwnedByCaller,
                "You do not own this general.");

        if (targetProvince.OwnerPlayerId != caller.Id)
            return GeneralsResult.Reject(GeneralsRejectionReason.ProvinceNotOwnedByCaller,
                "Generals can only be assigned to provinces you own.");

        general.AssignedProvinceId = targetProvince.Id;
        general.AssignedProvince = targetProvince;
        return GeneralsResult.AcceptAssign(general);
    }

    /// <summary>
    /// Apply the recruit money debit. Mirrors <c>OrderService.DebitForBuild</c> in
    /// shape — controller calls this on a tracked Player row right before SaveChanges.
    /// </summary>
    public static void DebitForRecruit(Player caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        caller.Money -= RecruitMoneyCost;
    }
}
