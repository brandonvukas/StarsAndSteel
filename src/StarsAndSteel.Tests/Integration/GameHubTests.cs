using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Hubs;
using StarsAndSteel.Api.Hubs.Dtos;
using StarsAndSteel.Api.Orders.Dtos;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Snapshots;
using StarsAndSteel.Data;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Phase 1J end-to-end checks for the SignalR hub:
/// <list type="bullet">
///   <item>Anonymous (no JWT) connect is rejected.</item>
///   <item>Authenticated connect → JoinWorld → tick → client receives both a
///   per-event message (UnitMoved) and the terminal TickAdvanced barrier.</item>
/// </list>
/// Skipped when Docker is unavailable (no Testcontainers SQL Server).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class GameHubTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public GameHubTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Connect_without_token_is_rejected()
    {
        // No access_token query param → JwtBearer authentication fails → hub
        // negotiation returns 401 → SignalR client throws on StartAsync.
        await using var connection = new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + GameHub.Path.TrimStart('/'), opts =>
            {
                opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                opts.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var act = async () => await connection.StartAsync(TestTimeout(5));
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [DockerFact]
    public async Task Move_order_broadcasts_UnitMoved_then_TickAdvanced_to_world_group()
    {
        var (httpClient, accessToken, summary, snap) = await CreateAndJoinAsync();
        var capital = snap.Provinces.Single(p => p.OwnerPlayerId == snap.Me.PlayerId);
        var adjacent = snap.Provinces.Single(p => p.Id != capital.Id);
        var unit = snap.MyUnits.First(u => u.Domain == "Ground");

        await using var connection = BuildConnection(accessToken);
        var unitMovedTcs = new TaskCompletionSource<TickEventDtos.UnitMoved>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tickAdvancedTcs = new TaskCompletionSource<TickEventDtos.TickAdvanced>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<TickEventDtos.UnitMoved>(TickEventNames.UnitMoved, evt =>
        {
            // First UnitMoved for our unit is the one we want.
            if (evt.UnitId == unit.Id) unitMovedTcs.TrySetResult(evt);
        });
        connection.On<TickEventDtos.TickAdvanced>(TickEventNames.TickAdvanced, evt =>
            tickAdvancedTcs.TrySetResult(evt));

        await connection.StartAsync(TestTimeout(10));
        await connection.InvokeAsync(nameof(GameHub.JoinWorld), summary.Id, TestTimeout(5));

        // Submit the move order AFTER subscribing so we can't miss the broadcast.
        var move = await httpClient.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/orders/move",
            new MoveOrderRequest(unit.Id, adjacent.Id));
        move.EnsureSuccessStatusCode();

        await ForceTickAsync(summary.Id);

        var moved = await unitMovedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        moved.UnitId.Should().Be(unit.Id);
        moved.OwnerPlayerId.Should().Be(snap.Me.PlayerId);
        moved.FromProvinceId.Should().Be(capital.Id);
        moved.ToProvinceId.Should().Be(adjacent.Id);

        var advanced = await tickAdvancedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        advanced.Tick.Should().Be(moved.Tick);
        advanced.EventCount.Should().BeGreaterThan(0);
    }

    // ---- helpers --------------------------------------------------------

    private HubConnection BuildConnection(string accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + GameHub.Path.TrimStart('/'), opts =>
            {
                // Route through TestServer's in-memory handler so we don't
                // need a real Kestrel socket. LongPolling is the only
                // transport TestServer's HTTP-only handler supports.
                opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                opts.Transports = HttpTransportType.LongPolling;
                opts.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

    private static CancellationToken TestTimeout(int seconds) =>
        new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    private async Task ForceTickAsync(Guid worldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
        var world = await db.GameWorlds.SingleAsync(w => w.Id == worldId);
        world.NextTickDueUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var runner = scope.ServiceProvider.GetRequiredService<TickRunner>();
        var result = await runner.RunAsync(worldId, default);
        result.Should().NotBeNull();
    }

    private async Task<(HttpClient Client, string AccessToken, WorldSummary Summary, WorldSnapshot Snap)>
        CreateAndJoinAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"hub-{unique}@example.com";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"Hub {unique}", password));
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password));
        var auth = (await loginResp.Content.ReadFromJsonAsync<AuthResponse>())!;

        var create = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"Hub-{unique}", MapSeed: 42));
        var summary = (await create.Content.ReadFromJsonAsync<WorldSummary>())!;

        await client.PostAsJsonAsync($"/api/worlds/{summary.Id}/join",
            new JoinWorldRequest("Alice", "#ff0000", "#ffffff"));

        var snapResp = await client.GetAsync($"/api/worlds/{summary.Id}/snapshot");
        var snap = (await snapResp.Content.ReadFromJsonAsync<WorldSnapshot>())!;

        return (client, auth.AccessToken, summary, snap);
    }
}
