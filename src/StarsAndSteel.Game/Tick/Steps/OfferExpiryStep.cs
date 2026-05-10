using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Tick step (Phase 2D) that expires stale diplomatic proposals. Scans
/// <see cref="TickContext.PendingTreatyOffers"/> and for any offer whose
/// <see cref="TreatyOffer.ExpiresAtTick"/> &lt;= the tick being processed, flips
/// <see cref="TreatyOffer.Status"/> to <see cref="TreatyOfferStatus.Expired"/>, sets
/// <see cref="TreatyOffer.ResolvedAtTick"/>, emits a <see cref="TreatyOfferExpiredEvent"/>, and
/// queues an Info-severity Diplomacy news headline.
/// <para/>
/// Runs immediately before <see cref="NewsStep"/> so the news ticker shows expiries on the same
/// tick they happen. The headline is built directly here (not via <see cref="NewsStep"/> templates)
/// to mirror the pattern used by <c>DiplomacyService</c> for its out-of-tick offer transitions —
/// both producers feed the same <see cref="NewsCategory.Diplomacy"/> stream.
/// </summary>
public sealed class OfferExpiryStep : ITickStep
{
    public string Name => "OfferExpiry";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tick = context.ProcessingTick;
        var playerById = context.World.Players.ToDictionary(p => p.Id);

        foreach (var offer in context.PendingTreatyOffers)
        {
            // Defensive: PendingTreatyOffers is "loaded as Pending", but a concurrent action
            // earlier in the tick (none today, but possible later) could have moved it.
            if (offer.Status != TreatyOfferStatus.Pending) continue;
            if (offer.ExpiresAtTick > tick) continue;

            offer.Status = TreatyOfferStatus.Expired;
            offer.ResolvedAtTick = tick;

            context.Events.Add(new TreatyOfferExpiredEvent(
                Tick: tick,
                OfferId: offer.Id,
                SenderPlayerId: offer.SenderPlayerId,
                ReceiverPlayerId: offer.ReceiverPlayerId,
                Kind: offer.Kind));

            var senderName = playerById.TryGetValue(offer.SenderPlayerId, out var s) ? s.NationName : "Unknown";
            var receiverName = playerById.TryGetValue(offer.ReceiverPlayerId, out var r) ? r.NationName : "Unknown";

            context.NewsItemsToInsert.Add(new NewsItem
            {
                Id = Guid.NewGuid(),
                GameWorldId = context.World.Id,
                Tick = tick,
                Headline = $"{KindLabel(offer.Kind)} proposal expired",
                Body = $"{senderName}'s {KindLabel(offer.Kind)} offer to {receiverName} expired without response.",
                Severity = NewsSeverity.Info,
                Category = NewsCategory.Diplomacy,
                RelatedPlayerId = offer.SenderPlayerId,
            });
        }
    }

    private static string KindLabel(TreatyOfferKind kind) => kind switch
    {
        TreatyOfferKind.Peace         => "Peace",
        TreatyOfferKind.NonAggression => "Non-aggression pact",
        TreatyOfferKind.Alliance      => "Alliance",
        _ => kind.ToString(),
    };
}
