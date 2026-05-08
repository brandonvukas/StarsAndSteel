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

    public ICollection<Player> Players { get; set; } = new List<Player>();
}
