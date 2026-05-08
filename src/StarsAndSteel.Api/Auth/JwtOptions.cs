namespace StarsAndSteel.Api.Auth;

/// <summary>
/// Strongly typed binding for the "Jwt" config section. <see cref="Key"/> is the
/// HMAC signing secret (base64). Bound from user-secrets in dev, from the
/// STARSANDSTEEL_JWT_KEY env var in production (mapped via standard config).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = default!;
    public string Audience { get; init; } = default!;
    public string Key { get; init; } = default!;
    public int AccessTokenLifetimeMinutes { get; init; } = 15;
}
