using StarsAndSteel.Core.Entities;
using StarsAndSteel.Game.News;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Final pre-RNG-persist step (docs/07 §"NewsStep"): scans the events emitted by earlier
/// steps and converts the notable ones into <see cref="NewsItem"/> rows + matching
/// <see cref="NewsPublishedEvent"/>s on the wire.
/// <para/>
/// Determinism: all variant selection pulls from <see cref="TickContext.Rng"/>, which is
/// seeded from <c>world.RngState</c>. Replays produce the same headlines.
/// <para/>
/// MVP coverage:
/// - <see cref="ProvinceCapturedEvent"/> → Breaking/Combat
/// - <see cref="AirStrikeResolvedEvent"/> → Notable/Combat
/// - <see cref="CombatResolvedEvent"/> → Notable/Combat (one per engagement)
/// - <see cref="UnitBuiltEvent"/> → Info/Politics
/// - <see cref="BuildingCompletedEvent"/> → Info/Economy
/// <para/>
/// Steps that emit <see cref="UnitMovedEvent"/> / <see cref="UnitDestroyedEvent"/> /
/// <see cref="ResourcesProducedEvent"/> are intentionally not headline-worthy in MVP — too
/// chatty for a 60-second tick. They're still on the wire for the client store.
/// </summary>
public sealed class NewsStep : ITickStep
{
    public string Name => "News";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Snapshot Events to a separate list — we'll be appending NewsPublishedEvents below
        // and we don't want to recursively news-ify our own news.
        var sourceEvents = context.Events.ToList();
        var playerById = context.World.Players.ToDictionary(p => p.Id);
        var provinceById = context.World.Provinces.ToDictionary(p => p.Id);

        foreach (var ev in sourceEvents)
        {
            switch (ev)
            {
                case ProvinceCapturedEvent e:
                    EmitProvinceCaptured(context, e, playerById, provinceById);
                    break;
                case AirStrikeResolvedEvent e:
                    EmitAirStrike(context, e, playerById, provinceById);
                    break;
                case CombatResolvedEvent e:
                    EmitCombat(context, e, playerById, provinceById);
                    break;
                case UnitBuiltEvent e:
                    EmitUnitBuilt(context, e, playerById, provinceById);
                    break;
                case BuildingCompletedEvent e:
                    EmitBuildingCompleted(context, e, playerById, provinceById);
                    break;
                case VictoryAchievedEvent e:
                    EmitVictoryAchieved(context, e);
                    break;
                case CoalitionVictoryAchievedEvent e:
                    EmitCoalitionVictory(context, e);
                    break;
                case PlayerEliminatedEvent e:
                    EmitPlayerEliminated(context, e);
                    break;
                case TechUnlockedEvent e:
                    EmitTechUnlocked(context, e);
                    break;
                case RandomEventOccurredEvent e:
                    EmitRandomEvent(context, e, playerById, provinceById);
                    break;
                // Other event types are not headline-worthy in MVP.
            }
        }
    }

    private static void EmitProvinceCaptured(
        TickContext ctx,
        ProvinceCapturedEvent e,
        IReadOnlyDictionary<Guid, Player> players,
        IReadOnlyDictionary<Guid, Province> provinces)
    {
        if (!provinces.TryGetValue(e.ProvinceId, out var province)) return;
        var attacker = players.TryGetValue(e.ToPlayerId, out var a) ? a.NationName : "Unknown";
        var defender = e.FromPlayerId.HasValue && players.TryGetValue(e.FromPlayerId.Value, out var d)
            ? d.NationName : "neutral forces";

        Emit(ctx, NewsTemplates.ProvinceCaptured, new Dictionary<string, string>
        {
            ["attacker"] = attacker,
            ["defender"] = defender,
            ["province"] = province.Name,
        }, relatedPlayerId: e.ToPlayerId);
    }

    private static void EmitAirStrike(
        TickContext ctx,
        AirStrikeResolvedEvent e,
        IReadOnlyDictionary<Guid, Player> players,
        IReadOnlyDictionary<Guid, Province> provinces)
    {
        if (!provinces.TryGetValue(e.TargetProvinceId, out var province)) return;
        var attacker = players.TryGetValue(e.AttackerPlayerId, out var a) ? a.NationName : "Unknown";

        Emit(ctx, NewsTemplates.AirStrikeResolved, new Dictionary<string, string>
        {
            ["attacker"] = attacker,
            ["province"] = province.Name,
        }, relatedPlayerId: e.AttackerPlayerId);
    }

    private static void EmitCombat(
        TickContext ctx,
        CombatResolvedEvent e,
        IReadOnlyDictionary<Guid, Player> players,
        IReadOnlyDictionary<Guid, Province> provinces)
    {
        // Skip if this combat resulted in a capture — the ProvinceCaptured headline is the
        // bigger story and we don't want two cards for the same engagement. The capture
        // event is in the same Events list and we look it up.
        var sameProvinceCapture = ctx.Events.OfType<ProvinceCapturedEvent>()
            .Any(c => c.ProvinceId == e.ProvinceId && c.Tick == e.Tick);
        if (sameProvinceCapture) return;

        if (!provinces.TryGetValue(e.ProvinceId, out var province)) return;
        var attacker = players.TryGetValue(e.AttackerPlayerId, out var a) ? a.NationName : "Unknown";
        var defender = players.TryGetValue(e.DefenderPlayerId, out var d) ? d.NationName : "Unknown";

        Emit(ctx, NewsTemplates.CombatResolved, new Dictionary<string, string>
        {
            ["attacker"] = attacker,
            ["defender"] = defender,
            ["province"] = province.Name,
        }, relatedPlayerId: e.WinnerPlayerId ?? e.AttackerPlayerId);
    }

    private static void EmitUnitBuilt(
        TickContext ctx,
        UnitBuiltEvent e,
        IReadOnlyDictionary<Guid, Player> players,
        IReadOnlyDictionary<Guid, Province> provinces)
    {
        if (!provinces.TryGetValue(e.ProvinceId, out var province)) return;
        var owner = players.TryGetValue(e.OwnerPlayerId, out var o) ? o.NationName : "Unknown";

        Emit(ctx, NewsTemplates.UnitBuilt, new Dictionary<string, string>
        {
            ["owner"] = owner,
            ["unitType"] = e.Type.ToString(),
            ["province"] = province.Name,
        }, relatedPlayerId: e.OwnerPlayerId);
    }

    private static void EmitBuildingCompleted(
        TickContext ctx,
        BuildingCompletedEvent e,
        IReadOnlyDictionary<Guid, Player> players,
        IReadOnlyDictionary<Guid, Province> provinces)
    {
        if (!provinces.TryGetValue(e.ProvinceId, out var province)) return;
        var owner = players.TryGetValue(e.OwnerPlayerId, out var o) ? o.NationName : "Unknown";

        // Phase 4b1: wonders get the breaking-news treatment instead of the routine
        // ribbon-cutting line.
        var wonderInfo = StarsAndSteel.Core.Wonders.WonderCatalog.TryGet(e.Type);
        if (wonderInfo is not null)
        {
            Emit(ctx, NewsTemplates.WonderCompleted, new Dictionary<string, string>
            {
                ["owner"] = owner,
                ["wonderName"] = wonderInfo.Name,
                ["province"] = province.Name,
            }, relatedPlayerId: e.OwnerPlayerId);
            return;
        }

        Emit(ctx, NewsTemplates.BuildingCompleted, new Dictionary<string, string>
        {
            ["owner"] = owner,
            ["buildingType"] = e.Type.ToString(),
            ["province"] = province.Name,
        }, relatedPlayerId: e.OwnerPlayerId);
    }

    private static void EmitVictoryAchieved(TickContext ctx, VictoryAchievedEvent e)
    {
        Emit(ctx, NewsTemplates.VictoryAchieved, new Dictionary<string, string>
        {
            ["winner"] = e.WinnerNationName,
            ["owned"] = e.OwnedProvinceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["total"] = e.TotalProvinceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, relatedPlayerId: e.WinnerPlayerId);
    }

    private static void EmitPlayerEliminated(TickContext ctx, PlayerEliminatedEvent e)
    {
        Emit(ctx, NewsTemplates.PlayerEliminated, new Dictionary<string, string>
        {
            ["nation"] = e.NationName,
        }, relatedPlayerId: e.PlayerId);
    }

    private static void EmitCoalitionVictory(TickContext ctx, CoalitionVictoryAchievedEvent e)
    {
        var coalition = string.Join(" + ", e.WinnerNationNames);
        Emit(ctx, NewsTemplates.CoalitionVictoryAchieved, new Dictionary<string, string>
        {
            ["coalition"] = coalition,
            ["owned"] = e.OwnedProvinceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["total"] = e.TotalProvinceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, relatedPlayerId: e.WinnerPlayerIds.FirstOrDefault());
    }

    private static void EmitTechUnlocked(TickContext ctx, TechUnlockedEvent e)
    {
        Emit(ctx, NewsTemplates.TechUnlocked, new Dictionary<string, string>
        {
            ["nation"] = e.PlayerNationName,
            ["tech"] = e.TechName,
        }, relatedPlayerId: e.PlayerId);
    }

    /// <summary>Phase 4c: dispatch one of the 5 random-event templates based on Kind.</summary>
    private static void EmitRandomEvent(
        TickContext ctx,
        RandomEventOccurredEvent e,
        IReadOnlyDictionary<Guid, Player> players,
        IReadOnlyDictionary<Guid, Province> provinces)
    {
        var nation = e.AffectedPlayerId.HasValue && players.TryGetValue(e.AffectedPlayerId.Value, out var p)
            ? p.NationName : "neutral authorities";
        var province = e.ProvinceId.HasValue && provinces.TryGetValue(e.ProvinceId.Value, out var pr)
            ? pr.Name : "an unnamed region";

        switch (e.Kind)
        {
            case RandomEventKind.NaturalDisaster:
            {
                // Magnitude carries the destroyed BuildingType numeric value.
                var bt = ((StarsAndSteel.Core.Enums.BuildingType)(int)e.Magnitude).ToString();
                Emit(ctx, NewsTemplates.NaturalDisaster, new Dictionary<string, string>
                {
                    ["nation"] = nation,
                    ["province"] = province,
                    ["buildingType"] = bt,
                }, relatedPlayerId: e.AffectedPlayerId);
                break;
            }
            case RandomEventKind.ResourceBoom:
                Emit(ctx, NewsTemplates.ResourceBoom, new Dictionary<string, string>
                {
                    ["nation"] = nation,
                    ["province"] = province,
                }, relatedPlayerId: e.AffectedPlayerId);
                break;
            case RandomEventKind.ScientificBreakthrough:
            {
                // Look up the active research row to name the tech. The
                // tech name lookup is best-effort; if the row was finished
                // this same tick (rare) we fall back to "a key technology".
                var row = ctx.ActiveResearch.FirstOrDefault(r =>
                    r.PlayerId == e.AffectedPlayerId && !r.IsUnlocked);
                var techName = row is null
                    ? "a key technology"
                    : (StarsAndSteel.Game.Research.TechCatalog.Find(row.TechId)?.Name ?? row.TechId);
                Emit(ctx, NewsTemplates.ScientificBreakthrough, new Dictionary<string, string>
                {
                    ["nation"] = nation,
                    ["tech"] = techName,
                }, relatedPlayerId: e.AffectedPlayerId);
                break;
            }
            case RandomEventKind.CivilUnrest:
                Emit(ctx, NewsTemplates.CivilUnrest, new Dictionary<string, string>
                {
                    ["nation"] = nation,
                    ["province"] = province,
                    ["magnitude"] = e.Magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }, relatedPlayerId: e.AffectedPlayerId);
                break;
            case RandomEventKind.MarketCrash:
                Emit(ctx, NewsTemplates.MarketCrash, new Dictionary<string, string>
                {
                    ["nation"] = nation,
                    ["magnitude"] = "$" + e.Magnitude.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                }, relatedPlayerId: e.AffectedPlayerId);
                break;
        }
    }

    private static void Emit(
        TickContext ctx,
        NewsTemplate template,
        Dictionary<string, string> values,
        Guid? relatedPlayerId)
    {
        var headlineTemplate = NewsTemplates.PickVariant(template.HeadlineVariants, ctx.Rng);
        var bodyTemplate = NewsTemplates.PickVariant(template.BodyVariants, ctx.Rng);
        var headline = NewsTemplates.Render(headlineTemplate, values);
        var body = NewsTemplates.Render(bodyTemplate, values);

        var item = new NewsItem
        {
            Id = Guid.NewGuid(),
            GameWorldId = ctx.World.Id,
            Tick = ctx.ProcessingTick,
            Headline = headline,
            Body = body,
            Severity = template.Severity,
            Category = template.Category,
            RelatedPlayerId = relatedPlayerId,
        };
        ctx.NewsItemsToInsert.Add(item);
        ctx.Events.Add(new NewsPublishedEvent(
            Tick: ctx.ProcessingTick,
            NewsItemId: item.Id,
            Headline: item.Headline,
            Body: item.Body,
            Severity: item.Severity,
            Category: item.Category,
            RelatedPlayerId: item.RelatedPlayerId));
    }
}
