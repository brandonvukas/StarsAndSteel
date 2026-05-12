using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Phase 3a: drains pending <see cref="OrderType.MissileLaunch"/> orders. Each launch
/// consumes the launching missile stack (CruiseMissile or NuclearMissile) and applies
/// damage at the target province.
/// <para/>
/// Damage model (intentionally coarse for MVP):
/// <list type="bullet">
///   <item>CruiseMissile: <c>ConventionalDamagePerMissile</c> total strength damage spread
///   evenly across enemy stacks at the target. No radiation. No building damage in MVP
///   (deferred — would require a per-building HP model).</item>
///   <item>NuclearMissile: <c>NuclearDamagePerMissile</c> spread across enemy stacks
///   (much higher, often wipes the province), AND adds <c>NuclearRadiationApplied</c>
///   to <see cref="Province.RadiationLevel"/> (capped at 100). Radiation reduces resource
///   output via <c>ResourceProductionStep.RadiationFactor</c>.</item>
/// </list>
/// Diplomacy gating: nuking an Ally / Peace partner is permitted by the engine (the
/// player issued an explicit launch order at their own peril); the news/morale/diplomatic
/// fallout from "hey you nuked your ally" is left to higher-level systems.
/// <para/>
/// MVP scoping:
/// <list type="bullet">
///   <item>No interception (cruise-missile defenses, ABM systems) — Phase 3b/c.</item>
///   <item>No multi-province blast radius — only the targeted tile.</item>
///   <item>No friendly-fire on the launcher's own units at the target (defensive — they
///   shouldn't be there in the first place).</item>
/// </list>
/// Slots between AirStrikeStep and CombatStep in the pipeline so missile losses are
/// applied before ground combat resolves at the same tick.
/// </summary>
public sealed class MissileImpactStep : ITickStep
{
    public string Name => "MissileImpact";

    /// <summary>Total strength damage dealt by one CruiseMissile, summed across defending stacks.</summary>
    internal const int ConventionalDamagePerMissile = 1500;

    /// <summary>Total strength damage dealt by one NuclearMissile (typically obliterates the target).</summary>
    internal const int NuclearDamagePerMissile = 15000;

    /// <summary>RadiationLevel added per nuke. Stacks (capped at 100); decays slowly.</summary>
    internal const int NuclearRadiationApplied = 60;

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var unitsById = context.Units.ToDictionary(u => u.Id);
        var unitsByProvince = context.Units
            .Where(u => u.LocationProvinceId.HasValue && u.Strength > 0)
            .GroupBy(u => u.LocationProvinceId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var provinceById = context.World.Provinces.ToDictionary(p => p.Id);

        foreach (var order in context.PendingUnitOrders)
        {
            if (order.Status != OrderStatus.Pending) continue;
            if (order.OrderType != OrderType.MissileLaunch) continue;
            if (!unitsById.TryGetValue(order.UnitId, out var missile)) { order.Status = OrderStatus.Cancelled; continue; }
            if (missile.Strength <= 0) { order.Status = OrderStatus.Cancelled; continue; }
            if (missile.Domain != UnitDomain.Missile) { order.Status = OrderStatus.Cancelled; continue; }
            if (order.TargetProvinceId is null) { order.Status = OrderStatus.Cancelled; continue; }

            var targetId = order.TargetProvinceId.Value;
            if (!provinceById.TryGetValue(targetId, out var target)) { order.Status = OrderStatus.Cancelled; continue; }

            bool isNuclear = missile.Type == UnitType.NuclearMissile;
            int totalDamage = isNuclear ? NuclearDamagePerMissile : ConventionalDamagePerMissile;

            // Distribute damage across enemy stacks at the target. Friendly stacks are
            // spared (the engine's view is "you wouldn't nuke your own units"; if you
            // did anyway we just don't model it).
            var defenders = unitsByProvince.TryGetValue(targetId, out var stacks)
                ? stacks
                    .Where(u => u.OwnerPlayerId != missile.OwnerPlayerId
                                && u.Strength > 0)
                    .ToList()
                : new List<Unit>();

            int totalDefenderStrength = defenders.Sum(u => u.Strength);
            int defenderLoss = 0;
            if (defenders.Count > 0 && totalDefenderStrength > 0)
            {
                int remaining = totalDamage;
                // Proportional split, then apply.
                foreach (var d in defenders)
                {
                    int share = (int)Math.Round((double)d.Strength / totalDefenderStrength * totalDamage);
                    if (share > remaining) share = remaining;
                    int actual = Math.Min(share, d.Strength);
                    d.Strength -= actual;
                    defenderLoss += actual;
                    remaining -= actual;
                    if (d.Strength == 0)
                    {
                        context.UnitsToDelete.Add(d);
                        context.Events.Add(new UnitDestroyedEvent(
                            Tick: context.ProcessingTick,
                            UnitId: d.Id,
                            OwnerPlayerId: d.OwnerPlayerId,
                            LocationProvinceId: d.LocationProvinceId,
                            Cause: isNuclear ? "Nuked" : "MissileStrike"));
                        // Phase 2b: a sunk carrier still drags its embarked wings.
                        if (d.Type == UnitType.AircraftCarrier)
                        {
                            foreach (var wing in context.Units)
                            {
                                if (wing.ParentUnitId == d.Id && wing.Strength > 0)
                                {
                                    wing.Strength = 0;
                                    context.UnitsToDelete.Add(wing);
                                    context.Events.Add(new UnitDestroyedEvent(
                                        Tick: context.ProcessingTick,
                                        UnitId: wing.Id,
                                        OwnerPlayerId: wing.OwnerPlayerId,
                                        LocationProvinceId: wing.LocationProvinceId,
                                        Cause: "CarrierLost"));
                                }
                            }
                        }
                    }
                }
            }

            int radiationApplied = 0;
            if (isNuclear)
            {
                int before = target.RadiationLevel;
                target.RadiationLevel = Math.Min(100, before + NuclearRadiationApplied);
                radiationApplied = target.RadiationLevel - before;
                // Nuking also tanks morale (population centers vaporized).
                target.MoraleLevel = Math.Max(0, target.MoraleLevel - 50);
            }

            // Consume the missile (one-shot). Mirrors the carrier-wing destruction shape
            // so the runner cleans up orphan rows.
            missile.Strength = 0;
            context.UnitsToDelete.Add(missile);
            context.Events.Add(new UnitDestroyedEvent(
                Tick: context.ProcessingTick,
                UnitId: missile.Id,
                OwnerPlayerId: missile.OwnerPlayerId,
                LocationProvinceId: missile.LocationProvinceId,
                Cause: "MissileLaunched"));

            order.Status = OrderStatus.Complete;
            context.Events.Add(new MissileImpactResolvedEvent(
                Tick: context.ProcessingTick,
                AttackerUnitId: missile.Id,
                AttackerPlayerId: missile.OwnerPlayerId,
                TargetProvinceId: targetId,
                WasNuclear: isNuclear,
                DefenderStrengthLoss: defenderLoss,
                RadiationApplied: radiationApplied));
        }
    }
}
