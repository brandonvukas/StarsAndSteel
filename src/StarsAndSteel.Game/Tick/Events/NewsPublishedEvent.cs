using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Emitted by <see cref="Steps.NewsStep"/> for each cable-news headline generated this
/// tick. Mirrors the persisted <see cref="Core.Entities.NewsItem"/> shape so the wire
/// DTO is a 1:1 projection. <see cref="NewsItemId"/> is the Guid the row got assigned
/// in-memory (the runner inserts it on SaveChanges).
/// </summary>
public sealed record NewsPublishedEvent(
    int Tick,
    Guid NewsItemId,
    string Headline,
    string Body,
    NewsSeverity Severity,
    NewsCategory Category,
    Guid? RelatedPlayerId
) : TickEvent(Tick);
