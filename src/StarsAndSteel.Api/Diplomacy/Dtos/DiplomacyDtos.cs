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

// ---- GET /diplomacy state shape -----------------------------------------

/// <summary>
/// Whole-world diplomacy snapshot for the calling player. Returned by
/// <c>GET /api/worlds/{id}/diplomacy</c>. Relation pairs are reported in canonical
/// (PartyA &lt; PartyB) order so clients can de-dupe trivially.
/// </summary>
public sealed record DiplomacyStateDto(
    Guid CallerPlayerId,
    IReadOnlyList<DiplomacyPlayerDto> Players,
    IReadOnlyList<DiplomacyRelationDto> Relations,
    IReadOnlyList<DiplomacyOfferDto> Inbox,
    IReadOnlyList<DiplomacyOfferDto> Outbox);

public sealed record DiplomacyPlayerDto(
    Guid PlayerId,
    string NationName,
    string FlagPrimaryHex,
    string FlagSecondaryHex,
    bool IsAi,
    bool IsAlive);

public sealed record DiplomacyRelationDto(
    Guid PartyAPlayerId,
    Guid PartyBPlayerId,
    DiplomaticStatus Status,
    int LastChangedAtTick);

public sealed record DiplomacyOfferDto(
    Guid OfferId,
    Guid SenderPlayerId,
    Guid ReceiverPlayerId,
    TreatyOfferKind Kind,
    TreatyOfferStatus Status,
    int ProposedAtTick,
    int ExpiresAtTick,
    int? ResolvedAtTick);

