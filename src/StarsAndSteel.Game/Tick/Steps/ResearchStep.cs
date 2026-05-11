using StarsAndSteel.Game.Research;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Phase 2G research-tick step: every active <see cref="Core.Entities.ResearchProgress"/>
/// (passed in via <see cref="TickContext.ActiveResearch"/>) gains 1 ProgressPoint per tick.
/// When the per-tech <c>TicksToResearch</c> threshold is reached, <c>IsUnlocked</c> flips
/// true and a <see cref="TechUnlockedEvent"/> fires for the news ticker.
/// <para/>
/// Runs after Construction (so freshly-completed buildings can't influence research timing
/// retroactively) and before MoraleRecovery (no interaction either way; ordering is purely
/// for log readability).
/// </summary>
public sealed class ResearchStep : ITickStep
{
    public string Name => "Research";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var playerById = context.World.Players.ToDictionary(p => p.Id);

        foreach (var row in context.ActiveResearch)
        {
            if (row.IsUnlocked) continue;

            var spec = TechCatalog.Find(row.TechId);
            if (spec is null) continue; // unknown tech (data drift) — leave alone

            row.ProgressPoints++;
            if (row.ProgressPoints >= spec.TicksToResearch)
            {
                row.IsUnlocked = true;
                row.ProgressPoints = spec.TicksToResearch;
                var nationName = playerById.TryGetValue(row.PlayerId, out var p) ? p.NationName : "Unknown";
                context.Events.Add(new TechUnlockedEvent(
                    Tick: context.ProcessingTick,
                    PlayerId: row.PlayerId,
                    PlayerNationName: nationName,
                    TechId: spec.Id,
                    TechName: spec.Name));
            }
        }
    }
}
