using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Game.Research;

public enum ResearchRejectionReason
{
    GameEnded,                  // 409
    UnknownTech,                // 404
    AlreadyUnlocked,            // 409
    AlreadyInProgress,          // 409
    PrerequisiteMissing,        // 409
    InsufficientResources,      // 409
}

public sealed record ResearchResult(
    ResearchProgress? Mutation,
    bool DebitMoney,
    bool DebitElectronics,
    long MoneyDelta,
    long ElectronicsDelta,
    ResearchRejectionReason? Rejection,
    string? RejectionMessage)
{
    public static ResearchResult Accept(ResearchProgress row, long money, long electronics) =>
        new(row, money > 0, electronics > 0, money, electronics, null, null);

    public static ResearchResult Reject(ResearchRejectionReason reason, string message) =>
        new(null, false, false, 0, 0, reason, message);

    public bool IsAccepted => Rejection is null;
}

/// <summary>
/// Pure starts/cancels of per-player research. Mirrors <see cref="Diplomacy.DiplomacyService"/>
/// in shape — controller loads the player + existing rows, calls the service, and applies
/// the returned <see cref="ResearchProgress"/> mutation + resource debit. Forward progress
/// is applied by the per-tick <see cref="Tick.Steps.ResearchStep"/>.
/// </summary>
public sealed class ResearchService
{
    /// <summary>
    /// Validate and start research on <paramref name="techId"/>. The returned ResearchProgress
    /// is either a brand-new row (state Pending, ProgressPoints=0) or null with a Rejection.
    /// Caller must add the row to the EF context and persist alongside the resource debit.
    /// </summary>
    public ResearchResult StartResearch(
        Player player,
        string techId,
        bool gameEnded,
        IReadOnlyCollection<ResearchProgress> existingRows)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(techId);
        ArgumentNullException.ThrowIfNull(existingRows);

        if (gameEnded)
            return ResearchResult.Reject(ResearchRejectionReason.GameEnded, "World has ended.");

        var spec = TechCatalog.Find(techId);
        if (spec is null)
            return ResearchResult.Reject(ResearchRejectionReason.UnknownTech, $"Unknown tech '{techId}'.");

        var existing = existingRows.FirstOrDefault(r =>
            string.Equals(r.TechId, techId, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing.IsUnlocked
                ? ResearchResult.Reject(ResearchRejectionReason.AlreadyUnlocked, $"Tech '{techId}' is already unlocked.")
                : ResearchResult.Reject(ResearchRejectionReason.AlreadyInProgress, $"Tech '{techId}' is already in progress.");
        }

        // Prereqs must all be unlocked.
        foreach (var pre in spec.Prerequisites)
        {
            var preRow = existingRows.FirstOrDefault(r => string.Equals(r.TechId, pre, StringComparison.Ordinal));
            if (preRow is null || !preRow.IsUnlocked)
            {
                return ResearchResult.Reject(
                    ResearchRejectionReason.PrerequisiteMissing,
                    $"Prerequisite '{pre}' is not unlocked.");
            }
        }

        if (player.Money < spec.MoneyCost || player.Electronics < spec.ElectronicsCost)
        {
            return ResearchResult.Reject(
                ResearchRejectionReason.InsufficientResources,
                $"Need {spec.MoneyCost} money and {spec.ElectronicsCost} electronics.");
        }

        var row = new ResearchProgress
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Player = player,
            TechId = techId,
            ProgressPoints = 0,
            IsUnlocked = false,
        };
        return ResearchResult.Accept(row, spec.MoneyCost, spec.ElectronicsCost);
    }
}
