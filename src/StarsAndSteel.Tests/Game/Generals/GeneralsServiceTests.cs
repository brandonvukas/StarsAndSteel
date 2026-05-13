using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Generals;

namespace StarsAndSteel.Tests.Game.Generals;

/// <summary>
/// Phase 3f: pure-service tests for theater commander recruit + assign. POCO graphs only;
/// no DbContext. Mirrors <c>OrderServiceTests</c> in shape — every test uses a fresh
/// fixture so mutations don't bleed across cases.
/// </summary>
public class GeneralsServiceTests
{
    private readonly GeneralsService _svc = new();

    [Fact]
    public void Recruit_succeeds_when_player_has_money_and_no_general()
    {
        var f = new Fixture();

        var result = _svc.RecruitGeneral(f.Alice, Array.Empty<General>(),
            "Patton", GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.General.Should().NotBeNull();
        result.General!.Name.Should().Be("Patton");
        result.General.OwnerPlayerId.Should().Be(f.Alice.Id);
        result.General.GameWorldId.Should().Be(f.Alice.GameWorldId);
        result.General.AssignedProvinceId.Should().BeNull();
        result.MoneyDelta.Should().Be(GeneralsService.RecruitMoneyCost);
    }

    [Fact]
    public void Recruit_trims_name()
    {
        var f = new Fixture();

        var result = _svc.RecruitGeneral(f.Alice, Array.Empty<General>(),
            "  Eisenhower  ", GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.General!.Name.Should().Be("Eisenhower");
    }

    [Fact]
    public void Recruit_rejects_when_world_ended()
    {
        var f = new Fixture();
        var result = _svc.RecruitGeneral(f.Alice, Array.Empty<General>(),
            "x", GameWorldStatus.Ended);
        result.Rejection.Should().Be(GeneralsRejectionReason.GameEnded);
    }

    [Fact]
    public void Recruit_rejects_blank_or_too_long_name()
    {
        var f = new Fixture();

        _svc.RecruitGeneral(f.Alice, Array.Empty<General>(), "   ", GameWorldStatus.Active)
            .Rejection.Should().Be(GeneralsRejectionReason.NameTooLongOrEmpty);

        var tooLong = new string('a', 81);
        _svc.RecruitGeneral(f.Alice, Array.Empty<General>(), tooLong, GameWorldStatus.Active)
            .Rejection.Should().Be(GeneralsRejectionReason.NameTooLongOrEmpty);
    }

    [Fact]
    public void Recruit_rejects_when_player_already_has_a_general()
    {
        var f = new Fixture();
        var existing = new[] { new General { Id = Guid.NewGuid(), GameWorldId = f.WorldId,
                                              OwnerPlayerId = f.Alice.Id, Name = "Existing" } };

        var result = _svc.RecruitGeneral(f.Alice, existing, "Second", GameWorldStatus.Active);
        result.Rejection.Should().Be(GeneralsRejectionReason.AlreadyHasGeneral);
    }

    [Fact]
    public void Recruit_rejects_when_player_cannot_afford_cost()
    {
        var f = new Fixture();
        f.Alice.Money = GeneralsService.RecruitMoneyCost - 1;

        var result = _svc.RecruitGeneral(f.Alice, Array.Empty<General>(),
            "Patton", GameWorldStatus.Active);

        result.Rejection.Should().Be(GeneralsRejectionReason.InsufficientResources);
    }

    [Fact]
    public void Debit_for_recruit_subtracts_money()
    {
        var f = new Fixture();
        var before = f.Alice.Money;

        GeneralsService.DebitForRecruit(f.Alice);

        (before - f.Alice.Money).Should().Be(GeneralsService.RecruitMoneyCost);
    }

    [Fact]
    public void Assign_succeeds_to_friendly_province()
    {
        var f = new Fixture();
        var general = new General
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.WorldId,
            OwnerPlayerId = f.Alice.Id,
            Name = "Patton",
        };

        var result = _svc.AssignGeneral(f.Alice, general, f.AliceProvince, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        general.AssignedProvinceId.Should().Be(f.AliceProvince.Id);
    }

    [Fact]
    public void Assign_rejects_when_general_owned_by_someone_else()
    {
        var f = new Fixture();
        var general = new General
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.WorldId,
            OwnerPlayerId = f.Bob.Id,
            Name = "Bob's General",
        };

        var result = _svc.AssignGeneral(f.Alice, general, f.AliceProvince, GameWorldStatus.Active);

        result.Rejection.Should().Be(GeneralsRejectionReason.GeneralNotOwnedByCaller);
    }

    [Fact]
    public void Assign_rejects_to_enemy_or_neutral_province()
    {
        var f = new Fixture();
        var general = new General
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.WorldId,
            OwnerPlayerId = f.Alice.Id,
            Name = "Patton",
        };

        _svc.AssignGeneral(f.Alice, general, f.BobProvince, GameWorldStatus.Active)
            .Rejection.Should().Be(GeneralsRejectionReason.ProvinceNotOwnedByCaller);
        _svc.AssignGeneral(f.Alice, general, f.NeutralProvince, GameWorldStatus.Active)
            .Rejection.Should().Be(GeneralsRejectionReason.ProvinceNotOwnedByCaller);
    }

    [Fact]
    public void Assign_can_reassign_between_friendly_provinces()
    {
        var f = new Fixture();
        var second = new Province
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, Name = "A2",
            Type = ProvinceType.Industrial, OwnerPlayerId = f.Alice.Id,
        };
        var general = new General
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Name = "Patton", AssignedProvinceId = f.AliceProvince.Id,
        };

        var result = _svc.AssignGeneral(f.Alice, general, second, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        general.AssignedProvinceId.Should().Be(second.Id);
    }

    private sealed class Fixture
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public Player Alice { get; }
        public Player Bob { get; }
        public Province AliceProvince { get; }
        public Province BobProvince { get; }
        public Province NeutralProvince { get; }

        public Fixture()
        {
            Alice = new Player
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, NationName = "Alice",
                FlagPrimaryHex = "#000000", FlagSecondaryHex = "#ffffff",
                Money = 100_000,
            };
            Bob = new Player
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, NationName = "Bob",
                FlagPrimaryHex = "#111111", FlagSecondaryHex = "#222222",
            };
            AliceProvince = new Province
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, Name = "A",
                Type = ProvinceType.Capital, OwnerPlayerId = Alice.Id,
            };
            BobProvince = new Province
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, Name = "B",
                Type = ProvinceType.Industrial, OwnerPlayerId = Bob.Id,
            };
            NeutralProvince = new Province
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, Name = "C",
                Type = ProvinceType.Resource, OwnerPlayerId = null,
            };
        }
    }
}
