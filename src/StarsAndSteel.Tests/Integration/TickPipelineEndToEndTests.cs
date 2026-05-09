using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Orders.Dtos;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Snapshots;
using StarsAndSteel.Data;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Phase 1I: end-to-end checks that submitted orders actually execute on tick.
/// We bypass the BackgroundService poll loop and invoke <see cref="TickRunner"/>
/// directly so tests don't depend on wall-clock waits.
/// <para/>
/// Skipped when Docker isn't available (no Testcontainers).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TickPipelineEndToEndTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public TickPipelineEndToEndTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Move_order_advances_unit_to_target_after_one_tick()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);
        var adjacent = snap.Provinces.Single(p => p.Id != capital.Id);
        // Pick a ground unit (not AA — but all starter units are ground here; mech infantry is fine).
        var unit = snap.MyUnits.First(u => u.Domain == "Ground");

        var move = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/move",
            new MoveOrderRequest(unit.Id, adjacent.Id));
        move.StatusCode.Should().Be(HttpStatusCode.OK);

        await ForceTickAsync(summary.Id);

        var afterSnap = await GetSnapshotAsync(client, summary.Id);
        var movedUnit = afterSnap.MyUnits.Single(u => u.Id == unit.Id);
        movedUnit.LocationProvinceId.Should().Be(adjacent.Id);
    }

    [DockerFact]
    public async Task BuildBuilding_completes_after_its_tick_count()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);

        var build = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/build-building",
            new BuildBuildingOrderRequest(capital.Id, BuildingType.SteelMill.ToString()));
        build.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await build.Content.ReadFromJsonAsync<ConstructionOrderAccepted>();
        accepted!.TicksRemaining.Should().Be(12);

        // Tick the world 12 times.
        for (var i = 0; i < 12; i++) await ForceTickAsync(summary.Id);

        var afterSnap = await GetSnapshotAsync(client, summary.Id);
        var capitalAfter = afterSnap.Provinces.Single(p => p.Id == capital.Id);
        capitalAfter.Buildings.Should().Contain(b => b.Type == BuildingType.SteelMill.ToString());
    }

    [DockerFact]
    public async Task BuildUnit_completes_and_appears_in_owner_units()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);
        var beforeUnitCount = snap.MyUnits.Count;

        var build = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/build-unit",
            new BuildUnitOrderRequest(capital.Id, UnitType.MechInfantry.ToString(), Quantity: 1000));
        build.StatusCode.Should().Be(HttpStatusCode.OK);

        // MechInfantry takes 5 ticks to build.
        for (var i = 0; i < 5; i++) await ForceTickAsync(summary.Id);

        var afterSnap = await GetSnapshotAsync(client, summary.Id);
        afterSnap.MyUnits.Count.Should().BeGreaterThan(beforeUnitCount);
        afterSnap.MyUnits.Should().Contain(u =>
            u.Type == UnitType.MechInfantry.ToString() &&
            u.LocationProvinceId == capital.Id &&
            u.Strength == 1000);
    }

    /// <summary>
    /// Force a single tick to run for <paramref name="worldId"/>, bypassing the
    /// scheduler's NextTickDueUtc guard by rewinding it to the past first.
    /// </summary>
    private async Task ForceTickAsync(Guid worldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
        var world = await db.GameWorlds.SingleAsync(w => w.Id == worldId);
        world.NextTickDueUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var runner = scope.ServiceProvider.GetRequiredService<TickRunner>();
        var result = await runner.RunAsync(worldId, default);
        result.Should().NotBeNull("tick should have executed");
    }

    private async Task<(HttpClient Client, WorldSummary Summary, WorldSnapshot Snapshot)> CreateAndJoinAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"tick-{unique}@example.com";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"Tick {unique}", password));
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password));

        var create = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"Tick-{unique}", MapSeed: 42));
        var summary = (await create.Content.ReadFromJsonAsync<WorldSummary>())!;

        await client.PostAsJsonAsync($"/api/worlds/{summary.Id}/join",
            new JoinWorldRequest("Alice", "#ff0000", "#ffffff"));

        var snap = await GetSnapshotAsync(client, summary.Id);
        return (client, summary, snap);
    }

    private static async Task<WorldSnapshot> GetSnapshotAsync(HttpClient client, Guid worldId)
    {
        var response = await client.GetAsync($"/api/worlds/{worldId}/snapshot");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<WorldSnapshot>())!;
    }
}
