using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Api.Diplomacy.Dtos;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Snapshots;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Phase 4e: integration smoke for the new <c>POST /diplomacy/sanction</c> and
/// <c>/lift-sanction</c> endpoints. Exercises the full controller → service →
/// EF persistence path against a real Testcontainers SQL instance, and verifies
/// the canonical-pair flag round-trips through <c>GET /diplomacy</c>.
/// <para/>
/// Skipped when Docker isn't available (see <see cref="DockerFactAttribute"/>).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DiplomacySanctionEndpointsTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public DiplomacySanctionEndpointsTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Sanction_then_LiftSanction_round_trips_through_GET_state()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();

        // Pick the auto-seated AI opponent as our target.
        var target = snap.Players.Single(p => p.PlayerId != snap.Me.PlayerId);

        // POST /sanction
        var sanctionResp = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/diplomacy/sanction",
            new SanctionRequest(target.PlayerId));
        sanctionResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sanctioned = (await sanctionResp.Content.ReadFromJsonAsync<SanctionActionAccepted>())!;

        // The flag is reported relative to the canonical (PartyA<PartyB) pair.
        var callerIsA = snap.Me.PlayerId.CompareTo(target.PlayerId) <= 0;
        if (callerIsA)
        {
            sanctioned.IsSanctioningAtoB.Should().BeTrue();
            sanctioned.IsSanctioningBtoA.Should().BeFalse();
        }
        else
        {
            sanctioned.IsSanctioningAtoB.Should().BeFalse();
            sanctioned.IsSanctioningBtoA.Should().BeTrue();
        }

        // GET /diplomacy must reflect the same pair flag.
        var stateResp = await client.GetAsync($"/api/worlds/{summary.Id}/diplomacy");
        stateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = (await stateResp.Content.ReadFromJsonAsync<DiplomacyStateDto>())!;

        var (pairA, pairB) = snap.Me.PlayerId.CompareTo(target.PlayerId) <= 0
            ? (snap.Me.PlayerId, target.PlayerId)
            : (target.PlayerId, snap.Me.PlayerId);
        var pair = state.Relations.Single(r => r.PartyAPlayerId == pairA && r.PartyBPlayerId == pairB);
        var callerIsAFlag = callerIsA ? pair.IsSanctioningAtoB : pair.IsSanctioningBtoA;
        callerIsAFlag.Should().BeTrue("caller just sanctioned the target");

        // Re-sanction must be rejected with 409 AlreadySanctioning.
        var dupResp = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/diplomacy/sanction",
            new SanctionRequest(target.PlayerId));
        dupResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // POST /lift-sanction clears the flag.
        var liftResp = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/diplomacy/lift-sanction",
            new SanctionRequest(target.PlayerId));
        liftResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var lifted = (await liftResp.Content.ReadFromJsonAsync<SanctionActionAccepted>())!;
        lifted.IsSanctioningAtoB.Should().BeFalse();
        lifted.IsSanctioningBtoA.Should().BeFalse();

        // Re-lift must be rejected with 409 NotCurrentlySanctioning.
        var dupLiftResp = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/diplomacy/lift-sanction",
            new SanctionRequest(target.PlayerId));
        dupLiftResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [DockerFact]
    public async Task Sanction_self_returns_400()
    {
        var (client, summary, snap) = await CreateAndJoinAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/worlds/{summary.Id}/diplomacy/sanction",
            new SanctionRequest(snap.Me.PlayerId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ----

    private async Task<(HttpClient, WorldSummary, WorldSnapshot)> CreateAndJoinAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"sanction-{unique}@example.com";
        var displayName = $"Sanction {unique}";
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
