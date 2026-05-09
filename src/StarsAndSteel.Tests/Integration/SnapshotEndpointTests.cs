using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Snapshots;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// End-to-end snapshot tests. Hits the real Api against Testcontainers SQL.
/// Skipped when Docker isn't available (see <see cref="DockerFactAttribute"/>).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class SnapshotEndpointTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public SnapshotEndpointTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Snapshot_after_join_returns_starter_state_with_visible_capital()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"snap-{unique}@example.com";
        var displayName = $"Snap {unique}";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });

        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, displayName, password)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var createResponse = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"World-{unique}", MapSeed: 7));
        var summary = await createResponse.Content.ReadFromJsonAsync<WorldSummary>();
        summary.Should().NotBeNull();

        var joinResponse = await client.PostAsJsonAsync($"/api/worlds/{summary!.Id}/join",
            new JoinWorldRequest("Alice", "#ff0000", "#ffffff"));
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshotResponse = await client.GetAsync($"/api/worlds/{summary.Id}/snapshot");
        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var snap = await snapshotResponse.Content.ReadFromJsonAsync<WorldSnapshot>();
        snap.Should().NotBeNull();

        snap!.WorldId.Should().Be(summary.Id);
        snap.Status.Should().Be(GameWorldStatus.Active.ToString());

        snap.Me.NationName.Should().Be("Alice");
        snap.Me.Resources.Money.Should().Be(5_000);
        snap.Me.Resources.Manpower.Should().Be(2_000);

        // The capital is visible and shows full detail.
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);
        capital.Visible.Should().BeTrue();
        capital.Buildings.Should().HaveCount(4, "RC + MB + AB + FD per docs/03");
        capital.GarrisonStrength.Should().Be(2_500, "2x MechInf 1000 + 1x AA 500 = 2500");

        // Every province adjacent to the capital is visible (fog-of-war rule:
        // own + adjacent). The starting province on the real-world map has at
        // least one neighbour (graph is fully connected and capital-typed
        // provinces are land-locked or coastal — never isolated).
        capital.AdjacentProvinceIds.Should().NotBeEmpty(
            "every starting province on the real-world map has at least one neighbour");
        foreach (var adjId in capital.AdjacentProvinceIds)
        {
            var adj = snap.Provinces.Single(p => p.Id == adjId);
            adj.Visible.Should().BeTrue("provinces adjacent to the player's capital are visible");
            adj.OwnerPlayerId.Should().BeNull("only the spawning player has claimed land in this single-join world");
        }

        // My units full detail: 3 stacks total.
        snap.MyUnits.Should().HaveCount(3);
        snap.MyUnits.Should().OnlyContain(u => u.LocationProvinceId == capital.Id);
        snap.MyUnits.Should().OnlyContain(u => !u.IsInTransit);

        // No enemies in this single-player smoke world.
        snap.VisibleEnemyUnits.Should().BeEmpty();
    }

    [DockerFact]
    public async Task Snapshot_for_non_member_returns_403()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var password = "Sup3rSafe!Pa55";

        // Alice creates and joins a world.
        var aliceClient = _factory.CreateClient(new() { HandleCookies = true });
        await aliceClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"alice-{unique}@example.com", $"Alice {unique}", password));
        await aliceClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"alice-{unique}@example.com", password));

        var create = await aliceClient.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest($"World-{unique}", MapSeed: 1));
        var summary = await create.Content.ReadFromJsonAsync<WorldSummary>();
        await aliceClient.PostAsJsonAsync($"/api/worlds/{summary!.Id}/join",
            new JoinWorldRequest("Alice", "#ff0000", "#ffffff"));

        // Bob registers + logs in but never joins.
        var bobClient = _factory.CreateClient(new() { HandleCookies = true });
        await bobClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"bob-{unique}@example.com", $"Bob {unique}", password));
        await bobClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"bob-{unique}@example.com", password));

        var bobSnap = await bobClient.GetAsync($"/api/worlds/{summary.Id}/snapshot");
        bobSnap.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "we don't leak world contents to users who haven't joined");
    }

    [DockerFact]
    public async Task Snapshot_for_unknown_world_returns_404()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"nf-{unique}@example.com", $"NF {unique}", password));
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"nf-{unique}@example.com", password));

        var response = await client.GetAsync($"/api/worlds/{Guid.NewGuid()}/snapshot");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
