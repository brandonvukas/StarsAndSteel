using FluentValidation;
using StarsAndSteel.Api.Diplomacy.Dtos;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Diplomacy.Validators;

public sealed class DeclareWarRequestValidator : AbstractValidator<DeclareWarRequest>
{
    public DeclareWarRequestValidator()
    {
        RuleFor(x => x.TargetPlayerId).NotEmpty();
    }
}

public sealed class ProposeTreatyRequestValidator : AbstractValidator<ProposeTreatyRequest>
{
    public ProposeTreatyRequestValidator()
    {
        RuleFor(x => x.ReceiverPlayerId).NotEmpty();
        RuleFor(x => x.Kind)
            .NotEmpty()
            .Must(s => Enum.TryParse<TreatyOfferKind>(s, ignoreCase: false, out _))
                .WithMessage("Kind must be one of the TreatyOfferKind enum values (Peace, NonAggression, Alliance).");
    }
}

public sealed class OfferActionRequestValidator : AbstractValidator<OfferActionRequest>
{
    public OfferActionRequestValidator()
    {
        RuleFor(x => x.OfferId).NotEmpty();
    }
}

public sealed class SanctionRequestValidator : AbstractValidator<SanctionRequest>
{
    public SanctionRequestValidator()
    {
        RuleFor(x => x.TargetPlayerId).NotEmpty();
    }
}
