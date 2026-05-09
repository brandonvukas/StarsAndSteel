namespace StarsAndSteel.Game.Tick.Events;

/// <summary>Emitted by AirStrikeStep after each strike resolves.</summary>
public sealed record AirStrikeResolvedEvent(
    int Tick,
    Guid AttackerUnitId,
    Guid AttackerPlayerId,
    Guid TargetProvinceId,
    int AttackerStrengthLoss,
    int DefenderStrengthLoss      // sum across all defending stacks at the target
) : TickEvent(Tick);

/// <summary>Emitted by CombatStep after each ground engagement.</summary>
public sealed record CombatResolvedEvent(
    int Tick,
    Guid ProvinceId,
    Guid AttackerPlayerId,
    Guid DefenderPlayerId,
    int AttackerStrengthLoss,
    int DefenderStrengthLoss,
    Guid? WinnerPlayerId          // null = stalemate
) : TickEvent(Tick);

/// <summary>Emitted by CombatStep when an attacker takes a province (defender empty after combat).</summary>
public sealed record ProvinceCapturedEvent(
    int Tick,
    Guid ProvinceId,
    Guid? FromPlayerId,
    Guid ToPlayerId
) : TickEvent(Tick);
