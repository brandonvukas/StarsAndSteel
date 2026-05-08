using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using StarsAndSteel.Api.Auth.Dtos;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// Smoke tests for /api/auth/{register,login,me,logout}. Uses a real SQL Server
/// (via Testcontainers) so Identity, EF migrations, and the auth pipeline all
/// exercise their real code paths.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AuthEndpointsTests : IClassFixture<MsSqlContainerFixture>
{
    private readonly StarsAndSteelWebAppFactory _factory;

    public AuthEndpointsTests(MsSqlContainerFixture sql)
    {
        _factory = new StarsAndSteelWebAppFactory(sql.ConnectionString);
    }

    [DockerFact]
    public async Task Register_then_login_then_me_round_trips()
    {
        // Each test invents a unique identity to keep parallel runs from colliding.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"player-{unique}@example.com";
        var displayName = $"Player {unique}";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new()
        {
            // We need cookies to round-trip so /me works after /login.
            HandleCookies = true,
        });

        // 1. Register
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            Email: email,
            DisplayName: displayName,
            Password: password));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Login — should set the cookie AND return a JWT
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            EmailOrDisplayName: email,
            Password: password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.DisplayName.Should().Be(displayName);
        auth.Email.Should().Be(email);
        auth.AccessTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        // 3. /me using the cookie
        var meResponse = await client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await meResponse.Content.ReadFromJsonAsync<MeResponse>();
        me.Should().NotBeNull();
        me!.DisplayName.Should().Be(displayName);
        me.Email.Should().Be(email);
        me.UserId.Should().Be(auth.UserId);

        // 4. Logout clears the cookie
        var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var meAfter = await client.GetAsync("/api/auth/me");
        meAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task Login_with_bad_password_returns_401()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"badpw-{unique}@example.com";
        var displayName = $"BadPw {unique}";
        var goodPassword = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, displayName, goodPassword));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            EmailOrDisplayName: email,
            Password: "wrong-password-1!"));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task Me_without_login_returns_401()
    {
        var client = _factory.CreateClient(new() { HandleCookies = true });
        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task Register_with_duplicate_email_returns_409()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"dup-{unique}@example.com";
        var password = "Sup3rSafe!Pa55";

        var client = _factory.CreateClient(new() { HandleCookies = true });

        var first = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            email, $"First {unique}", password));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            email, $"Second {unique}", password));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
