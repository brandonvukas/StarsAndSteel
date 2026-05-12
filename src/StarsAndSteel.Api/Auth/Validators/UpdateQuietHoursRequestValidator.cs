using FluentValidation;
using StarsAndSteel.Api.Auth.Dtos;

namespace StarsAndSteel.Api.Auth.Validators;

/// <summary>
/// Phase 2L. Quiet hours window. Both bounds must be set together (or both null
/// to clear). When the start is greater than the end, the window wraps midnight
/// — that's fine and explicitly allowed (e.g., 22:00 → 07:00 = overnight).
/// Therefore we don't enforce start &lt; end.
/// </summary>
public sealed class UpdateQuietHoursRequestValidator : AbstractValidator<UpdateQuietHoursRequest>
{
    public UpdateQuietHoursRequestValidator()
    {
        RuleFor(x => x)
            .Must(BothSetOrBothNull)
                .WithMessage("Either provide both QuietHoursStartUtc and QuietHoursEndUtc to set a window, or both null to clear it.");

        // Defensive: identical bounds describe a zero-length window, which is
        // pointless. Reject so the user notices the typo.
        RuleFor(x => x)
            .Must(NotIdentical)
                .When(x => x.QuietHoursStartUtc.HasValue && x.QuietHoursEndUtc.HasValue)
                .WithMessage("QuietHoursStartUtc and QuietHoursEndUtc must differ.");
    }

    private static bool BothSetOrBothNull(UpdateQuietHoursRequest r) =>
        r.QuietHoursStartUtc.HasValue == r.QuietHoursEndUtc.HasValue;

    private static bool NotIdentical(UpdateQuietHoursRequest r) =>
        r.QuietHoursStartUtc!.Value != r.QuietHoursEndUtc!.Value;
}
