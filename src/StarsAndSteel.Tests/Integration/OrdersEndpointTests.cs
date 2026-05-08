using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.Orders.Dtos;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Snapshots;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// End-to-end order endpoint tests. Hits the real Api against Testcontainers SQL.
/// Skipped when Docker isn't available.
/// <para/>
/// The stub map (<c>shared/map-data.json</c>) has only 1 candidate-capital province
/// (United States) with neighbour Canada (Resource type, neutral). That's enough
/// for move/build tests; combat-vs-other-player needs a richer map and is deferred.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class OrdersEndpointTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public OrdersEndpointTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Move_to_adjacent_province_is_accepted_and_stamped_for_next_tick()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();

        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);
        var adjacent = snap.Provinces.Single(p => p.Id != capital.Id);
        var unit = snap.MyUnits.First();

        var response = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/move",
            new MoveOrderRequest(unit.Id, adjacent.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await response.Content.ReadFromJsonAsync<UnitOrderAccepted>();
        accepted.Should().NotBeNull();
        accepted!.OrderType.Should().Be("Move");
        accepted.UnitId.Should().Be(unit.Id);
        accepted.TargetProvinceId.Should().Be(adjacent.Id);
        accepted.IssuedAtTick.Should().Be(1, "world.CurrentTick is 0 just after creation; orders stamp at +1");
    }

    [DockerFact]
    public async Task Move_to_non_adjacent_province_returns_400()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);
        var unit = snap.MyUnits.First();

        var response = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/move",
            new MoveOrderRequest(unit.Id, capital.Id));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [DockerFact]
    public async Task Move_unit_owned_by_other_player_returns_403()
    {
        var (aliceClient, summary, aliceSnap) = await CreateAndJoinAsync();
        var aliceUnit = aliceSnap.MyUnits.First();
        var aliceCapital = aliceSnap.Provinces.Single(p => p.OwnerPlayerId == aliceSnap.Me.PlayerId);

        // Bob registers + logs in but won't be in this world (only 1 capital available).
        // Bob can't join, but he can attempt to issue orders — should get 403 (not in world).
        var unique = Guid.NewGuid().ToString("N")[..8];
        var bobClient = _factory.CreateClient(new() { HandleCookies = true });
        var pwd = "Sup3rSafe!Pa55";
        await bobClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"bob-{unique}@example.com", $"Bob {unique}", pwd));
        await bobClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"bob-{unique}@example.com", pwd));

        var response = await bobClient.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/move",
            new MoveOrderRequest(aliceUnit.Id, aliceCapital.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Bob isn't in this world; we don't leak ownership info via 4xx variance");
    }

    [DockerFact]
    public async Task BuildBuilding_steel_mill_at_capital_is_accepted_and_debits_resources()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);

        var moneyBefore = snap.Me.Resources.Money;
        var steelBefore = snap.Me.Resources.Steel;

        var response = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/build-building",
            new BuildBuildingOrderRequest(capital.Id, BuildingType.SteelMill.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await response.Content.ReadFromJsonAsync<ConstructionOrderAccepted>();
        accepted.Should().NotBeNull();
        accepted!.OrderType.Should().Be("BuildBuilding");
        accepted.BuildingType.Should().Be("SteelMill");
        accepted.TicksRemaining.Should().Be(12);
        accepted.IssuedAtTick.Should().Be(1);

        // Pull a fresh snapshot to confirm the player was debited.
        var snap2 = await GetSnapshotAsync(client, summary.Id);
        snap2.Me.Resources.Money.Should().Be(moneyBefore - 1500);
        snap2.Me.Resources.Steel.Should().Be(steelBefore - 100);
    }

    [DockerFact]
    public async Task BuildUnit_mech_infantry_at_capital_is_accepted_with_proper_ticks()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);

        var response = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/build-unit",
            new BuildUnitOrderRequest(
                capital.Id,
                UnitType.MechInfantry.ToString(),
                Quantity: 1000));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await response.Content.ReadFromJsonAsync<ConstructionOrderAccepted>();
        accepted!.UnitType.Should().Be("MechInfantry");
        accepted.Quantity.Should().Be(1000);
        accepted.TicksRemaining.Should().Be(5);
    }

    [DockerFact]
    public async Task BuildUnit_with_insufficient_resources_returns_409()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);

        // Stealth bombers cost $3500 / 800 steel / 1200 electronics / 500 oil per 1000.
        // Starter pool: $5000 / 1000 steel / 500 electronics — electronics is the bottleneck.
        var response = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/build-unit",
            new BuildUnitOrderRequest(
                capital.Id,
                UnitType.StealthBomber.ToString(),
                Quantity: 1000));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [DockerFact]
    public async Task BuildBuilding_unknown_building_type_returns_400_from_validator()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);

        var response = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/build-building",
            new BuildBuildingOrderRequest(capital.Id, "NotARealBuilding"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [DockerFact]
    public async Task Move_without_login_returns_401()
    {
        var anon = _factory.CreateClient(new() { HandleCookies = true });
        var response = await anon.PostAsJsonAsync(
            $"/api/worlds/{Guid.NewGuid()}/orders/move",
            new MoveOrderRequest(Guid.NewGuid(), Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    private async Task<(HttpClient, WorldSummary, WorldSnapshot)> CreateAndJoinAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"orders-{unique}@example.com";
        var displayName = $"Orders {unique}";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, displayName, password));
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password));

        var create = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"World-{unique}", MapSeed: 11));
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
