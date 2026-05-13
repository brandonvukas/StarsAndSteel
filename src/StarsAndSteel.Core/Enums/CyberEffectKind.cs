namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Phase 3d: which effect a cyber attack applies at resolution time. Picked
/// deterministically per-order via the per-world RNG inside CyberAttackStep,
/// not at submission time, so the attacker doesn't get to pick the effect.
/// </summary>
public enum CyberEffectKind
{
    /// <summary>Subtract a flat ProgressPoints amount from one in-progress research row of the target owner.</summary>
    SlowResearch = 0,

    /// <summary>Subtract a flat Money amount from the target owner's treasury (clamped at 0).</summary>
    DrainMoney = 1,
}
