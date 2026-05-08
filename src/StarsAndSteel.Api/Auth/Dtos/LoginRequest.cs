namespace StarsAndSteel.Api.Auth.Dtos;

/// <summary>
/// Body of POST /api/auth/login. Accepts either email or display name in
/// <see cref="EmailOrDisplayName"/> so users aren't forced to remember which.
/// </summary>
public sealed record LoginRequest(
    string EmailOrDisplayName,
    string Password);
