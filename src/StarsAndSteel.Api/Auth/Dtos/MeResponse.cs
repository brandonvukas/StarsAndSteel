namespace StarsAndSteel.Api.Auth.Dtos;

/// <summary>
/// Response of GET /api/auth/me. Returns the calling user's identity. No game
/// state is included; that comes from /api/world/* endpoints.
/// <para/>
/// Phase 2L adds <see cref="QuietHoursStartUtc"/> + <see cref="QuietHoursEndUtc"/>
/// (advisory only — the client uses them to suppress non-critical hub
/// notifications during the configured window). Both serialize as ISO 8601
/// time-of-day strings (e.g., "23:00:00") via System.Text.Json's TimeOnly support.
/// </summary>
public sealed record MeResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    TimeOnly? QuietHoursStartUtc,
    TimeOnly? QuietHoursEndUtc);

/// <summary>
/// PUT /api/auth/me/quiet-hours request. Both fields are nullable: passing both
/// nulls clears the window entirely. If one is set the other must be set too —
/// enforced by <c>UpdateQuietHoursRequestValidator</c>.
/// </summary>
public sealed record UpdateQuietHoursRequest(
    TimeOnly? QuietHoursStartUtc,
    TimeOnly? QuietHoursEndUtc);

