using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Snapshots;
using StarsAndSteel.Data;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Phase 1L: end-to-end check that AI=1 worlds seat a Hawk player at creation, stay in
/// Lobby until a human joins, and that the AI emits orders via <c>AiTurnStep</c> once the
/// world is ticking. Skipped when Docker isn't available.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AiOpponentEndToEndTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public AiOpponentEndToEndTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task World_with_AI_opponent_stays_in_Lobby_until_human_joins_then_AI_acts()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"ai-{unique}@example.com";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"AI {unique}", password));
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password));

        // Create world with AI=1.
        var create = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"AI-{unique}", MapSeed: 42, AiOpponentCount: 1));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var summary = (await create.Content.ReadFromJsonAsync<WorldSummary>())!;

        // World should be in Lobby with the AI already seated (PlayerCount = 1).
        summary.Status.Should().Be(GameWorldStatus.Lobby.ToString());
        summary.PlayerCount.Should().Be(1, "Hawk AI is seated at world creation");

        // Verify the AI player exists in the database with personality + memory.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
            var ai = await db.Players
                .Include(p => p.AiMemory)
                .Include(p => p.OwnedProvinces)
                .SingleAsync(p => p.GameWorldId == summary.Id && p.IsAi);
            ai.AiPersonality.Should().Be(AiPersonality.Hawk);
            ai.AiMemory.Should().NotBeNull();
            ai.OwnedProvinces.Should().NotBeEmpty("PlayerSpawner must seat the AI on a province");
        }

        // Human joins — flips Lobby -> Active.
        var join = await client.PostAsJsonAsync($"/api/worlds/{summary.Id}/join",
            new JoinWorldRequest("Patriot", "#0033aa", "#ffffff"));
        join.StatusCode.Should().Be(HttpStatusCode.OK);

        // Tick a few times. The AI's adjacent enemy is the human, so AiTurnStep should
        // either issue an attack (human is adjacent and weaker per starter) OR queue a
        // recruit (resources allow). We assert "did SOMETHING": either a pending/in-progress
        // construction order or a movement/in-transit unit attributed to the AI.
        for (var i = 0; i < 3; i++) await ForceTickAsync(summary.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
            var aiId = await db.Players
                .Where(p => p.GameWorldId == summary.Id && p.IsAi)
                .Select(p => p.Id)
                .SingleAsync();

            var aiHasConstruction = await db.ConstructionOrders
                .AnyAsync(o => o.OwnerPlayerId == aiId);
            var aiHasUnitOrder = await db.UnitOrders
                .AnyAsync(o => o.Unit.OwnerPlayerId == aiId);

            (aiHasConstruction || aiHasUnitOrder).Should().BeTrue(
                "Hawk AI must issue at least one order across 3 ticks against an adjacent human");
        }
    }

    [DockerFact]
    public async Task World_with_AI_zero_seats_no_AI_player()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"noai-{unique}@example.com";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"NoAI {unique}", password));
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password));

        var create = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"NoAI-{unique}", MapSeed: 7, AiOpponentCount: 0));
        var summary = (await create.Content.ReadFromJsonAsync<WorldSummary>())!;
        summary.PlayerCount.Should().Be(0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
        var aiCount = await db.Players.CountAsync(p => p.GameWorldId == summary.Id && p.IsAi);
        aiCount.Should().Be(0);
    }

    private async Task ForceTickAsync(Guid worldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
        var world = await db.GameWorlds.SingleAsync(w => w.Id == worldId);
        world.NextTickDueUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var runner = scope.ServiceProvider.GetRequiredService<TickRunner>();
        await runner.RunAsync(worldId, default);
    }
}
