using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Api.Auth;

/// <summary>
/// HMAC-SHA256-signed JWTs. Claims are limited to identity (sub, name, email).
/// We deliberately do NOT embed game state — fog of war, ownership, etc. all
/// resolve at the server on every request. See docs/10 §"Server authority".
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        // Base64-decoded key is fine for HMAC-SHA256 (must be >= 32 bytes).
        // We accept either base64 or raw text; raw is hashed once for length safety.
        var keyBytes = TryDecodeBase64(_options.Key, out var decoded)
            ? decoded
            : Encoding.UTF8.GetBytes(_options.Key);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must decode to at least 32 bytes (HS256 minimum). " +
                "Set a longer value via user-secrets or STARSANDSTEEL_JWT_KEY.");
        }

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            // ClaimTypes.NameIdentifier is what ASP.NET wires up to User.Identity by default.
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return (encoded, expiresAt);
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}
