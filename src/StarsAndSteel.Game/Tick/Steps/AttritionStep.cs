using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Step 4 of the tick pipeline (docs/07 §"AttritionStep"): units sitting on territory
/// they don't own bleed strength and morale every tick. Models the cost of operating
/// without a friendly supply chain.
/// <para/>
/// MVP rule: any unit whose <see cref="Core.Entities.Unit.LocationProvinceId"/> resolves
/// to a province whose owner is not the unit's owner (including neutral provinces) loses
/// <c>2%</c> strength and <c>5</c> morale per tick. Units in transit are skipped — their
/// path cost is the strength tax. Air units are skipped too — they recover at home base
/// each tick in MVP and <see cref="AirStrikeStep"/> already taxes their strength.
/// <para/>
/// Stacks reduced to <c>Strength &lt;= 0</c> are queued in <see cref="TickContext.UnitsToDelete"/>
/// and emit <see cref="UnitDestroyedEvent"/>.
/// </summary>
public sealed class AttritionStep : ITickStep
{
    public string Name => "Attrition";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var provincesById = context.World.Provinces.ToDictionary(p => p.Id);

        foreach (var unit in context.Units)
        {
            if (unit.Strength <= 0) continue;
            if (unit.IsInTransit) continue;
            if (unit.Domain == Core.Enums.UnitDomain.Air) continue;
            if (unit.LocationProvinceId is null) continue;
            if (!provincesById.TryGetValue(unit.LocationProvinceId.Value, out var province)) continue;

            // Friendly territory: no attrition.
            if (province.OwnerPlayerId == unit.OwnerPlayerId) continue;

            // Round up so a single soldier in a 50-strength stack still loses something.
            var strengthLoss = Math.Max(1, (int)Math.Ceiling(unit.Strength * 0.02));
            unit.Strength = Math.Max(0, unit.Strength - strengthLoss);
            unit.Morale = Math.Max(0, unit.Morale - 5);

            if (unit.Strength == 0)
            {
                context.UnitsToDelete.Add(unit);
                context.Events.Add(new UnitDestroyedEvent(
                    Tick: context.ProcessingTick,
                    UnitId: unit.Id,
                    OwnerPlayerId: unit.OwnerPlayerId,
                    LocationProvinceId: province.Id,
                    Cause: "Attrition"));
            }
        }
    }
}
