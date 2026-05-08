namespace StarsAndSteel.Api.Auth.Dtos;

/// <summary>
/// Body of POST /api/auth/register. The validator enforces lengths and Identity
/// password rules (min 8, 1 upper, 1 digit, 1 non-alphanumeric).
/// </summary>
public sealed record RegisterRequest(
    string Email,
    string DisplayName,
    string Password);
