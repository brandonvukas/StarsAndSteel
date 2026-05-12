namespace StarsAndSteel.Core.Entities;

using StarsAndSteel.Core.Enums;

/// <summary>
/// Per-game player chat message. Visibility is governed by <see cref="Scope"/>:
/// Global broadcasts to the world; Alliance broadcasts to the sender's current allies;
/// Direct routes to <see cref="ToPlayerId"/> only. Phase 2K.
/// </summary>
public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid FromPlayerId { get; set; }
    public Player FromPlayer { get; set; } = default!;

    /// <summary>Set only when <see cref="Scope"/> = <see cref="ChatScope.Direct"/>.</summary>
    public Guid? ToPlayerId { get; set; }
    public Player? ToPlayer { get; set; }

    /// <summary>Phase 2K: visibility scope (Global / Alliance / Direct).</summary>
    public ChatScope Scope { get; set; }

    public string Body { get; set; } = default!;
    public DateTime SentAtUtc { get; set; }
}
