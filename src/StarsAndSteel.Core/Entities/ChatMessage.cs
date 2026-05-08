namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Per-game player chat message. <see cref="ToPlayerId"/> null = global channel; otherwise direct.
/// </summary>
public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid FromPlayerId { get; set; }
    public Player FromPlayer { get; set; } = default!;

    /// <summary>Null = global channel; otherwise direct message.</summary>
    public Guid? ToPlayerId { get; set; }
    public Player? ToPlayer { get; set; }

    public string Body { get; set; } = default!;
    public DateTime SentAtUtc { get; set; }
}
