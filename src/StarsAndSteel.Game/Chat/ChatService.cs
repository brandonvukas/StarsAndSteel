using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Chat;

/// <summary>Why a chat send was rejected. Maps 1:1 to HTTP codes in <c>ChatController</c>.</summary>
public enum ChatRejectionReason
{
    GameEnded,            // 409 — world is no longer Active
    SelfTargeted,         // 400 — direct message to self
    RecipientNotInWorld,  // 404 — direct message recipient not a player in this world
    RecipientEliminated,  // 409 — direct message to a dead player
    BodyEmpty,            // 400 — defensive (FluentValidation should catch first)
    BodyTooLong,          // 400 — defensive
    InvalidScopePayload,  // 400 — ToPlayerId set/missing inconsistently with Scope (defensive)
}

/// <summary>
/// Outcome of a chat send. Either <see cref="Mutation"/> is the new
/// <see cref="ChatMessage"/> to insert, or <see cref="Rejection"/> describes
/// the failure. Pure: no DB writes happen here.
/// </summary>
public sealed record ChatResult(
    ChatMessage? Mutation,
    ChatRejectionReason? Rejection,
    string? RejectionMessage)
{
    public static ChatResult Accept(ChatMessage message) => new(message, null, null);
    public static ChatResult Reject(ChatRejectionReason reason, string message) =>
        new(null, reason, message);

    public bool IsAccepted => Rejection is null;
}

/// <summary>
/// Phase 2K. Pure validation + <see cref="ChatMessage"/> construction. Mirrors
/// <c>ResearchService</c> and <c>DiplomacyService</c>: takes already-loaded
/// entities, returns a mutation describing what to persist. The controller
/// owns the load → save → broadcast sequencing.
/// <para/>
/// The service does NOT compute the alliance recipient set — that's a read-time
/// concern in <c>ChatController.GetHistory</c>.
/// </summary>
public sealed class ChatService
{
    private const int MaxBodyLength = 500;

    /// <param name="sender">Sender player (must already be loaded; caller of this world).</param>
    /// <param name="scope">Visibility scope of the message.</param>
    /// <param name="recipient">
    /// Recipient player. Required iff <paramref name="scope"/> = <see cref="ChatScope.Direct"/>;
    /// must be null otherwise.
    /// </param>
    /// <param name="body">Message body (will be trimmed).</param>
    /// <param name="gameEnded">If <c>true</c>, the world is no longer Active and chat is rejected.</param>
    /// <param name="utcNow">Clock-injected timestamp.</param>
    public ChatResult Send(
        Player sender,
        ChatScope scope,
        Player? recipient,
        string body,
        bool gameEnded,
        DateTime utcNow)
    {
        if (gameEnded)
        {
            return ChatResult.Reject(
                ChatRejectionReason.GameEnded,
                "Chat is closed once the world has ended.");
        }

        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return ChatResult.Reject(
                ChatRejectionReason.BodyEmpty,
                "Message body must be non-empty after trimming.");
        }
        if (trimmed.Length > MaxBodyLength)
        {
            return ChatResult.Reject(
                ChatRejectionReason.BodyTooLong,
                $"Message body must be ≤{MaxBodyLength} characters.");
        }

        // Scope/recipient consistency. Defensive: validator should have caught these.
        if (scope == ChatScope.Direct)
        {
            if (recipient is null)
            {
                return ChatResult.Reject(
                    ChatRejectionReason.RecipientNotInWorld,
                    "Recipient is not a player in this world.");
            }
            if (recipient.Id == sender.Id)
            {
                return ChatResult.Reject(
                    ChatRejectionReason.SelfTargeted,
                    "You cannot direct-message yourself.");
            }
            if (recipient.GameWorldId != sender.GameWorldId)
            {
                return ChatResult.Reject(
                    ChatRejectionReason.RecipientNotInWorld,
                    "Recipient is not a player in this world.");
            }
            if (!recipient.IsAlive)
            {
                return ChatResult.Reject(
                    ChatRejectionReason.RecipientEliminated,
                    "Recipient has been eliminated.");
            }
        }
        else if (recipient is not null)
        {
            return ChatResult.Reject(
                ChatRejectionReason.InvalidScopePayload,
                "ToPlayerId must be null for Global/Alliance messages.");
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            GameWorldId = sender.GameWorldId,
            FromPlayerId = sender.Id,
            ToPlayerId = scope == ChatScope.Direct ? recipient!.Id : null,
            Scope = scope,
            Body = trimmed,
            SentAtUtc = utcNow,
        };
        return ChatResult.Accept(message);
    }
}
