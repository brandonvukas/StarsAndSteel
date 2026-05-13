using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Hubs.Dtos;

/// <summary>
/// Wire-format records broadcast by <see cref="GameHub"/> for diplomacy actions.
/// Unlike tick events these fire OUT-OF-TICK from <see cref="DiplomacyController"/>
/// immediately after the action persists. They share the same per-world group
/// (<see cref="GameHub.WorldGroup"/>) so every connected player observes the change.
/// </summary>
public static class DiplomacyEventDtos
{
    /// <summary>
    /// Broadcast when the symmetric relation between two players changes status
    /// (declare-war, accepted peace/alliance/non-aggression). Both directional rows
    /// were updated atomically; the pair is reported once.
    /// </summary>
    public sealed record RelationChanged(
        Guid PartyAPlayerId,
        Guid PartyBPlayerId,
        DiplomaticStatus NewStatus,
        int AtTick);

    /// <summary>
    /// A new pending offer was created. Receiver's UI should surface it in the inbox;
    /// senders can use it to confirm their proposal landed.
    /// </summary>
    public sealed record OfferReceived(
        Guid OfferId,
        Guid SenderPlayerId,
        Guid ReceiverPlayerId,
        TreatyOfferKind Kind,
        int ProposedAtTick,
        int ExpiresAtTick);

    /// <summary>
    /// An existing pending offer transitioned to a terminal state
    /// (Accepted / Rejected / Revoked / Expired). Status carries which.
    /// </summary>
    public sealed record OfferResolved(
        Guid OfferId,
        Guid SenderPlayerId,
        Guid ReceiverPlayerId,
        TreatyOfferKind Kind,
        TreatyOfferStatus Status,
        int ResolvedAtTick);

    /// <summary>
    /// Phase 4e: directional sanction toggle. <see cref="FromPlayerId"/> is the sanctioner;
    /// <see cref="ToPlayerId"/> is the target. <see cref="IsSanctioning"/> indicates whether
    /// the sanction is now active (true = imposed) or lifted (false). Receivers should
    /// update both inbound and outbound sanction badges in their UI.
    /// </summary>
    public sealed record SanctionChanged(
        Guid FromPlayerId,
        Guid ToPlayerId,
        bool IsSanctioning,
        int AtTick);
}

public static class DiplomacyEventNames
{
    public const string RelationChanged = "RelationChanged";
    public const string OfferReceived = "OfferReceived";
    public const string OfferResolved = "OfferResolved";
    public const string SanctionChanged = "SanctionChanged";
}
