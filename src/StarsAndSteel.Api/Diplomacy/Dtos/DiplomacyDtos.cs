using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Diplomacy.Dtos;

/// <summary>Declare war on another player in the same world.</summary>
public sealed record DeclareWarRequest(Guid TargetPlayerId);

/// <summary>Propose a treaty (peace, non-aggression, or alliance) to another player.</summary>
public sealed record ProposeTreatyRequest(Guid ReceiverPlayerId, string Kind);

/// <summary>Accept or reject a pending offer addressed to the caller.</summary>
public sealed record OfferActionRequest(Guid OfferId);

/// <summary>Returned on accepted relation-changing actions (declare-war, accept).</summary>
public sealed record DiplomacyActionAccepted(
    Guid PartyAPlayerId,
    Guid PartyBPlayerId,
    DiplomaticStatus NewStatus,
    int AtTick);

/// <summary>Returned on accepted offer-creating actions (propose).</summary>
public sealed record OfferCreated(
    Guid OfferId,
    Guid SenderPlayerId,
    Guid ReceiverPlayerId,
    TreatyOfferKind Kind,
    int ProposedAtTick,
    int ExpiresAtTick);

/// <summary>Returned on accepted offer-resolution actions (reject, revoke).</summary>
public sealed record OfferResolutionAccepted(
    Guid OfferId,
    TreatyOfferStatus Status,
    int ResolvedAtTick);
