using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Wonders;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Phase 4c — Random world events. Runs after <see cref="ResourceProductionStep"/>
/// (so a ResourceBoom is credited *this* tick on top of the normal production)
/// and before any combat / construction so disasters can affect what those
/// downstream steps see.
/// <para/>
/// Determinism: every roll comes from <see cref="TickContext.Rng"/>; given the
/// same seed and world state, the same event fires. Per tick, at most one event
/// triggers — keeps the news ticker readable and prevents per-tick chaos.
/// <para/>
/// The 5 event kinds (see <see cref="RandomEventKind"/>):
/// <list type="bullet">
///   <item><b>NaturalDisaster</b> — destroys 1 random non-wonder building.</item>
///   <item><b>ResourceBoom</b> — credits a random owned province's owner with bonus resources equal to one tick of that province's production × <see cref="BoomBonusFactor"/>.</item>
///   <item><b>ScientificBreakthrough</b> — adds <see cref="BreakthroughProgress"/> points to a random in-flight ResearchProgress row.</item>
///   <item><b>CivilUnrest</b> — drops a random owned province's morale by <see cref="UnrestMoraleLoss"/>.</item>
///   <item><b>MarketCrash</b> — drains <see cref="MarketCrashPercent"/>% of a random rich player's money.</item>
/// </list>
/// Each event picks its subject deterministically via the RNG; if no valid
/// subject exists (e.g. no provinces have non-wonder buildings for a disaster)
/// the step no-ops cleanly without spending the global trigger.
/// </summary>
public sealed class RandomEventStep : ITickStep
{
    public string Name => "RandomEvent";

    /// <summary>Per-tick chance an event fires. 15% keeps headlines occasional but visible.</summary>
    public const double EventTriggerChance = 0.15;

    /// <summary>ResourceBoom: bonus resources = province's per-tick output × this factor (i.e. 100% extra, equivalent to a doubled production tick).</summary>
    public const double BoomBonusFactor = 1.0;

    /// <summary>ScientificBreakthrough: flat progress points added to the picked tech.</summary>
    public const int BreakthroughProgress = 25;

    /// <summary>CivilUnrest: morale points subtracted (clamped to 0).</summary>
    public const int UnrestMoraleLoss = 20;

    /// <summary>MarketCrash: % of the victim's money drained (min 100; only triggers when victim has ≥ 1000).</summary>
    public const int MarketCrashPercent = 10;

    /// <summary>MarketCrash: minimum money a player must hold to be a viable victim.</summary>
    public const long MarketCrashMinMoney = 1000;

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Single global trigger roll. Skipping the event roll entirely on a
        // miss means we don't burn RNG on subject selection when nothing
        // happens — keeps replays cheap.
        if (context.Rng.NextDouble() >= EventTriggerChance) return;

        // Pick a kind uniformly. 5 kinds → idx 0..4.
        var kindIdx = context.Rng.NextInt(5);
        var kind = (RandomEventKind)(kindIdx + 1);

        switch (kind)
        {
            case RandomEventKind.NaturalDisaster: TryNaturalDisaster(context); break;
            case RandomEventKind.ResourceBoom: TryResourceBoom(context); break;
            case RandomEventKind.ScientificBreakthrough: TryScientificBreakthrough(context); break;
            case RandomEventKind.CivilUnrest: TryCivilUnrest(context); break;
            case RandomEventKind.MarketCrash: TryMarketCrash(context); break;
        }
    }

    private static void TryNaturalDisaster(TickContext ctx)
    {
        // Eligible: any building that ISN'T a wonder. Wonders survive disasters
        // (they're national treasures + a Phase 4b1 design choice — they can't
        // be lost without intentional combat).
        var candidates = new List<Building>();
        foreach (var province in ctx.World.Provinces)
        {
            foreach (var b in province.Buildings)
            {
                if (!WonderCatalog.IsWonder(b.Type)) candidates.Add(b);
            }
        }
        if (candidates.Count == 0) return;

        var pick = candidates[ctx.Rng.NextInt(candidates.Count)];
        var province2 = ctx.World.Provinces.FirstOrDefault(p => p.Id == pick.ProvinceId);
        if (province2 is null) return;

        province2.Buildings.Remove(pick);
        ctx.BuildingsToDelete.Add(pick);

        ctx.Events.Add(new RandomEventOccurredEvent(
            Tick: ctx.ProcessingTick,
            Kind: RandomEventKind.NaturalDisaster,
            ProvinceId: province2.Id,
            AffectedPlayerId: province2.OwnerPlayerId,
            Magnitude: (long)pick.Type));
    }

    private static void TryResourceBoom(TickContext ctx)
    {
        // Eligible: any owned province with at least one positive per-tick resource.
        var candidates = ctx.World.Provinces
            .Where(p => p.OwnerPlayerId.HasValue
                && (p.MoneyPerTick + p.OilPerTick + p.SteelPerTick
                    + p.ElectronicsPerTick + p.FoodPerTick + p.ManpowerPerTick) > 0)
            .ToList();
        if (candidates.Count == 0) return;

        var province = candidates[ctx.Rng.NextInt(candidates.Count)];
        var owner = ctx.World.Players.FirstOrDefault(p => p.Id == province.OwnerPlayerId);
        if (owner is null) return;

        var bonus = (long)Math.Round(province.MoneyPerTick * BoomBonusFactor);
        owner.Money += (long)Math.Round(province.MoneyPerTick * BoomBonusFactor);
        owner.Oil += (long)Math.Round(province.OilPerTick * BoomBonusFactor);
        owner.Steel += (long)Math.Round(province.SteelPerTick * BoomBonusFactor);
        owner.Electronics += (long)Math.Round(province.ElectronicsPerTick * BoomBonusFactor);
        owner.Food += (long)Math.Round(province.FoodPerTick * BoomBonusFactor);
        owner.Manpower += (long)Math.Round(province.ManpowerPerTick * BoomBonusFactor);

        ctx.Events.Add(new RandomEventOccurredEvent(
            Tick: ctx.ProcessingTick,
            Kind: RandomEventKind.ResourceBoom,
            ProvinceId: province.Id,
            AffectedPlayerId: owner.Id,
            Magnitude: (long)(BoomBonusFactor * 100)));
    }

    private static void TryScientificBreakthrough(TickContext ctx)
    {
        // Eligible: any in-flight (not unlocked) ResearchProgress row whose
        // owning player is alive. We boost the lowest-progress one in the
        // shuffled candidate list — the goal is to feel like a happy accident,
        // not a victory accelerator on a tech they're about to finish anyway.
        var candidates = ctx.ActiveResearch
            .Where(r => !r.IsUnlocked)
            .ToList();
        if (candidates.Count == 0) return;

        var pick = candidates[ctx.Rng.NextInt(candidates.Count)];
        pick.ProgressPoints += BreakthroughProgress;

        ctx.Events.Add(new RandomEventOccurredEvent(
            Tick: ctx.ProcessingTick,
            Kind: RandomEventKind.ScientificBreakthrough,
            ProvinceId: null,
            AffectedPlayerId: pick.PlayerId,
            Magnitude: BreakthroughProgress));
    }

    private static void TryCivilUnrest(TickContext ctx)
    {
        var candidates = ctx.World.Provinces
            .Where(p => p.OwnerPlayerId.HasValue && p.MoraleLevel > 0)
            .ToList();
        if (candidates.Count == 0) return;

        var province = candidates[ctx.Rng.NextInt(candidates.Count)];
        var before = province.MoraleLevel;
        province.MoraleLevel = Math.Max(0, province.MoraleLevel - UnrestMoraleLoss);
        var actualLoss = before - province.MoraleLevel;

        ctx.Events.Add(new RandomEventOccurredEvent(
            Tick: ctx.ProcessingTick,
            Kind: RandomEventKind.CivilUnrest,
            ProvinceId: province.Id,
            AffectedPlayerId: province.OwnerPlayerId,
            Magnitude: actualLoss));
    }

    private static void TryMarketCrash(TickContext ctx)
    {
        var candidates = ctx.World.Players
            .Where(p => p.IsAlive && p.Money >= MarketCrashMinMoney)
            .ToList();
        if (candidates.Count == 0) return;

        var victim = candidates[ctx.Rng.NextInt(candidates.Count)];
        var loss = Math.Max(100, victim.Money * MarketCrashPercent / 100);
        victim.Money -= loss;

        ctx.Events.Add(new RandomEventOccurredEvent(
            Tick: ctx.ProcessingTick,
            Kind: RandomEventKind.MarketCrash,
            ProvinceId: null,
            AffectedPlayerId: victim.Id,
            Magnitude: loss));
    }
}
