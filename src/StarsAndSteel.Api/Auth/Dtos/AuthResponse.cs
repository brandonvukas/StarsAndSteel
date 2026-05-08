namespace StarsAndSteel.Api.Auth.Dtos;

/// <summary>
/// Response of POST /api/auth/login. The cookie is set as a Set-Cookie header on
/// the same response (the SPA uses it for REST). The <see cref="AccessToken"/>
/// is what the SPA must hand to SignalR via the access_token query string.
/// </summary>
public sealed record AuthResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt);
