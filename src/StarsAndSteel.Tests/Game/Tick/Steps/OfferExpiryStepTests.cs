using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class OfferExpiryStepTests
{
    [Fact]
    public void Pending_offer_at_expires_at_tick_is_marked_Expired_and_emits_event_and_news()
    {
        var world = NewWorld();
        world.CurrentTick = 9; // ProcessingTick = 10
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "USSR");
        var offer = NewOffer(world, alice, bob, TreatyOfferKind.Peace,
            proposedAtTick: 7, expiresAtTick: 10);
        var ctx = Context(world, pendingTreatyOffers: new List<TreatyOffer> { offer });

        new OfferExpiryStep().Execute(ctx);

        offer.Status.Should().Be(TreatyOfferStatus.Expired);
        offer.ResolvedAtTick.Should().Be(10);

        ctx.Events.OfType<TreatyOfferExpiredEvent>().Should().ContainSingle()
            .Which.Should().Match<TreatyOfferExpiredEvent>(e =>
                e.OfferId == offer.Id &&
                e.SenderPlayerId == alice.Id &&
                e.ReceiverPlayerId == bob.Id &&
                e.Kind == TreatyOfferKind.Peace &&
                e.Tick == 10);

        ctx.NewsItemsToInsert.Should().ContainSingle()
            .Which.Should().Match<NewsItem>(n =>
                n.Tick == 10 &&
                n.Severity == NewsSeverity.Info &&
                n.Category == NewsCategory.Diplomacy &&
                n.RelatedPlayerId == alice.Id);
    }

    [Fact]
    public void Pending_offer_past_expires_at_tick_is_also_expired()
    {
        var world = NewWorld();
        world.CurrentTick = 19; // ProcessingTick = 20
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "USSR");
        var offer = NewOffer(world, alice, bob, TreatyOfferKind.NonAggression,
            proposedAtTick: 5, expiresAtTick: 8);
        var ctx = Context(world, pendingTreatyOffers: new List<TreatyOffer> { offer });

        new OfferExpiryStep().Execute(ctx);

        offer.Status.Should().Be(TreatyOfferStatus.Expired);
        offer.ResolvedAtTick.Should().Be(20);
        ctx.Events.OfType<TreatyOfferExpiredEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Pending_offer_before_expires_at_tick_is_left_alone()
    {
        var world = NewWorld();
        world.CurrentTick = 4; // ProcessingTick = 5
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "USSR");
        var offer = NewOffer(world, alice, bob, TreatyOfferKind.Alliance,
            proposedAtTick: 3, expiresAtTick: 6);
        var ctx = Context(world, pendingTreatyOffers: new List<TreatyOffer> { offer });

        new OfferExpiryStep().Execute(ctx);

        offer.Status.Should().Be(TreatyOfferStatus.Pending);
        offer.ResolvedAtTick.Should().BeNull();
        ctx.Events.Should().BeEmpty();
        ctx.NewsItemsToInsert.Should().BeEmpty();
    }

    [Fact]
    public void Already_terminal_offers_in_the_pending_list_are_skipped_defensively()
    {
        var world = NewWorld();
        world.CurrentTick = 9;
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "USSR");
        // Pretend an earlier (future) step revoked it this same tick.
        var offer = NewOffer(world, alice, bob, TreatyOfferKind.Peace,
            proposedAtTick: 7, expiresAtTick: 10);
        offer.Status = TreatyOfferStatus.Revoked;
        offer.ResolvedAtTick = 10;
        var ctx = Context(world, pendingTreatyOffers: new List<TreatyOffer> { offer });

        new OfferExpiryStep().Execute(ctx);

        offer.Status.Should().Be(TreatyOfferStatus.Revoked);
        ctx.Events.Should().BeEmpty();
        ctx.NewsItemsToInsert.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_offers_are_expired_in_one_pass()
    {
        var world = NewWorld();
        world.CurrentTick = 9;
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "USSR");
        var charlie = AddPlayer(world, "PRC");
        var o1 = NewOffer(world, alice, bob, TreatyOfferKind.Peace, 7, 10);
        var o2 = NewOffer(world, charlie, alice, TreatyOfferKind.NonAggression, 6, 9);
        var o3Fresh = NewOffer(world, bob, charlie, TreatyOfferKind.Alliance, 9, 12); // not yet expired
        var ctx = Context(world, pendingTreatyOffers: new List<TreatyOffer> { o1, o2, o3Fresh });

        new OfferExpiryStep().Execute(ctx);

        o1.Status.Should().Be(TreatyOfferStatus.Expired);
        o2.Status.Should().Be(TreatyOfferStatus.Expired);
        o3Fresh.Status.Should().Be(TreatyOfferStatus.Pending);
        ctx.Events.OfType<TreatyOfferExpiredEvent>().Should().HaveCount(2);
        ctx.NewsItemsToInsert.Should().HaveCount(2);
    }

    [Fact]
    public void Empty_pending_list_is_a_noop()
    {
        var world = NewWorld();
        var ctx = Context(world);
        new OfferExpiryStep().Execute(ctx);
        ctx.Events.Should().BeEmpty();
        ctx.NewsItemsToInsert.Should().BeEmpty();
    }

    private static TreatyOffer NewOffer(
        GameWorld world, Player sender, Player receiver,
        TreatyOfferKind kind, int proposedAtTick, int expiresAtTick) =>
        new()
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            SenderPlayerId = sender.Id,
            ReceiverPlayerId = receiver.Id,
            Kind = kind,
            Status = TreatyOfferStatus.Pending,
            ProposedAtTick = proposedAtTick,
            ExpiresAtTick = expiresAtTick,
            ResolvedAtTick = null,
        };
}
