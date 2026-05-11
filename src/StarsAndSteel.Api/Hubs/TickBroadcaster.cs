using Microsoft.AspNetCore.SignalR;
using StarsAndSteel.Api.Hubs.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Api.Hubs;

/// <summary>
/// Translates pure <see cref="TickEvent"/>s into wire DTOs and broadcasts them
/// to the world's SignalR group. Called by <see cref="StarsAndSteel.Api.BackgroundServices.TickRunner"/>
/// AFTER the DB save commits, so clients never observe a state the database
/// does not also hold.
/// <para/>
/// The terminal <see cref="TickEventDtos.TickAdvanced"/> is always sent last;
/// the client treats it as a barrier signaling "all diffs for this tick have
/// arrived; safe to render". A client that misses it (e.g., dropped frame) can
/// re-snapshot from REST.
/// </summary>
public sealed class TickBroadcaster
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<TickBroadcaster> _logger;

    public TickBroadcaster(IHubContext<GameHub> hub, ILogger<TickBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task BroadcastAsync(Guid worldId, TickResult result, CancellationToken cancellationToken)
    {
        var group = _hub.Clients.Group(GameHub.WorldGroup(worldId));

        foreach (var evt in result.Events)
        {
            try
            {
                await DispatchAsync(group, evt, cancellationToken);
            }
            catch (Exception ex)
            {
                // A bad client subscription must not poison the rest of the broadcast.
                _logger.LogError(ex,
                    "Failed to broadcast {EventType} for world {WorldId} tick {Tick}",
                    evt.GetType().Name, worldId, result.Tick);
            }
        }

        // Barrier event — always last, always sent (even if Events is empty so
        // the client knows a tick happened with no observable changes).
        await group.SendAsync(
            TickEventNames.TickAdvanced,
            new TickEventDtos.TickAdvanced(result.Tick, result.Events.Count),
            cancellationToken);
    }

    private static Task DispatchAsync(IClientProxy group, TickEvent evt, CancellationToken ct) => evt switch
    {
        ResourcesProducedEvent e => group.SendAsync(
            TickEventNames.ResourcesUpdated,
            new TickEventDtos.ResourcesUpdated(
                e.Tick, e.PlayerId,
                e.MoneyDelta, e.OilDelta, e.SteelDelta,
                e.ElectronicsDelta, e.FoodDelta, e.ManpowerDelta),
            ct),

        UnitMovedEvent e => group.SendAsync(
            TickEventNames.UnitMoved,
            new TickEventDtos.UnitMoved(
                e.Tick, e.UnitId, e.OwnerPlayerId, e.FromProvinceId, e.ToProvinceId),
            ct),

        UnitDestroyedEvent e => group.SendAsync(
            TickEventNames.UnitDestroyed,
            new TickEventDtos.UnitDestroyed(
                e.Tick, e.UnitId, e.OwnerPlayerId, e.LocationProvinceId, e.Cause),
            ct),

        AirStrikeResolvedEvent e => group.SendAsync(
            TickEventNames.AirStrikeResolved,
            new TickEventDtos.AirStrikeResolved(
                e.Tick, e.AttackerUnitId, e.AttackerPlayerId, e.TargetProvinceId,
                e.AttackerStrengthLoss, e.DefenderStrengthLoss),
            ct),

        CombatResolvedEvent e => group.SendAsync(
            TickEventNames.CombatResolved,
            new TickEventDtos.CombatResolved(
                e.Tick, e.ProvinceId, e.AttackerPlayerId, e.DefenderPlayerId,
                e.AttackerStrengthLoss, e.DefenderStrengthLoss, e.WinnerPlayerId),
            ct),

        ProvinceCapturedEvent e => group.SendAsync(
            TickEventNames.ProvinceCaptured,
            new TickEventDtos.ProvinceCaptured(
                e.Tick, e.ProvinceId, e.FromPlayerId, e.ToPlayerId),
            ct),

        UnitBuiltEvent e => group.SendAsync(
            TickEventNames.UnitBuilt,
            new TickEventDtos.UnitBuilt(
                e.Tick, e.UnitId, e.OwnerPlayerId, e.ProvinceId, e.Type, e.Strength),
            ct),

        BuildingCompletedEvent e => group.SendAsync(
            TickEventNames.BuildingCompleted,
            new TickEventDtos.BuildingCompleted(
                e.Tick, e.BuildingId, e.OwnerPlayerId, e.ProvinceId, e.Type, e.Level),
            ct),

        NewsPublishedEvent e => group.SendAsync(
            TickEventNames.NewsPublished,
            new TickEventDtos.NewsPublished(
                e.Tick, e.NewsItemId, e.Headline, e.Body, e.Severity, e.Category, e.RelatedPlayerId),
            ct),

        VictoryAchievedEvent e => group.SendAsync(
            TickEventNames.VictoryAchieved,
            new TickEventDtos.VictoryAchieved(
                e.Tick, e.WinnerPlayerId, e.WinnerNationName, e.OwnedProvinceCount, e.TotalProvinceCount),
            ct),

        CoalitionVictoryAchievedEvent e => group.SendAsync(
            TickEventNames.CoalitionVictoryAchieved,
            new TickEventDtos.CoalitionVictoryAchieved(
                e.Tick, e.WinnerPlayerIds, e.WinnerNationNames, e.OwnedProvinceCount, e.TotalProvinceCount),
            ct),

        PlayerEliminatedEvent e => group.SendAsync(
            TickEventNames.PlayerEliminated,
            new TickEventDtos.PlayerEliminated(e.Tick, e.PlayerId, e.NationName),
            ct),

        // Phase 2D: in-tick offer expiry. Reuse the diplomacy OfferResolved DTO so the client
        // hits the same handler whether the terminal transition came from a player action or
        // the expiry sweep. ResolvedAtTick equals the event tick by construction.
        TreatyOfferExpiredEvent e => group.SendAsync(
            DiplomacyEventNames.OfferResolved,
            new DiplomacyEventDtos.OfferResolved(
                e.OfferId, e.SenderPlayerId, e.ReceiverPlayerId,
                e.Kind, TreatyOfferStatus.Expired, e.Tick),
            ct),

        // Unknown event types are logged at the call site after this returns.
        _ => Task.CompletedTask,
    };
}
