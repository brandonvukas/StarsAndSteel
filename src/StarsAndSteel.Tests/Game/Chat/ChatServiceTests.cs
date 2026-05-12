using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Chat;

namespace StarsAndSteel.Tests.Game.Chat;

/// <summary>
/// Pure tests for <see cref="ChatService"/>. No DbContext: hand-built Player graphs.
/// </summary>
public sealed class ChatServiceTests
{
    private readonly ChatService _service = new();
    private static readonly DateTime Now = new(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);

    // ---- Acceptance ----------------------------------------------------

    [Fact]
    public void Send_global_accepted_with_null_recipient()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Global, recipient: null,
            body: "hello world", gameEnded: false, utcNow: Now);

        result.IsAccepted.Should().BeTrue();
        var m = result.Mutation!;
        m.Scope.Should().Be(ChatScope.Global);
        m.FromPlayerId.Should().Be(f.Alice.Id);
        m.ToPlayerId.Should().BeNull();
        m.Body.Should().Be("hello world");
        m.GameWorldId.Should().Be(f.World.Id);
        m.SentAtUtc.Should().Be(Now);
        m.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Send_alliance_accepted_with_null_recipient()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Alliance, recipient: null,
            body: "allies only", gameEnded: false, utcNow: Now);

        result.IsAccepted.Should().BeTrue();
        result.Mutation!.Scope.Should().Be(ChatScope.Alliance);
        result.Mutation.ToPlayerId.Should().BeNull();
    }

    [Fact]
    public void Send_direct_accepted_to_other_player()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Direct, recipient: f.Bob,
            body: "hey bob", gameEnded: false, utcNow: Now);

        result.IsAccepted.Should().BeTrue();
        result.Mutation!.Scope.Should().Be(ChatScope.Direct);
        result.Mutation.ToPlayerId.Should().Be(f.Bob.Id);
    }

    [Fact]
    public void Send_trims_body_whitespace()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Global, null, "   trimmed   ", false, Now);
        result.Mutation!.Body.Should().Be("trimmed");
    }

    // ---- Rejections ----------------------------------------------------

    [Fact]
    public void Send_rejected_when_game_ended()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Global, null, "hi", gameEnded: true, Now);
        result.Rejection.Should().Be(ChatRejectionReason.GameEnded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Send_rejected_when_body_blank(string body)
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Global, null, body, false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.BodyEmpty);
    }

    [Fact]
    public void Send_rejected_when_body_too_long()
    {
        var f = new Fixture();
        var body = new string('x', 501);
        var result = _service.Send(f.Alice, ChatScope.Global, null, body, false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.BodyTooLong);
    }

    [Fact]
    public void Send_rejected_when_direct_message_to_self()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Direct, recipient: f.Alice,
            body: "hi me", gameEnded: false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.SelfTargeted);
    }

    [Fact]
    public void Send_rejected_when_direct_recipient_missing()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Direct, recipient: null,
            body: "hi nobody", gameEnded: false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.RecipientNotInWorld);
    }

    [Fact]
    public void Send_rejected_when_direct_recipient_in_other_world()
    {
        var f = new Fixture();
        var stranger = new Player
        {
            Id = Guid.NewGuid(),
            GameWorldId = Guid.NewGuid(), // different world
            NationName = "Stranger",
            FlagPrimaryHex = "#000000",
            FlagSecondaryHex = "#FFFFFF",
            IsAlive = true,
        };
        var result = _service.Send(f.Alice, ChatScope.Direct, stranger, "hi", false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.RecipientNotInWorld);
    }

    [Fact]
    public void Send_rejected_when_direct_recipient_dead()
    {
        var f = new Fixture();
        f.Bob.IsAlive = false;
        var result = _service.Send(f.Alice, ChatScope.Direct, f.Bob, "hi corpse", false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.RecipientEliminated);
    }

    [Fact]
    public void Send_rejected_when_global_carries_recipient()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Global, recipient: f.Bob,
            body: "leaky", gameEnded: false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.InvalidScopePayload);
    }

    [Fact]
    public void Send_rejected_when_alliance_carries_recipient()
    {
        var f = new Fixture();
        var result = _service.Send(f.Alice, ChatScope.Alliance, recipient: f.Bob,
            body: "leaky", gameEnded: false, Now);
        result.Rejection.Should().Be(ChatRejectionReason.InvalidScopePayload);
    }

    // ---- Helpers -------------------------------------------------------

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
                CurrentTick = 1,
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
