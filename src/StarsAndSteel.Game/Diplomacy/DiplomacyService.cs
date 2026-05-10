using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Diplomacy;

/// <summary>
/// Why a diplomatic action was rejected. Maps 1:1 to HTTP status codes in
/// <c>DiplomacyController</c>.
/// </summary>
public enum DiplomacyRejectionReason
{
    GameEnded,                       // 409
    SelfTargeted,                    // 400 — cannot act diplomatically on yourself
    PlayerNotInWorld,                // 404 — sender or receiver missing / wrong world
    PlayerEliminated,                // 409 — target is dead
    AlreadyAtWar,                    // 409 — declare-war on an existing enemy
    AlreadyAllied,                   // 409 — propose alliance with an ally
    AlreadyAtPeace,                  // 409 — propose peace while already at peace
    DuplicatePendingOffer,           // 409 — sender already has a pending offer of this kind
    OfferNotFound,                   // 404 — accept/reject/revoke missing offer
    OfferNotPending,                 // 409 — terminal offers cannot be acted on
    OfferNotForCaller,               // 403 — caller is neither the sender nor receiver
    NotOfferReceiver,                // 403 — only the receiver can accept/reject
    NotOfferSender,                  // 403 — only the sender can revoke
}

/// <summary>
/// Outcome of a pure diplomacy action. Exactly one of (<see cref="Mutation"/>) or
/// (<see cref="Rejection"/>) is non-null. The controller persists the mutation +
/// emitted news + broadcasts the events in a single transaction.
/// </summary>
public sealed record DiplomacyResult(
    DiplomacyMutation? Mutation,
    DiplomacyRejectionReason? Rejection,
    string? RejectionMessage)
{
    public static DiplomacyResult Accept(DiplomacyMutation mutation) =>
        new(mutation, null, null);

    public static DiplomacyResult Reject(DiplomacyRejectionReason reason, string message) =>
        new(null, reason, message);

    public bool IsAccepted => Rejection is null;
}

/// <summary>
/// What the service decided to change. The controller applies these mutations against
/// EF tracked entities and saves them.
/// <para/>
/// <see cref="RelationChanges"/> is the symmetric pair of rows to upsert when the
/// effective relation transitions (declare-war, accept-alliance, accept-peace).
/// <see cref="OfferChanges"/> covers create / status-flip on <see cref="TreatyOffer"/>.
/// <see cref="News"/> are headlines to insert and broadcast.
/// </summary>
public sealed record DiplomacyMutation(
    IReadOnlyList<RelationChange> RelationChanges,
    IReadOnlyList<OfferChange> OfferChanges,
    IReadOnlyList<NewsItem> News);

/// <summary>
/// Symmetric directed pair to upsert into <see cref="DiplomaticRelation"/>. The service
/// always emits TWO rows (A→B and B→A) so the existing directional schema behaves as a
/// symmetric model: querying either direction yields the same status.
/// </summary>
public sealed record RelationChange(
    Guid GameWorldId,
    Guid FromPlayerId,
    Guid ToPlayerId,
    DiplomaticStatus NewStatus,
    int AtTick);

public enum OfferChangeKind { Create, MarkAccepted, MarkRejected, MarkRevoked, MarkExpired }

public sealed record OfferChange(
    OfferChangeKind Kind,
    TreatyOffer Offer);

/// <summary>
/// Pure diplomacy state machine. No DbContext, no SignalR — the controller loads
/// the world, players, and the affected relation/offer rows, hands them in, and the
/// service decides what should change.
/// </summary>
public sealed class DiplomacyService
{
    /// <summary>How many ticks a fresh proposal stays pending before auto-expiring.</summary>
    public const int OfferLifetimeTicks = 3;

    /// <summary>
    /// Declare war: free, instant. The relation transitions to War immediately and
    /// any pending non-war offers between the two parties are revoked as a side effect.
    /// </summary>
    public DiplomacyResult DeclareWar(
        GameWorld world,
        Player declarer,
        Player target,
        DiplomaticStatus currentStatus,
        IReadOnlyList<TreatyOffer> pendingOffersBetween)
    {
        if (world.Status == GameWorldStatus.Ended)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.GameEnded, "World has ended.");
        if (declarer.Id == target.Id)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.SelfTargeted, "Cannot declare war on yourself.");
        if (!target.IsAlive)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.PlayerEliminated, "Target has been eliminated.");
        if (currentStatus == DiplomaticStatus.War)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.AlreadyAtWar, "Already at war with this player.");

        var tick = world.CurrentTick;
        var relations = new[]
        {
            new RelationChange(world.Id, declarer.Id, target.Id, DiplomaticStatus.War, tick),
            new RelationChange(world.Id, target.Id, declarer.Id, DiplomaticStatus.War, tick),
        };

        // Auto-revoke any pending offers between the pair — war supersedes diplomacy.
        var offerChanges = new List<OfferChange>();
        foreach (var offer in pendingOffersBetween)
        {
            if (offer.Status != TreatyOfferStatus.Pending) continue;
            offer.Status = TreatyOfferStatus.Revoked;
            offer.ResolvedAtTick = tick;
            offerChanges.Add(new OfferChange(OfferChangeKind.MarkRevoked, offer));
        }

        var news = new List<NewsItem>
        {
            BuildNews(world.Id, tick,
                $"{declarer.NationName} declares war on {target.NationName}",
                $"{declarer.NationName} has formally declared war on {target.NationName}. Hostilities are now permitted.",
                NewsSeverity.Breaking, declarer.Id),
        };

        return DiplomacyResult.Accept(new DiplomacyMutation(relations, offerChanges, news));
    }

    /// <summary>
    /// Propose a peace, non-aggression, or alliance treaty. Creates a Pending offer that
    /// auto-expires after <see cref="OfferLifetimeTicks"/>.
    /// </summary>
    public DiplomacyResult ProposeTreaty(
        GameWorld world,
        Player sender,
        Player receiver,
        TreatyOfferKind kind,
        DiplomaticStatus currentStatus,
        IReadOnlyList<TreatyOffer> pendingOffersFromSender)
    {
        if (world.Status == GameWorldStatus.Ended)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.GameEnded, "World has ended.");
        if (sender.Id == receiver.Id)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.SelfTargeted, "Cannot propose a treaty to yourself.");
        if (!receiver.IsAlive)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.PlayerEliminated, "Target has been eliminated.");

        switch (kind)
        {
            case TreatyOfferKind.Peace when currentStatus != DiplomaticStatus.War:
                return DiplomacyResult.Reject(DiplomacyRejectionReason.AlreadyAtPeace,
                    "A peace treaty requires an active war between the parties.");
            case TreatyOfferKind.Alliance when currentStatus == DiplomaticStatus.Allied:
                return DiplomacyResult.Reject(DiplomacyRejectionReason.AlreadyAllied,
                    "Already allied with this player.");
            case TreatyOfferKind.Alliance when currentStatus == DiplomaticStatus.War:
                return DiplomacyResult.Reject(DiplomacyRejectionReason.AlreadyAtWar,
                    "Cannot propose an alliance while at war. Sign peace first.");
        }

        // Block duplicate pending offers of the same kind from the same sender→receiver direction.
        if (pendingOffersFromSender.Any(o =>
                o.Status == TreatyOfferStatus.Pending &&
                o.SenderPlayerId == sender.Id &&
                o.ReceiverPlayerId == receiver.Id &&
                o.Kind == kind))
        {
            return DiplomacyResult.Reject(DiplomacyRejectionReason.DuplicatePendingOffer,
                $"You already have a pending {kind} offer to this player.");
        }

        var tick = world.CurrentTick;
        var offer = new TreatyOffer
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            SenderPlayerId = sender.Id,
            ReceiverPlayerId = receiver.Id,
            Kind = kind,
            Status = TreatyOfferStatus.Pending,
            ProposedAtTick = tick,
            ExpiresAtTick = tick + OfferLifetimeTicks,
            ResolvedAtTick = null,
        };

        var news = new List<NewsItem>
        {
            BuildNews(world.Id, tick,
                $"{sender.NationName} proposes {KindLabel(kind)} to {receiver.NationName}",
                $"{sender.NationName} has formally proposed a {KindLabel(kind)} agreement with {receiver.NationName}.",
                NewsSeverity.Notable, sender.Id),
        };

        return DiplomacyResult.Accept(new DiplomacyMutation(
            Array.Empty<RelationChange>(),
            new[] { new OfferChange(OfferChangeKind.Create, offer) },
            news));
    }

    /// <summary>
    /// Accept a pending offer addressed to <paramref name="caller"/>. Flips the symmetric
    /// relation pair to the agreed status and marks the offer Accepted.
    /// </summary>
    public DiplomacyResult AcceptOffer(GameWorld world, Player caller, TreatyOffer? offer)
    {
        if (world.Status == GameWorldStatus.Ended)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.GameEnded, "World has ended.");
        if (offer is null)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.OfferNotFound, "Offer not found.");
        if (offer.Status != TreatyOfferStatus.Pending)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.OfferNotPending,
                $"Offer is already {offer.Status}.");
        if (offer.ReceiverPlayerId != caller.Id)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.NotOfferReceiver,
                "Only the offer's receiver can accept it.");

        var tick = world.CurrentTick;
        var newStatus = offer.Kind switch
        {
            TreatyOfferKind.Peace         => DiplomaticStatus.Peace,
            TreatyOfferKind.NonAggression => DiplomaticStatus.NonAggression,
            TreatyOfferKind.Alliance      => DiplomaticStatus.Allied,
            _ => DiplomaticStatus.Peace,
        };

        offer.Status = TreatyOfferStatus.Accepted;
        offer.ResolvedAtTick = tick;

        var relations = new[]
        {
            new RelationChange(world.Id, offer.SenderPlayerId, offer.ReceiverPlayerId, newStatus, tick),
            new RelationChange(world.Id, offer.ReceiverPlayerId, offer.SenderPlayerId, newStatus, tick),
        };

        var news = new List<NewsItem>
        {
            BuildNews(world.Id, tick,
                $"{KindLabel(offer.Kind)} accepted",
                $"The proposed {KindLabel(offer.Kind)} between the two nations has been ratified.",
                NewsSeverity.Notable, offer.SenderPlayerId),
        };

        return DiplomacyResult.Accept(new DiplomacyMutation(
            relations,
            new[] { new OfferChange(OfferChangeKind.MarkAccepted, offer) },
            news));
    }

    /// <summary>Reject a pending offer. Receiver-only. Marks the offer Rejected and emits news.</summary>
    public DiplomacyResult RejectOffer(GameWorld world, Player caller, TreatyOffer? offer)
    {
        if (world.Status == GameWorldStatus.Ended)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.GameEnded, "World has ended.");
        if (offer is null)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.OfferNotFound, "Offer not found.");
        if (offer.Status != TreatyOfferStatus.Pending)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.OfferNotPending,
                $"Offer is already {offer.Status}.");
        if (offer.ReceiverPlayerId != caller.Id)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.NotOfferReceiver,
                "Only the offer's receiver can reject it.");

        var tick = world.CurrentTick;
        offer.Status = TreatyOfferStatus.Rejected;
        offer.ResolvedAtTick = tick;

        var news = new List<NewsItem>
        {
            BuildNews(world.Id, tick,
                $"{KindLabel(offer.Kind)} proposal rejected",
                "The proposed agreement has been declined.",
                NewsSeverity.Info, offer.SenderPlayerId),
        };

        return DiplomacyResult.Accept(new DiplomacyMutation(
            Array.Empty<RelationChange>(),
            new[] { new OfferChange(OfferChangeKind.MarkRejected, offer) },
            news));
    }

    /// <summary>Sender-only: pull back a pending offer before the receiver acts.</summary>
    public DiplomacyResult RevokeOffer(GameWorld world, Player caller, TreatyOffer? offer)
    {
        if (world.Status == GameWorldStatus.Ended)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.GameEnded, "World has ended.");
        if (offer is null)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.OfferNotFound, "Offer not found.");
        if (offer.Status != TreatyOfferStatus.Pending)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.OfferNotPending,
                $"Offer is already {offer.Status}.");
        if (offer.SenderPlayerId != caller.Id)
            return DiplomacyResult.Reject(DiplomacyRejectionReason.NotOfferSender,
                "Only the offer's sender can revoke it.");

        var tick = world.CurrentTick;
        offer.Status = TreatyOfferStatus.Revoked;
        offer.ResolvedAtTick = tick;

        var news = new List<NewsItem>
        {
            BuildNews(world.Id, tick,
                $"{KindLabel(offer.Kind)} proposal withdrawn",
                "The proposing nation has withdrawn its offer.",
                NewsSeverity.Info, offer.SenderPlayerId),
        };

        return DiplomacyResult.Accept(new DiplomacyMutation(
            Array.Empty<RelationChange>(),
            new[] { new OfferChange(OfferChangeKind.MarkRevoked, offer) },
            news));
    }

    private static string KindLabel(TreatyOfferKind kind) => kind switch
    {
        TreatyOfferKind.Peace         => "peace",
        TreatyOfferKind.NonAggression => "non-aggression pact",
        TreatyOfferKind.Alliance      => "alliance",
        _ => kind.ToString(),
    };

    private static NewsItem BuildNews(
        Guid worldId, int tick, string headline, string body,
        NewsSeverity severity, Guid? relatedPlayerId) => new()
    {
        Id = Guid.NewGuid(),
        GameWorldId = worldId,
        Tick = tick,
        Headline = headline,
        Body = body,
        Severity = severity,
        Category = NewsCategory.Diplomacy,
        RelatedPlayerId = relatedPlayerId,
    };
}
