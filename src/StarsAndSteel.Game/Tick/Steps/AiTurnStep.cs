using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Ai;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Step 0 of the tick pipeline (docs/07 §"AiTurnStep" / docs/09 §"Where the AI runs"):
/// run each AI player's planner against the pre-tick world state and inject the resulting
/// orders into the same tick's pending queues. Subsequent steps (Movement, Combat,
/// Construction) consume them just like human-issued orders.
/// <para/>
/// MVP supports a single personality (Hawk). Future personalities slot in here via a
/// switch on <see cref="Core.Entities.Player.AiPersonality"/>.
/// <para/>
/// Phase 2J adds Industrialist, Isolationist, and Schemer planners. Insurgent stays
/// deferred (Phase 3 — needs random multiplier rerolls per game).
/// <para/>
/// Determinism: the planner takes <see cref="TickContext.Rng"/>, so any AI tie-breaking
/// pulls from the same per-world LCG that drives combat and other steps. Replays reproduce.
/// </summary>
public sealed class AiTurnStep : ITickStep
{
    public string Name => "AiTurn";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Iterate AI players in deterministic order (lex Guid). Otherwise dictionary ordering
        // would couple AI behaviour to insertion order, which is fine in EF but not pure.
        var aiPlayers = context.World.Players
            .Where(p => p.IsAi && p.IsAlive && p.AiPersonality.HasValue)
            .OrderBy(p => p.Id)
            .ToList();

        foreach (var ai in aiPlayers)
        {
            AiPlan? plan = ai.AiPersonality!.Value switch
            {
                AiPersonality.Hawk          => HawkPlanner.Plan(ai, context.World, context.Units, context.Adjacencies, context.ProcessingTick, context.Rng),
                AiPersonality.Industrialist => IndustrialistPlanner.Plan(ai, context.World, context.Units, context.Adjacencies, context.ProcessingTick, context.Rng),
                AiPersonality.Isolationist  => IsolationistPlanner.Plan(ai, context.World, context.Units, context.Adjacencies, context.ProcessingTick, context.Rng),
                AiPersonality.Schemer       => SchemerPlanner.Plan(ai, context.World, context.Units, context.Adjacencies, context.ProcessingTick, context.Rng),
                // Insurgent (random multipliers per game) lands in Phase 3.
                _ => null,
            };

            if (plan is null) continue;

            foreach (var order in plan.UnitOrders)
                context.PendingUnitOrders.Add(order);
            foreach (var order in plan.ConstructionOrders)
                context.PendingConstructionOrders.Add(order);
        }
    }
}
