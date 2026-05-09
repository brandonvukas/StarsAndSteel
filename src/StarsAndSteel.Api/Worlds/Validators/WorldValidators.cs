using System.Text.RegularExpressions;
using FluentValidation;
using StarsAndSteel.Api.Worlds.Dtos;

namespace StarsAndSteel.Api.Worlds.Validators;

public sealed class CreateWorldRequestValidator : AbstractValidator<CreateWorldRequest>
{
    public CreateWorldRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(100)
            .Matches(@"^[\w\s\-\.\']+$")
                .WithMessage("Name may only contain letters, digits, spaces, hyphens, periods, and apostrophes.");

        RuleFor(x => x.AiOpponentCount)
            .InclusiveBetween(0, 1)
                .WithMessage("AiOpponentCount must be 0 or 1 in MVP.")
            .When(x => x.AiOpponentCount.HasValue);
    }
}

public sealed class JoinWorldRequestValidator : AbstractValidator<JoinWorldRequest>
{
    // #rrggbb only — short-form #rgb is rejected because the column is fixed at 7 chars.
    private static readonly Regex HexColor = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public JoinWorldRequestValidator()
    {
        RuleFor(x => x.NationName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(80);

        RuleFor(x => x.FlagPrimaryHex)
            .NotEmpty()
            .Must(s => s is not null && HexColor.IsMatch(s))
                .WithMessage("FlagPrimaryHex must be a 7-character hex color like '#1a2b3c'.");

        RuleFor(x => x.FlagSecondaryHex)
            .NotEmpty()
            .Must(s => s is not null && HexColor.IsMatch(s))
                .WithMessage("FlagSecondaryHex must be a 7-character hex color like '#1a2b3c'.");
    }
}
