using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Api.Auth;

/// <summary>
/// Issues short-lived JWTs for SignalR. The cookie covers REST; this token is
/// what the SPA passes to the hub via the access_token query string.
/// </summary>
public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(User user);
}
