using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Seeding;
using StarsAndSteel.Data.Seeding;

namespace StarsAndSteel.Tests.Seeding;

/// <summary>
/// Smoke tests for <see cref="MapSeeder"/>. The real value: confirms the build pipeline copies
/// <c>shared/map-data.json</c> into the test bin directory, that the JSON shape matches the
/// seeder's expected DTOs, and that the ProvinceA &lt; ProvinceB invariant is enforced.
/// <para/>
/// When map-data.json grows from the 2-province stub to the ~80-province real-world map, expand
/// these tests to cover representative samples (USA = Capital with high resources, etc.).
/// </summary>
public class MapSeederTests
{
    [Fact]
    public void Load_ReadsStubProvincesFromSharedJson()
    {
        var data = MapSeeder.Load();

        data.Provinces.Should().HaveCount(2, "the Phase-0 stub map has USA and Canada");
        data.Adjacencies.Should().HaveCount(1, "the stub has a single USA-Canada border");

        var usa = data.Provinces.Single(p => p.Name == "United States");
        usa.Type.Should().Be(ProvinceType.Capital);
        usa.IsCoastal.Should().BeTrue();
        usa.MoneyPerTick.Should().Be(100);
    }

    [Fact]
    public void Load_PutsSmallerGuidInProvinceA()
    {
        // The composite-PK invariant from docs/03-DATABASE-SCHEMA.md: ProvinceAId < ProvinceBId.
        // Adjacency lookups depend on this; the seeder is responsible for normalizing.
        var data = MapSeeder.Load();

        foreach (var edge in data.Adjacencies)
        {
            edge.ProvinceAId.CompareTo(edge.ProvinceBId)
                .Should().BeLessThan(0,
                    "every adjacency row must satisfy ProvinceAId < ProvinceBId");
        }
    }

    [Fact]
    public void Load_ProducesDeterministicGuidsAcrossCalls()
    {
        // Re-running Migration 3 on a fresh DB must produce the same PKs every time. We're not
        // running the migration here, but the underlying Guid derivation must be stable.
        var first = MapSeeder.Load();
        var second = MapSeeder.Load();

        first.Provinces.Select(p => p.Id)
            .Should().Equal(second.Provinces.Select(p => p.Id));
    }
}
