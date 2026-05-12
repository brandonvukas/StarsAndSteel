using Microsoft.AspNetCore.Identity;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// The human account. Inherits <see cref="IdentityUser{TKey}"/> with Guid keys (the default
/// non-generic IdentityUser uses string PKs which we don't want).
/// One <see cref="User"/> can hold multiple <see cref="Player"/> seats across different
/// <see cref="GameWorld"/> instances.
/// </summary>
public class User : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = default!;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Phase 2L: optional quiet-hours window (UTC, inclusive start / exclusive end).
    /// Both null = no quiet hours. If <c>End &lt; Start</c> the window wraps midnight.
    /// Currently advisory — surfaced in profile UI; client-side notification gating
    /// (browser push, email digest) consumes this in a later phase.
    /// </summary>
    public TimeOnly? QuietHoursStartUtc { get; set; }
    public TimeOnly? QuietHoursEndUtc { get; set; }

    public ICollection<Player> Players { get; set; } = new List<Player>();
}
