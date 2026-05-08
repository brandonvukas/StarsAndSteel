using FluentValidation;
using StarsAndSteel.Api.Auth.Dtos;

namespace StarsAndSteel.Api.Auth.Validators;

/// <summary>
/// Length and shape checks. Identity itself enforces password complexity
/// (configured in Program.cs) and email-uniqueness, so we don't duplicate
/// those here — we just reject obviously bad input early with a 400.
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9 _.-]+$")
            .WithMessage("Display name may only contain letters, digits, spaces, underscores, periods, or hyphens.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);
    }
}
