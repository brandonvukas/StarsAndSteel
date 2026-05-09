namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Part of the docs/07 §"EventStep" cleanup phase, broken out for clarity. Each tick,
/// every owned, non-besieged province recovers <c>+1</c> morale (capped at 100). A
/// province is "besieged" when at least one enemy ground unit is stationed on it.
/// <para/>
/// Stationed friendly units also recover <c>+1</c> morale per tick when they're on a
/// province their owner controls — gives <see cref="AttritionStep"/> losses a slow
/// path back to full readiness.
/// <para/>
/// Pure: no events emitted (recovery is silent), no DB calls.
/// </summary>
public sealed class MoraleRecoveryStep : ITickStep
{
    public string Name => "MoraleRecovery";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Province morale recovery: skip neutral provinces (no owner to recover for) and
        // any province with at least one enemy ground unit present (besieged).
        foreach (var province in context.World.Provinces)
        {
            if (province.OwnerPlayerId is null) continue;
            if (province.MoraleLevel >= 100) continue;

            var besieged = false;
            foreach (var unit in context.Units)
            {
                if (unit.LocationProvinceId == province.Id
                    && unit.OwnerPlayerId != province.OwnerPlayerId
                    && unit.Strength > 0
                    && unit.Domain == Core.Enums.UnitDomain.Ground)
                {
                    besieged = true;
                    break;
                }
            }
            if (besieged) continue;

            province.MoraleLevel = Math.Min(100, province.MoraleLevel + 1);
        }

        // Unit morale recovery: only when the unit is on a province owned by the unit's
        // owner. AttritionStep has the inverse rule (-1 morale on hostile territory) so
        // the two compose into a stable equilibrium for garrisoned units.
        foreach (var unit in context.Units)
        {
            if (unit.Morale >= 100) continue;
            if (unit.IsInTransit) continue;
            if (unit.LocationProvinceId is null) continue;

            var province = context.World.Provinces.FirstOrDefault(p => p.Id == unit.LocationProvinceId);
            if (province is null) continue;
            if (province.OwnerPlayerId != unit.OwnerPlayerId) continue;

            unit.Morale = Math.Min(100, unit.Morale + 1);
        }
    }
}
