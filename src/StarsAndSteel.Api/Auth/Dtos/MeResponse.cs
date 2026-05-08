namespace StarsAndSteel.Api.Auth.Dtos;

/// <summary>
/// Response of GET /api/auth/me. Returns the calling user's identity. No game
/// state is included; that comes from /api/world/* endpoints.
/// </summary>
public sealed record MeResponse(
    Guid UserId,
    string DisplayName,
    string Email);
