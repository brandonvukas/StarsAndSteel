using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Snapshots;

namespace StarsAndSteel.Tests.Game.Snapshots;

/// <summary>
/// Pure-C# tests against <see cref="SnapshotService"/>. Builds a tiny
/// hand-crafted world graph (Alice owns A, B is adjacent enemy territory,
/// C is unrelated neutral) and asserts the fog-of-war filter behaves per
/// docs/06 §"DTOs (shape sketch)".
/// </summary>
public sealed class SnapshotServiceTests
{
    [Fact]
    public void Owned_province_is_visible_with_full_detail()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        var a = snap.Provinces.Single(p => p.Id == f.ProvinceA.Id);
        a.Visible.Should().BeTrue();
        a.MoraleLevel.Should().Be(80);
        a.GarrisonStrength.Should().Be(1_000, "Alice has one MechInfantry strength 1000 here");
        a.Buildings.Should().ContainSingle(b => b.Type == nameof(BuildingType.RecruitmentCenter));
        a.OwnerColorHex.Should().Be(f.Alice.FlagPrimaryHex);
    }

    [Fact]
    public void Adjacent_enemy_province_is_visible_but_only_partially_detailed()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        var b = snap.Provinces.Single(p => p.Id == f.ProvinceB.Id);
        b.Visible.Should().BeTrue("B is adjacent to Alice's A");
        b.OwnerPlayerId.Should().Be(f.Bob.Id);
        b.MoraleLevel.Should().Be(70, "morale is shown for visible provinces");
        b.GarrisonStrength.Should().Be(500, "Bob's stationed enemy strength is summed");
        b.Buildings.Should().HaveCount(1);
    }

    [Fact]
    public void Distant_neutral_province_is_invisible_and_intel_is_masked()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        var c = snap.Provinces.Single(p => p.Id == f.ProvinceC.Id);
        c.Visible.Should().BeFalse();
        c.MoraleLevel.Should().BeNull();
        c.GarrisonStrength.Should().BeNull();
        c.Buildings.Should().BeEmpty();

        // Polygon-rendering data still leaks (deliberately — the client needs
        // it to draw the map). Owner color only leaks if non-null.
        c.Name.Should().Be(f.ProvinceC.Name);
        c.CenterX.Should().Be(f.ProvinceC.CenterX);
        c.OwnerColorHex.Should().BeNull("C is neutral");
    }

    [Fact]
    public void My_units_are_returned_with_full_detail()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.MyUnits.Should().ContainSingle();
        var u = snap.MyUnits.Single();
        u.OwnerProvinceId(f.ProvinceA.Id);
        u.Type.Should().Be(nameof(UnitType.MechInfantry));
        u.Strength.Should().Be(1_000);
        u.Morale.Should().Be(90);       // not masked
        u.Experience.Should().Be(3);    // not masked
    }

    [Fact]
    public void Visible_enemy_units_are_surfaced_with_masked_morale()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.VisibleEnemyUnits.Should().ContainSingle();
        var enemy = snap.VisibleEnemyUnits.Single();
        enemy.OwnerPlayerId.Should().Be(f.Bob.Id);
        enemy.LocationProvinceId.Should().Be(f.ProvinceB.Id);
        enemy.Strength.Should().Be(500);
        // Morale + experience are absent from the DTO, by design.
    }

    [Fact]
    public void Enemy_units_in_invisible_provinces_are_omitted()
    {
        var f = NewFixture();
        // Plant a Bob unit in C (invisible to Alice). Should NOT show up.
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id,
            GameWorld = f.World,
            OwnerPlayerId = f.Bob.Id,
            OwnerPlayer = f.Bob,
            LocationProvinceId = f.ProvinceC.Id,
            LocationProvince = f.ProvinceC,
            Type = UnitType.MainBattleTank,
            Domain = UnitDomain.Ground,
            Strength = 9_999,
            Morale = 100,
            Experience = 0,
        });

        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.VisibleEnemyUnits.Should().HaveCount(1);
        snap.VisibleEnemyUnits.Should().NotContain(u => u.Strength == 9_999);
    }

    [Fact]
    public void Enemy_units_in_transit_are_invisible_even_in_visible_province()
    {
        var f = NewFixture();
        // Bob's transiting unit "passing through" B (which is visible to Alice).
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id,
            GameWorld = f.World,
            OwnerPlayerId = f.Bob.Id,
            OwnerPlayer = f.Bob,
            LocationProvinceId = null,
            Type = UnitType.MultiroleFighter,
            Domain = UnitDomain.Air,
            Strength = 250,
            IsInTransit = true,
            TransitFromProvinceId = f.ProvinceB.Id,
            TransitToProvinceId = f.ProvinceC.Id,
            TransitArrivalTick = 5,
        });

        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);
        snap.VisibleEnemyUnits.Should().NotContain(u => u.Type == nameof(UnitType.MultiroleFighter));
    }

    // ---- Phase 3c: submarine stealth filter ----

    [Fact]
    public void Enemy_submarine_in_visible_province_is_hidden_without_my_asw_present()
    {
        var f = NewFixture();
        // Bob's submarine in ProvinceB (visible to Alice via adjacency).
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id, GameWorld = f.World,
            OwnerPlayerId = f.Bob.Id, OwnerPlayer = f.Bob,
            LocationProvinceId = f.ProvinceB.Id, LocationProvince = f.ProvinceB,
            Type = UnitType.Submarine, Domain = UnitDomain.Naval,
            Strength = 800, Morale = 100,
        });

        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.VisibleEnemyUnits.Should().NotContain(u => u.Type == nameof(UnitType.Submarine));
    }

    [Fact]
    public void Enemy_submarine_is_revealed_when_my_destroyer_is_co_located()
    {
        var f = NewFixture();
        // Bob's sub in ProvinceB.
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id, GameWorld = f.World,
            OwnerPlayerId = f.Bob.Id, OwnerPlayer = f.Bob,
            LocationProvinceId = f.ProvinceB.Id, LocationProvince = f.ProvinceB,
            Type = UnitType.Submarine, Domain = UnitDomain.Naval,
            Strength = 800, Morale = 100,
        });
        // Alice's ASW destroyer in the same province.
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id, GameWorld = f.World,
            OwnerPlayerId = f.Alice.Id, OwnerPlayer = f.Alice,
            LocationProvinceId = f.ProvinceB.Id, LocationProvince = f.ProvinceB,
            Type = UnitType.Destroyer, Domain = UnitDomain.Naval,
            Strength = 600, Morale = 100,
        });

        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.VisibleEnemyUnits.Should().Contain(u =>
            u.Type == nameof(UnitType.Submarine) && u.OwnerPlayerId == f.Bob.Id);
    }

    [Fact]
    public void Enemy_submarine_is_NOT_revealed_by_my_carrier_alone_no_asw()
    {
        var f = NewFixture();
        // Bob's sub in ProvinceB.
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id, GameWorld = f.World,
            OwnerPlayerId = f.Bob.Id, OwnerPlayer = f.Bob,
            LocationProvinceId = f.ProvinceB.Id, LocationProvince = f.ProvinceB,
            Type = UnitType.Submarine, Domain = UnitDomain.Naval,
            Strength = 800, Morale = 100,
        });
        // Alice has a carrier (no ASW) co-located. Should still be blind.
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id, GameWorld = f.World,
            OwnerPlayerId = f.Alice.Id, OwnerPlayer = f.Alice,
            LocationProvinceId = f.ProvinceB.Id, LocationProvince = f.ProvinceB,
            Type = UnitType.AircraftCarrier, Domain = UnitDomain.Naval,
            Strength = 1000, Morale = 100,
        });

        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.VisibleEnemyUnits.Should().NotContain(u => u.Type == nameof(UnitType.Submarine));
    }

    [Fact]
    public void Me_block_carries_resources_and_is_alive()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.Me.PlayerId.Should().Be(f.Alice.Id);
        snap.Me.Resources.Money.Should().Be(5_000);
        snap.Me.IsAlive.Should().BeTrue();
    }

    [Fact]
    public void Player_summaries_omit_resources_but_include_owned_count()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.Players.Should().HaveCount(2);
        var bobRow = snap.Players.Single(p => p.PlayerId == f.Bob.Id);
        bobRow.OwnedProvinceCount.Should().Be(1, "Bob owns B");
        bobRow.NationName.Should().Be(f.Bob.NationName);
        // SnapshotPlayerSummary has no resource field — verified at compile time.
    }

    [Fact]
    public void Adjacent_province_ids_are_undirected_per_lookup()
    {
        var f = NewFixture();
        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        var a = snap.Provinces.Single(p => p.Id == f.ProvinceA.Id);
        a.AdjacentProvinceIds.Should().Contain(f.ProvinceB.Id,
            "the adjacency lookup must work in both directions regardless of A<B PK invariant");
    }

    [Fact]
    public void Throws_when_calling_player_isnt_in_the_world()
    {
        var f = NewFixture();
        var act = () => new SnapshotService().Build(f.World, f.Adjacencies, f.Units, Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>();
    }

    // ---- Phase 4b2: GPS Constellation wonder ----

    [Fact]
    public void GpsConstellation_makes_non_adjacent_enemy_province_visible()
    {
        var f = NewFixture();
        // Without GPS, ProvinceC (isolated, no adjacency) is invisible.
        var baseline = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);
        baseline.Provinces.Single(p => p.Id == f.ProvinceC.Id).Visible.Should().BeFalse();

        // Add GPS Constellation building to Alice's owned ProvinceA.
        f.ProvinceA.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(), ProvinceId = f.ProvinceA.Id, Province = f.ProvinceA,
            Type = BuildingType.GpsConstellation, Level = 1,
        });

        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.Provinces.Single(p => p.Id == f.ProvinceC.Id).Visible.Should().BeTrue();
        snap.Provinces.Single(p => p.Id == f.ProvinceB.Id).Visible.Should().BeTrue();
    }

    [Fact]
    public void GpsConstellation_reveals_enemy_submarine_without_my_asw_present()
    {
        var f = NewFixture();
        // Bob's submarine in ProvinceB (visible province via adjacency).
        f.Units.Add(new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = f.World.Id, GameWorld = f.World,
            OwnerPlayerId = f.Bob.Id, OwnerPlayer = f.Bob,
            LocationProvinceId = f.ProvinceB.Id, LocationProvince = f.ProvinceB,
            Type = UnitType.Submarine, Domain = UnitDomain.Naval,
            Strength = 800, Morale = 100,
        });

        // Without GPS sub stays hidden (no ASW).
        var baseline = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);
        baseline.VisibleEnemyUnits.Should().NotContain(u => u.Type == nameof(UnitType.Submarine));

        // Grant Alice GPS.
        f.ProvinceA.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(), ProvinceId = f.ProvinceA.Id, Province = f.ProvinceA,
            Type = BuildingType.GpsConstellation, Level = 1,
        });

        var snap = new SnapshotService().Build(f.World, f.Adjacencies, f.Units, f.Alice.Id);

        snap.VisibleEnemyUnits.Should().Contain(u =>
            u.Type == nameof(UnitType.Submarine) && u.OwnerPlayerId == f.Bob.Id);
    }

    // ---------- fixture ----------

    private sealed class Fixture
    {
        public required GameWorld World { get; init; }
        public required Player Alice { get; init; }
        public required Player Bob { get; init; }
        public required Province ProvinceA { get; init; }
        public required Province ProvinceB { get; init; }
        public required Province ProvinceC { get; init; }
        public required List<ProvinceAdjacency> Adjacencies { get; init; }
        public required List<Unit> Units { get; init; }
    }

    private static Fixture NewFixture()
    {
        var world = new GameWorld
        {
            Id = Guid.NewGuid(),
            Name = "FogTest",
            Status = GameWorldStatus.Active,
            CurrentTick = 7,
            TickIntervalSeconds = 60,
            CreatedAt = DateTime.UtcNow,
            NextTickDueUtc = DateTime.UtcNow.AddSeconds(60),
            MapSeed = 1,
            RngState = 1,
            RowVersion = Array.Empty<byte>(),
        };

        var alice = new Player
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            IsAi = false,
            NationName = "Alice",
            FlagPrimaryHex = "#ff0000",
            FlagSecondaryHex = "#ffffff",
            IsAlive = true,
            Money = 5_000,
            Oil = 1_000,
            Steel = 1_000,
            Electronics = 500,
            Food = 1_000,
            Manpower = 2_000,
        };
        var bob = new Player
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            IsAi = true,
            AiPersonality = AiPersonality.Hawk,
            NationName = "Bob",
            FlagPrimaryHex = "#0000ff",
            FlagSecondaryHex = "#000000",
            IsAlive = true,
        };
        world.Players.Add(alice);
        world.Players.Add(bob);

        // A: Alice's capital (owned). B: Bob's capital (adjacent to A).
        // C: distant neutral (not adjacent to anything Alice owns).
        var pA = new Province
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000000a"),
            GameWorldId = world.Id, GameWorld = world,
            Name = "A", Type = ProvinceType.Capital, IsCoastal = true,
            CenterX = 1, CenterY = 1, MoraleLevel = 80,
            OwnerPlayerId = alice.Id, OwnerPlayer = alice,
        };
        var pB = new Province
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000000b"),
            GameWorldId = world.Id, GameWorld = world,
            Name = "B", Type = ProvinceType.Industrial, IsCoastal = false,
            CenterX = 2, CenterY = 2, MoraleLevel = 70,
            OwnerPlayerId = bob.Id, OwnerPlayer = bob,
        };
        var pC = new Province
        {
            Id = Guid.Parse("00000000-0000-0000-0000-00000000000c"),
            GameWorldId = world.Id, GameWorld = world,
            Name = "C", Type = ProvinceType.Resource, IsCoastal = false,
            CenterX = 9, CenterY = 9, MoraleLevel = 100,
            OwnerPlayerId = null,
        };
        world.Provinces.Add(pA);
        world.Provinces.Add(pB);
        world.Provinces.Add(pC);
        alice.OwnedProvinces.Add(pA);
        bob.OwnedProvinces.Add(pB);

        // Buildings: one on each owned province so visibility-mask tests have content.
        pA.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(), ProvinceId = pA.Id, Province = pA,
            Type = BuildingType.RecruitmentCenter, Level = 1,
        });
        pB.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(), ProvinceId = pB.Id, Province = pB,
            Type = BuildingType.SteelMill, Level = 1,
        });

        // Adjacency A↔B only. C is isolated.
        // Enforce A<B invariant by hand for this hand-built fixture.
        var (a, b) = pA.Id.CompareTo(pB.Id) < 0 ? (pA.Id, pB.Id) : (pB.Id, pA.Id);
        var adjacencies = new List<ProvinceAdjacency>
        {
            new() { ProvinceAId = a, ProvinceBId = b, TerrainCost = 1.0f, IsSeaCrossing = false },
        };

        // Units: Alice has 1× MechInf 1000 in A. Bob has 1× AA 500 in B.
        var units = new List<Unit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                GameWorldId = world.Id, GameWorld = world,
                OwnerPlayerId = alice.Id, OwnerPlayer = alice,
                LocationProvinceId = pA.Id, LocationProvince = pA,
                Type = UnitType.MechInfantry, Domain = UnitDomain.Ground,
                Strength = 1_000, Morale = 90, Experience = 3,
            },
            new()
            {
                Id = Guid.NewGuid(),
                GameWorldId = world.Id, GameWorld = world,
                OwnerPlayerId = bob.Id, OwnerPlayer = bob,
                LocationProvinceId = pB.Id, LocationProvince = pB,
                Type = UnitType.AABattery, Domain = UnitDomain.Ground,
                Strength = 500, Morale = 100, Experience = 0,
            },
        };

        return new Fixture
        {
            World = world,
            Alice = alice,
            Bob = bob,
            ProvinceA = pA,
            ProvinceB = pB,
            ProvinceC = pC,
            Adjacencies = adjacencies,
            Units = units,
        };
    }
}

internal static class SnapshotMyUnitAssertions
{
    /// <summary>Tiny helper to express "this unit is at province X" without dragging FluentAssertions extension noise.</summary>
    public static void OwnerProvinceId(this StarsAndSteel.Core.Snapshots.SnapshotMyUnit u, Guid provinceId)
    {
        if (u.LocationProvinceId != provinceId)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected unit {u.Id} at province {provinceId} but was at {u.LocationProvinceId}.");
        }
    }
}
