using StarsAndSteel.Game.Orders;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Drains per-stack upkeep (food/manpower for ground, money/oil for air) from each
/// owning player's pool every tick. Runs immediately after
/// <see cref="ResourceProductionStep"/> so the freshly-produced income is available
/// to pay this tick's bills.
/// <para/>
/// When the player can't afford a unit's upkeep we don't refund or partial-charge —
/// we deduct what we can (clamping the pool at zero) and the affected unit takes a
/// <c>-3</c> morale hit per missing resource line. Models hungry, unpaid troops
/// without forcing immediate disbandment (which would surprise a player who just
/// briefly ran out of food).
/// <para/>
/// Costs come from <see cref="UpkeepCatalog"/>, scaled per 1000 strength. A 500-strength
/// stack pays half; a 2000-strength stack pays double.
/// </summary>
public sealed class LogisticsUpkeepStep : ITickStep
{
    public string Name => "LogisticsUpkeep";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var playersById = context.World.Players.ToDictionary(p => p.Id);

        foreach (var unit in context.Units)
        {
            if (unit.Strength <= 0) continue;
            if (!playersById.TryGetValue(unit.OwnerPlayerId, out var player)) continue;

            var spec = UpkeepCatalog.Get(unit.Type);
            // Scale the per-1000 spec by the actual stack size, rounding up so a
            // 1-soldier stack still costs at least 1 of each line it owes.
            var scale = unit.Strength / 1000.0;
            var money = (long)Math.Ceiling(spec.Money * scale);
            var oil = (long)Math.Ceiling(spec.Oil * scale);
            var food = (long)Math.Ceiling(spec.Food * scale);
            var manpower = (long)Math.Ceiling(spec.Manpower * scale);

            var moraleHit = 0;
            if (!TryCharge(player, ResourceKind.Money, money)) moraleHit += 3;
            if (!TryCharge(player, ResourceKind.Oil, oil)) moraleHit += 3;
            if (!TryCharge(player, ResourceKind.Food, food)) moraleHit += 3;
            if (!TryCharge(player, ResourceKind.Manpower, manpower)) moraleHit += 3;

            if (moraleHit > 0)
            {
                unit.Morale = Math.Max(0, unit.Morale - moraleHit);
            }
        }
    }

    private enum ResourceKind { Money, Oil, Food, Manpower }

    /// <summary>Try to deduct <paramref name="amount"/> from <paramref name="player"/>'s pool.
    /// Returns true if the pool covered it (or amount was zero); false if it had to clamp at zero.</summary>
    private static bool TryCharge(Core.Entities.Player player, ResourceKind kind, long amount)
    {
        if (amount <= 0) return true;
        switch (kind)
        {
            case ResourceKind.Money:
                if (player.Money >= amount) { player.Money -= amount; return true; }
                player.Money = 0; return false;
            case ResourceKind.Oil:
                if (player.Oil >= amount) { player.Oil -= amount; return true; }
                player.Oil = 0; return false;
            case ResourceKind.Food:
                if (player.Food >= amount) { player.Food -= amount; return true; }
                player.Food = 0; return false;
            case ResourceKind.Manpower:
                if (player.Manpower >= amount) { player.Manpower -= amount; return true; }
                player.Manpower = 0; return false;
            default:
                return true;
        }
    }
}
