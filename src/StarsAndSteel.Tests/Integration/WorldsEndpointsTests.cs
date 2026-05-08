using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// End-to-end through the real Api + real SQL Server: register, login, create a
/// world, join it, then drive the tick service forward and verify the player's
/// resources increased.
/// <para/>
/// Tick-cadence note: <c>GameWorld.TickIntervalSeconds</c> defaults to 60 and the
/// background <c>GameTickService</c> polls every 1s. Rather than wait a real
/// minute, the test reaches into the DbContext to back-date <c>NextTickDueUtc</c>
/// so the next poll iteration ticks the world immediately.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class WorldsEndpointsTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public WorldsEndpointsTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Create_then_join_then_tick_produces_resources()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"worlds-{unique}@example.com";
        var displayName = $"Player {unique}";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });

        // 1. Register + login
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, displayName, password)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Create the world
        var createResponse = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"World-{unique}", MapSeed: 12345));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var summary = await createResponse.Content.ReadFromJsonAsync<WorldSummary>();
        summary.Should().NotBeNull();
        summary!.Status.Should().Be(GameWorldStatus.Lobby.ToString());
        summary.MapSeed.Should().Be(12345);
        summary.PlayerCount.Should().Be(0);
        summary.ProvinceCount.Should().Be(2, "the stub map ships with 2 provinces");

        // 3. Join — flips world to Active and grants the starter package.
        var joinResponse = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/join",
            new JoinWorldRequest(NationName: "Alice", FlagPrimaryHex: "#ff0000", FlagSecondaryHex: "#ffffff"));
        joinResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinWorldResponse>();
        joinResult.Should().NotBeNull();
        joinResult!.Money.Should().Be(5_000);
        joinResult.CapitalProvinceName.Should().Be("United States");

        // 4. Force the world to be due RIGHT NOW so the background poller picks
        // it up on its next 1-second iteration. We bypass the API for this —
        // there is no admin endpoint, and we don't want the test to wait a minute.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
            await db.GameWorlds
                .Where(w => w.Id == summary.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.NextTickDueUtc,
                    DateTime.UtcNow.AddSeconds(-5)));
        }

        // 5. Wait for at least one tick to land. Poll loop is 1s; budget 8s.
        var deadline = DateTime.UtcNow.AddSeconds(8);
        long? observedMoney = null;
        int? observedTick = null;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();

            var snapshot = await db.GameWorlds
                .AsNoTracking()
                .Where(w => w.Id == summary.Id)
                .Select(w => new
                {
                    w.CurrentTick,
                    PlayerMoney = w.Players.Select(p => p.Money).FirstOrDefault(),
                })
                .FirstAsync();

            if (snapshot.CurrentTick > 0)
            {
                observedMoney = snapshot.PlayerMoney;
                observedTick = snapshot.CurrentTick;
                break;
            }
        }

        observedTick.Should().NotBeNull("the tick service must have advanced the world within 8 seconds");
        // United States base money/tick = 100; FinancialDistrict L1 = +20%.
        // Expect 5000 starter + at least one tick of 120 = 5120.
        observedMoney.Should().BeGreaterThanOrEqualTo(5_000 + 120,
            "after ≥1 tick the player should have at least one production cycle of money");
    }

    [DockerFact]
    public async Task Create_world_requires_authentication()
    {
        var client = _factory.CreateClient(new() { HandleCookies = true });

        var response = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: "Anon", MapSeed: 1));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task Joining_same_world_twice_returns_409()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"dup-join-{unique}@example.com";
        var displayName = $"DupJoin {unique}";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });

        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, displayName, password));
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password));

        var create = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"World-{unique}", MapSeed: 1));
        var summary = await create.Content.ReadFromJsonAsync<WorldSummary>();

        var first = await client.PostAsJsonAsync($"/api/worlds/{summary!.Id}/join",
            new JoinWorldRequest("Alice", "#ff0000", "#ffffff"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync($"/api/worlds/{summary.Id}/join",
            new JoinWorldRequest("Alice", "#ff0000", "#ffffff"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
