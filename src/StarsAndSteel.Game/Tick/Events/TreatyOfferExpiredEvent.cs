using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Emitted by <see cref="Steps.OfferExpiryStep"/> when a pending <see cref="Core.Entities.TreatyOffer"/>
/// reaches its <c>ExpiresAtTick</c> without being accepted, rejected, or revoked. The Api layer
/// translates this into a <c>DiplomacyEventDtos.OfferResolved</c> with status
/// <see cref="TreatyOfferStatus.Expired"/> so the client treats expiry symmetrically with the
/// other terminal transitions.
/// </summary>
public sealed record TreatyOfferExpiredEvent(
    int Tick,
    Guid OfferId,
    Guid SenderPlayerId,
    Guid ReceiverPlayerId,
    TreatyOfferKind Kind) : TickEvent(Tick);
