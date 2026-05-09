using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Phase 1M: end-to-end check that NewsStep persists <c>NewsItem</c> rows when the tick
/// produces headline-worthy events (here: AI vs human across a few ticks tends to produce
/// at least one Info-level UnitBuilt or Politics headline), and that the
/// <c>GET /api/worlds/{id}/news?since={tick}</c> endpoint returns them with proper
/// authorization. Skipped when Docker isn't available.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class NewsEndToEndTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public NewsEndToEndTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Tick_produces_news_rows_and_endpoint_returns_them_to_the_player()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"news-{unique}@example.com";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"News {unique}", password));
        await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password));

        // AI=1 so the Hawk produces UnitBuilt / movement / combat events that NewsStep can pick up.
        var create = await client.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"News-{unique}", MapSeed: 1234, AiOpponentCount: 1));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var summary = (await create.Content.ReadFromJsonAsync<WorldSummary>())!;

        var join = await client.PostAsJsonAsync($"/api/worlds/{summary.Id}/join",
            new JoinWorldRequest("Patriot", "#0033aa", "#ffffff"));
        join.StatusCode.Should().Be(HttpStatusCode.OK);

        // Tick a handful of times. Three ticks with AI vs adjacent human reliably produces
        // at least one ConstructionOrder completion or air strike — both surface as news.
        for (var i = 0; i < 5; i++) await ForceTickAsync(summary.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
            var newsCount = await db.NewsItems.CountAsync(n => n.GameWorldId == summary.Id);
            newsCount.Should().BeGreaterThan(0,
                "5 ticks with an AI opponent should produce at least one headline-worthy event");
        }

        // Endpoint returns rows with since=0.
        var newsResponse = await client.GetAsync($"/api/worlds/{summary.Id}/news?since=0");
        newsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await newsResponse.Content.ReadFromJsonAsync<List<NewsItemDto>>())!;
        rows.Should().NotBeEmpty();
        rows.Should().BeInAscendingOrder(n => n.Tick);
        rows.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n.Headline));

        // since filter excludes everything when set to current tick.
        var maxTick = rows.Max(n => n.Tick);
        var emptyResponse = await client.GetAsync($"/api/worlds/{summary.Id}/news?since={maxTick}");
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyRows = (await emptyResponse.Content.ReadFromJsonAsync<List<NewsItemDto>>())!;
        emptyRows.Should().BeEmpty();
    }

    [DockerFact]
    public async Task News_endpoint_forbids_callers_who_have_not_joined_the_world()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var ownerEmail = $"owner-{unique}@example.com";
        var outsiderEmail = $"outsider-{unique}@example.com";
        var password = "Sup3rSafe!Pa55";

        // Owner registers, creates and joins a world.
        var owner = _factory.CreateClient(new() { HandleCookies = true });
        await owner.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(ownerEmail, $"Owner {unique}", password));
        await owner.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ownerEmail, password));
        var create = await owner.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(Name: $"NewsAuth-{unique}", MapSeed: 99, AiOpponentCount: 0));
        var summary = (await create.Content.ReadFromJsonAsync<WorldSummary>())!;
        await owner.PostAsJsonAsync($"/api/worlds/{summary.Id}/join",
            new JoinWorldRequest("Patriot", "#0033aa", "#ffffff"));

        // Outsider registers + logs in but never joins.
        var outsider = _factory.CreateClient(new() { HandleCookies = true });
        await outsider.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(outsiderEmail, $"Outsider {unique}", password));
        await outsider.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(outsiderEmail, password));

        var forbidden = await outsider.GetAsync($"/api/worlds/{summary.Id}/news?since=0");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sanity: nonexistent world is 404 for an authenticated caller.
        var notFound = await owner.GetAsync($"/api/worlds/{Guid.NewGuid()}/news?since=0");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
