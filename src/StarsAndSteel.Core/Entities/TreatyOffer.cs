using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// A diplomatic proposal sent from one player to another within a single <see cref="GameWorld"/>.
/// Pending offers are surfaced in the receiver's diplomacy inbox; on acceptance the diplomacy
/// service writes the symmetric pair of <see cref="DiplomaticRelation"/> rows. Offers auto-expire
/// after <see cref="ExpiresAtTick"/> via the tick pipeline (see Phase 2D).
/// </summary>
public class TreatyOffer
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid SenderPlayerId { get; set; }
    public Player SenderPlayer { get; set; } = default!;

    public Guid ReceiverPlayerId { get; set; }
    public Player ReceiverPlayer { get; set; } = default!;

    public TreatyOfferKind Kind { get; set; }

    public TreatyOfferStatus Status { get; set; } = TreatyOfferStatus.Pending;

    /// <summary>World tick at which the offer was proposed.</summary>
    public int ProposedAtTick { get; set; }

    /// <summary>World tick at which the offer auto-transitions to <see cref="TreatyOfferStatus.Expired"/>.</summary>
    public int ExpiresAtTick { get; set; }

    /// <summary>Tick at which the offer reached its terminal status. Null while Pending.</summary>
    public int? ResolvedAtTick { get; set; }
}
