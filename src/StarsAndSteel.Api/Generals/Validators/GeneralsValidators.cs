using FluentValidation;
using StarsAndSteel.Api.Generals.Dtos;

namespace StarsAndSteel.Api.Generals.Validators;

public sealed class RecruitGeneralRequestValidator : AbstractValidator<RecruitGeneralRequest>
{
    public RecruitGeneralRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .Must(s => s is not null && s.Trim().Length is > 0 and <= 80)
                .WithMessage("Name must be 1-80 characters after trimming.");
    }
}

public sealed class AssignGeneralRequestValidator : AbstractValidator<AssignGeneralRequest>
{
    public AssignGeneralRequestValidator()
    {
        RuleFor(x => x.ProvinceId).NotEmpty();
    }
}
