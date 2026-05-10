using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Diplomacy;

namespace StarsAndSteel.Tests.Game.Diplomacy;

/// <summary>
/// Pure tests for <see cref="DiplomacyService"/>. No DbContext: we construct world / players /
/// offers in memory and pass already-loaded graphs.
/// </summary>
public sealed class DiplomacyServiceTests
{
    private readonly DiplomacyService _service = new();
    private const int CurrentTick = 10;

    // ---- DeclareWar ----------------------------------------------------

    [Fact]
    public void DeclareWar_emits_symmetric_pair_breaking_news_and_revokes_pending_offers()
    {
        var f = new Fixture();
        var pendingOffer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, status: TreatyOfferStatus.Pending);

        var result = _service.DeclareWar(
            f.World, f.Alice, f.Bob,
            currentStatus: DiplomaticStatus.Peace,
            pendingOffersBetween: new[] { pendingOffer });

        result.IsAccepted.Should().BeTrue();
        var m = result.Mutation!;
        m.RelationChanges.Should().HaveCount(2);
        m.RelationChanges.Should().AllSatisfy(rc =>
        {
            rc.NewStatus.Should().Be(DiplomaticStatus.War);
            rc.AtTick.Should().Be(CurrentTick);
            rc.GameWorldId.Should().Be(f.World.Id);
        });
        m.RelationChanges.Select(r => (r.FromPlayerId, r.ToPlayerId)).Should().BeEquivalentTo(new[]
        {
            (f.Alice.Id, f.Bob.Id),
            (f.Bob.Id, f.Alice.Id),
        });

        m.OfferChanges.Should().ContainSingle();
        m.OfferChanges[0].Kind.Should().Be(OfferChangeKind.MarkRevoked);
        pendingOffer.Status.Should().Be(TreatyOfferStatus.Revoked);
        pendingOffer.ResolvedAtTick.Should().Be(CurrentTick);

        m.News.Should().ContainSingle().Which.Should().Match<NewsItem>(n =>
            n.Severity == NewsSeverity.Breaking &&
            n.Category == NewsCategory.Diplomacy &&
            n.RelatedPlayerId == f.Alice.Id);
    }

    [Fact]
    public void DeclareWar_rejects_self()
    {
        var f = new Fixture();
        var result = _service.DeclareWar(
            f.World, f.Alice, f.Alice, DiplomaticStatus.Peace, Array.Empty<TreatyOffer>());
        result.Rejection.Should().Be(DiplomacyRejectionReason.SelfTargeted);
    }

    [Fact]
    public void DeclareWar_rejects_dead_target()
    {
        var f = new Fixture();
        f.Bob.IsAlive = false;
        var result = _service.DeclareWar(
            f.World, f.Alice, f.Bob, DiplomaticStatus.Peace, Array.Empty<TreatyOffer>());
        result.Rejection.Should().Be(DiplomacyRejectionReason.PlayerEliminated);
    }

    [Fact]
    public void DeclareWar_rejects_existing_war()
    {
        var f = new Fixture();
        var result = _service.DeclareWar(
            f.World, f.Alice, f.Bob, DiplomaticStatus.War, Array.Empty<TreatyOffer>());
        result.Rejection.Should().Be(DiplomacyRejectionReason.AlreadyAtWar);
    }

    [Fact]
    public void DeclareWar_rejects_when_world_ended()
    {
        var f = new Fixture();
        f.World.Status = GameWorldStatus.Ended;
        var result = _service.DeclareWar(
            f.World, f.Alice, f.Bob, DiplomaticStatus.Peace, Array.Empty<TreatyOffer>());
        result.Rejection.Should().Be(DiplomacyRejectionReason.GameEnded);
    }

    // ---- ProposeTreaty -------------------------------------------------

    [Fact]
    public void ProposeTreaty_creates_pending_offer_with_expiry()
    {
        var f = new Fixture();
        var result = _service.ProposeTreaty(
            f.World, f.Alice, f.Bob, TreatyOfferKind.Alliance,
            currentStatus: DiplomaticStatus.Peace,
            pendingOffersFromSender: Array.Empty<TreatyOffer>());

        result.IsAccepted.Should().BeTrue();
        var m = result.Mutation!;
        m.RelationChanges.Should().BeEmpty();
        m.OfferChanges.Should().ContainSingle();
        var oc = m.OfferChanges[0];
        oc.Kind.Should().Be(OfferChangeKind.Create);
        oc.Offer.SenderPlayerId.Should().Be(f.Alice.Id);
        oc.Offer.ReceiverPlayerId.Should().Be(f.Bob.Id);
        oc.Offer.Kind.Should().Be(TreatyOfferKind.Alliance);
        oc.Offer.Status.Should().Be(TreatyOfferStatus.Pending);
        oc.Offer.ProposedAtTick.Should().Be(CurrentTick);
        oc.Offer.ExpiresAtTick.Should().Be(CurrentTick + DiplomacyService.OfferLifetimeTicks);
    }

    [Fact]
    public void ProposeTreaty_peace_requires_active_war()
    {
        var f = new Fixture();
        var result = _service.ProposeTreaty(
            f.World, f.Alice, f.Bob, TreatyOfferKind.Peace,
            currentStatus: DiplomaticStatus.Peace,
            pendingOffersFromSender: Array.Empty<TreatyOffer>());
        result.Rejection.Should().Be(DiplomacyRejectionReason.AlreadyAtPeace);
    }

    [Fact]
    public void ProposeTreaty_alliance_rejected_when_already_allied()
    {
        var f = new Fixture();
        var result = _service.ProposeTreaty(
            f.World, f.Alice, f.Bob, TreatyOfferKind.Alliance,
            currentStatus: DiplomaticStatus.Allied,
            pendingOffersFromSender: Array.Empty<TreatyOffer>());
        result.Rejection.Should().Be(DiplomacyRejectionReason.AlreadyAllied);
    }

    [Fact]
    public void ProposeTreaty_alliance_rejected_at_war()
    {
        var f = new Fixture();
        var result = _service.ProposeTreaty(
            f.World, f.Alice, f.Bob, TreatyOfferKind.Alliance,
            currentStatus: DiplomaticStatus.War,
            pendingOffersFromSender: Array.Empty<TreatyOffer>());
        result.Rejection.Should().Be(DiplomacyRejectionReason.AlreadyAtWar);
    }

    [Fact]
    public void ProposeTreaty_blocks_duplicate_pending_offer_of_same_kind()
    {
        var f = new Fixture();
        var existing = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Pending);
        var result = _service.ProposeTreaty(
            f.World, f.Alice, f.Bob, TreatyOfferKind.Alliance,
            currentStatus: DiplomaticStatus.Peace,
            pendingOffersFromSender: new[] { existing });
        result.Rejection.Should().Be(DiplomacyRejectionReason.DuplicatePendingOffer);
    }

    [Fact]
    public void ProposeTreaty_allows_different_kind_when_other_pending()
    {
        var f = new Fixture();
        var existing = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.NonAggression, TreatyOfferStatus.Pending);
        var result = _service.ProposeTreaty(
            f.World, f.Alice, f.Bob, TreatyOfferKind.Alliance,
            currentStatus: DiplomaticStatus.Peace,
            pendingOffersFromSender: new[] { existing });
        result.IsAccepted.Should().BeTrue();
    }

    // ---- AcceptOffer ---------------------------------------------------

    [Fact]
    public void AcceptOffer_alliance_flips_relation_to_allied_symmetrically()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Pending);

        var result = _service.AcceptOffer(f.World, caller: f.Bob, offer);

        result.IsAccepted.Should().BeTrue();
        offer.Status.Should().Be(TreatyOfferStatus.Accepted);
        offer.ResolvedAtTick.Should().Be(CurrentTick);
        result.Mutation!.RelationChanges.Should().HaveCount(2);
        result.Mutation.RelationChanges.Should().AllSatisfy(rc =>
            rc.NewStatus.Should().Be(DiplomaticStatus.Allied));
    }

    [Fact]
    public void AcceptOffer_peace_flips_to_peace()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Peace, TreatyOfferStatus.Pending);

        var result = _service.AcceptOffer(f.World, f.Bob, offer);

        result.IsAccepted.Should().BeTrue();
        result.Mutation!.RelationChanges.Should().AllSatisfy(rc =>
            rc.NewStatus.Should().Be(DiplomaticStatus.Peace));
    }

    [Fact]
    public void AcceptOffer_only_receiver_can_accept()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Pending);

        var result = _service.AcceptOffer(f.World, caller: f.Alice, offer);

        result.Rejection.Should().Be(DiplomacyRejectionReason.NotOfferReceiver);
    }

    [Fact]
    public void AcceptOffer_rejects_terminal_offer()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Rejected);

        var result = _service.AcceptOffer(f.World, f.Bob, offer);

        result.Rejection.Should().Be(DiplomacyRejectionReason.OfferNotPending);
    }

    [Fact]
    public void AcceptOffer_rejects_missing_offer()
    {
        var f = new Fixture();
        var result = _service.AcceptOffer(f.World, f.Bob, offer: null);
        result.Rejection.Should().Be(DiplomacyRejectionReason.OfferNotFound);
    }

    // ---- RejectOffer ---------------------------------------------------

    [Fact]
    public void RejectOffer_marks_rejected_and_does_not_change_relation()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Pending);

        var result = _service.RejectOffer(f.World, f.Bob, offer);

        result.IsAccepted.Should().BeTrue();
        offer.Status.Should().Be(TreatyOfferStatus.Rejected);
        result.Mutation!.RelationChanges.Should().BeEmpty();
        result.Mutation.OfferChanges[0].Kind.Should().Be(OfferChangeKind.MarkRejected);
    }

    [Fact]
    public void RejectOffer_only_receiver_can_reject()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Pending);
        var result = _service.RejectOffer(f.World, f.Alice, offer);
        result.Rejection.Should().Be(DiplomacyRejectionReason.NotOfferReceiver);
    }

    // ---- RevokeOffer ---------------------------------------------------

    [Fact]
    public void RevokeOffer_marks_revoked_when_called_by_sender()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Pending);

        var result = _service.RevokeOffer(f.World, f.Alice, offer);

        result.IsAccepted.Should().BeTrue();
        offer.Status.Should().Be(TreatyOfferStatus.Revoked);
        result.Mutation!.OfferChanges[0].Kind.Should().Be(OfferChangeKind.MarkRevoked);
    }

    [Fact]
    public void RevokeOffer_only_sender_can_revoke()
    {
        var f = new Fixture();
        var offer = NewOffer(f, f.Alice, f.Bob, TreatyOfferKind.Alliance, TreatyOfferStatus.Pending);
        var result = _service.RevokeOffer(f.World, f.Bob, offer);
        result.Rejection.Should().Be(DiplomacyRejectionReason.NotOfferSender);
    }

    // ---- Helpers -------------------------------------------------------

    private static TreatyOffer NewOffer(
        Fixture f, Player sender, Player receiver, TreatyOfferKind kind, TreatyOfferStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id,
            SenderPlayerId = sender.Id,
            ReceiverPlayerId = receiver.Id,
            Kind = kind,
            Status = status,
            ProposedAtTick = CurrentTick,
            ExpiresAtTick = CurrentTick + DiplomacyService.OfferLifetimeTicks,
        };

    private sealed class Fixture
    {
        public GameWorld World { get; }
        public Player Alice { get; }
        public Player Bob { get; }

        public Fixture()
        {
            World = new GameWorld
            {
                Id = Guid.NewGuid(),
                Name = "Test World",
                Status = GameWorldStatus.Active,
                CurrentTick = CurrentTick,
            };
            Alice = NewPlayer("Alice");
            Bob = NewPlayer("Bob");
        }

        private Player NewPlayer(string name) => new()
        {
            Id = Guid.NewGuid(),
            GameWorldId = World.Id,
            NationName = name,
            FlagPrimaryHex = "#000000",
            FlagSecondaryHex = "#FFFFFF",
            IsAlive = true,
        };
    }
}
