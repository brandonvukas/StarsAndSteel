using FluentValidation;
using StarsAndSteel.Api.Chat.Dtos;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Chat.Validators;

/// <summary>
/// Phase 2K validation. Body length is bounded to the DB column (≤500 chars)
/// and trimmed-non-empty. ToPlayerId presence is gated by Scope: required iff
/// Direct, forbidden otherwise.
/// </summary>
public sealed class SendChatMessageRequestValidator : AbstractValidator<SendChatMessageRequest>
{
    public SendChatMessageRequestValidator()
    {
        RuleFor(x => x.Scope)
            .IsInEnum()
                .WithMessage("Scope must be Global, Alliance, or Direct.");

        RuleFor(x => x.Body)
            .NotNull()
            .Must(b => !string.IsNullOrWhiteSpace(b))
                .WithMessage("Body must be non-empty.")
            .MaximumLength(500)
                .WithMessage("Body must be ≤500 characters.");

        // Direct messages MUST carry a target; broadcast scopes MUST NOT.
        When(x => x.Scope == ChatScope.Direct, () =>
        {
            RuleFor(x => x.ToPlayerId)
                .NotNull()
                    .WithMessage("ToPlayerId is required for Direct messages.")
                .Must(id => id != Guid.Empty)
                    .WithMessage("ToPlayerId must be a non-empty Guid.");
        });

        When(x => x.Scope != ChatScope.Direct, () =>
        {
            RuleFor(x => x.ToPlayerId)
                .Null()
                    .WithMessage("ToPlayerId must be null for Global/Alliance messages.");
        });
    }
}
