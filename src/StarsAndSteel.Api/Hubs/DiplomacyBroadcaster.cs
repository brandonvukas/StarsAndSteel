using Microsoft.AspNetCore.SignalR;
using StarsAndSteel.Api.Hubs.Dtos;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Diplomacy;

namespace StarsAndSteel.Api.Hubs;

/// <summary>
/// Broadcasts diplomacy events out-of-tick to the world's SignalR group. Unlike
/// <see cref="TickBroadcaster"/> these are emitted directly from the
/// <c>DiplomacyController</c> after the DB save commits — diplomacy is a player
/// action, not a tick output. Pending news inserted by the same action is also
/// pushed via <see cref="TickEventNames.NewsPublished"/> so the cable-news ticker
/// surfaces it without waiting for the next tick.
/// </summary>
public sealed class DiplomacyBroadcaster
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<DiplomacyBroadcaster> _logger;

    public DiplomacyBroadcaster(IHubContext<GameHub> hub, ILogger<DiplomacyBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task BroadcastAsync(
        Guid worldId,
        DiplomacyMutation mutation,
        IReadOnlyList<NewsItem> persistedNews,
        CancellationToken ct)
    {
        var group = _hub.Clients.Group(GameHub.WorldGroup(worldId));

        // Emit one RelationChanged per pair (the mutation always supplies symmetric
        // pairs — collapse to A<B canonical pair so receivers don't see duplicates).
        var seenPairs = new HashSet<(Guid, Guid)>();
        foreach (var rc in mutation.RelationChanges)
        {
            var (a, b) = OrderedPair(rc.FromPlayerId, rc.ToPlayerId);
            if (!seenPairs.Add((a, b))) continue;

            try
            {
                await group.SendAsync(
                    DiplomacyEventNames.RelationChanged,
                    new DiplomacyEventDtos.RelationChanged(a, b, rc.NewStatus, rc.AtTick),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to broadcast RelationChanged for world={WorldId} pair={A}<->{B}",
                    worldId, a, b);
            }
        }

        foreach (var oc in mutation.OfferChanges)
        {
            try
            {
                if (oc.Kind == OfferChangeKind.Create)
                {
                    await group.SendAsync(
                        DiplomacyEventNames.OfferReceived,
                        new DiplomacyEventDtos.OfferReceived(
                            oc.Offer.Id,
                            oc.Offer.SenderPlayerId,
                            oc.Offer.ReceiverPlayerId,
                            oc.Offer.Kind,
                            oc.Offer.ProposedAtTick,
                            oc.Offer.ExpiresAtTick),
                        ct);
                }
                else
                {
                    await group.SendAsync(
                        DiplomacyEventNames.OfferResolved,
                        new DiplomacyEventDtos.OfferResolved(
                            oc.Offer.Id,
                            oc.Offer.SenderPlayerId,
                            oc.Offer.ReceiverPlayerId,
                            oc.Offer.Kind,
                            oc.Offer.Status,
                            oc.Offer.ResolvedAtTick ?? 0),
                        ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to broadcast offer change ({Kind}) for offer={OfferId} world={WorldId}",
                    oc.Kind, oc.Offer.Id, worldId);
            }
        }

        foreach (var n in persistedNews)
        {
            try
            {
                await group.SendAsync(
                    TickEventNames.NewsPublished,
                    new TickEventDtos.NewsPublished(
                        n.Tick, n.Id, n.Headline, n.Body, n.Severity, n.Category, n.RelatedPlayerId),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to broadcast NewsPublished for news={NewsId} world={WorldId}",
                    n.Id, worldId);
            }
        }
    }

    private static (Guid A, Guid B) OrderedPair(Guid x, Guid y) =>
        x.CompareTo(y) <= 0 ? (x, y) : (y, x);
}
