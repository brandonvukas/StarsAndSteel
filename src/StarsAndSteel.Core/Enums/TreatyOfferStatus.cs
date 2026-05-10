namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.TreatyOffer"/>. Pending offers are revealed in the
/// receiver's inbox; the four terminal states are kept around for audit / news generation
/// and pruned by retention later (out of scope for Phase 2).
/// </summary>
public enum TreatyOfferStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Revoked = 3,
    Expired = 4
}
