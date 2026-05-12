using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Chat.Dtos;

/// <summary>
/// One chat message as exposed to clients. <see cref="ToPlayerId"/> is set only
/// when <see cref="Scope"/> = <see cref="ChatScope.Direct"/>.
/// </summary>
public sealed record ChatMessageDto(
    Guid Id,
    Guid FromPlayerId,
    Guid? ToPlayerId,
    ChatScope Scope,
    string Body,
    DateTime SentAtUtc);

/// <summary>POST body for /api/worlds/{id}/chat/send.</summary>
public sealed record SendChatMessageRequest(
    ChatScope Scope,
    Guid? ToPlayerId,
    string Body);

/// <summary>Response from /api/worlds/{id}/chat/send.</summary>
public sealed record SendChatMessageResponse(Guid MessageId, DateTime SentAtUtc);
