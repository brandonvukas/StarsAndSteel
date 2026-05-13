using FluentValidation;
using StarsAndSteel.Api.Orders.Dtos;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Orders.Validators;

public sealed class MoveOrderRequestValidator : AbstractValidator<MoveOrderRequest>
{
    public MoveOrderRequestValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.TargetProvinceId).NotEmpty();
    }
}

public sealed class AttackOrderRequestValidator : AbstractValidator<AttackOrderRequest>
{
    public AttackOrderRequestValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.TargetProvinceId).NotEmpty();
    }
}

public sealed class AirStrikeOrderRequestValidator : AbstractValidator<AirStrikeOrderRequest>
{
    public AirStrikeOrderRequestValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.TargetProvinceId).NotEmpty();
    }
}

public sealed class MissileLaunchOrderRequestValidator : AbstractValidator<MissileLaunchOrderRequest>
{
    public MissileLaunchOrderRequestValidator()
    {
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.TargetProvinceId).NotEmpty();
    }
}

public sealed class CyberAttackOrderRequestValidator : AbstractValidator<CyberAttackOrderRequest>
{
    public CyberAttackOrderRequestValidator()
    {
        RuleFor(x => x.LaunchProvinceId).NotEmpty();
        RuleFor(x => x.TargetProvinceId).NotEmpty();
        RuleFor(x => x.TargetProvinceId)
            .NotEqual(x => x.LaunchProvinceId)
                .WithMessage("Cyber attack target must differ from the launch province.");
    }
}

public sealed class BuildUnitOrderRequestValidator : AbstractValidator<BuildUnitOrderRequest>
{
    public BuildUnitOrderRequestValidator()
    {
        RuleFor(x => x.ProvinceId).NotEmpty();
        RuleFor(x => x.UnitType)
            .NotEmpty()
            .Must(s => Enum.TryParse<UnitType>(s, ignoreCase: false, out _))
                .WithMessage("UnitType must be one of the UnitType enum values.");
        RuleFor(x => x.Quantity).InclusiveBetween(1, 10000);
    }
}

public sealed class BuildBuildingOrderRequestValidator : AbstractValidator<BuildBuildingOrderRequest>
{
    public BuildBuildingOrderRequestValidator()
    {
        RuleFor(x => x.ProvinceId).NotEmpty();
        RuleFor(x => x.BuildingType)
            .NotEmpty()
            .Must(s => Enum.TryParse<BuildingType>(s, ignoreCase: false, out _))
                .WithMessage("BuildingType must be one of the BuildingType enum values.");
    }
}
