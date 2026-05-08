using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Generated event surfaced in the cable-news ticker. Created by tick steps (combat, diplomacy,
/// etc.) using deterministic templates. <see cref="RelatedPlayerId"/> is set when the event
/// concerns a specific nation so the client can color-code it.
/// </summary>
public class NewsItem
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public int Tick { get; set; }
    public string Headline { get; set; } = default!;
    public string Body { get; set; } = default!;

    public NewsSeverity Severity { get; set; }
    public NewsCategory Category { get; set; }

    public Guid? RelatedPlayerId { get; set; }
    public Player? RelatedPlayer { get; set; }
}
