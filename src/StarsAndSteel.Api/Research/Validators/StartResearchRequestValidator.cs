using FluentValidation;
using StarsAndSteel.Api.Research.Dtos;
using StarsAndSteel.Game.Research;

namespace StarsAndSteel.Api.Research.Validators;

public sealed class StartResearchRequestValidator : AbstractValidator<StartResearchRequest>
{
    public StartResearchRequestValidator()
    {
        RuleFor(x => x.TechId)
            .NotEmpty()
            .Must(TechCatalog.Exists)
                .WithMessage("Unknown techId. Use a key from /api/worlds/{id}/research catalog.");
    }
}
