using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Orders;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Game.Ai;

/// <summary>
/// Industrialist planner per <c>docs/09-AI-OPPONENTS.md</c>: "Build Economy ×1.5,
/// Diplomacy ×1.4, Attack ×0.6". MVP behaviour:
/// <list type="number">
///   <item>If a province lacks an economy building (SteelMill / Refinery / FinancialDistrict)
///         and we can afford it, queue construction. Cycle through types so we don't
///         monoculture into one resource.</item>
///   <item>Otherwise, recruit a single MechInfantry stack at a province with a
///         RecruitmentCenter for token defense.</item>
/// </list>
/// Industrialists never initiate attacks in MVP (Diplomacy ×1.4 is wired in Phase 3 once
/// the AI proposes/accepts treaties; for now the absence of an attack branch is the
/// "doesn't shoot first" tell). Pure: takes the in-memory graph, returns orders, debits
/// <paramref name="me"/>'s resource columns when a build is queued.
/// </summary>
public static class IndustrialistPlanner
{
    public static AiPlan Plan(
        Player me,
        GameWorld world,
        IEnumerable<Unit> allUnits,
        IEnumerable<ProvinceAdjacency> adjacencies,
        int processingTick,
        IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(me);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rng);
        _ = allUnits; _ = adjacencies; // unused: industrialist ignores combat geometry

        if (!me.IsAi || me.AiPersonality != AiPersonality.Industrialist || !me.IsAlive)
            return Empty;

        // 1) Economy build. Cycle priority by current tick to avoid deterministic
        //    monoculture — first FD (money), then Refinery (oil), then SteelMill.
        var rotation = (processingTick % 3) switch
        {
            0 => new[] { BuildingType.FinancialDistrict, BuildingType.Refinery, BuildingType.SteelMill },
            1 => new[] { BuildingType.Refinery, BuildingType.SteelMill, BuildingType.FinancialDistrict },
            _ => new[] { BuildingType.SteelMill, BuildingType.FinancialDistrict, BuildingType.Refinery },
        };

        foreach (var bt in rotation)
        {
            var build = TryQueueBuilding(me, world, bt, processingTick);
            if (build is not null)
                return new AiPlan(Array.Empty<UnitOrder>(), new[] { build });
        }

        // 2) Token recruit fallback (mirror HawkPlanner shape).
        var recruit = TryQueueRecruitment(me, world, UnitType.MechInfantry, 1000, processingTick);
        if (recruit is not null)
            return new AiPlan(Array.Empty<UnitOrder>(), new[] { recruit });

        return Empty;
    }

    private static readonly AiPlan Empty = new(Array.Empty<UnitOrder>(), Array.Empty<ConstructionOrder>());

    private static ConstructionOrder? TryQueueBuilding(Player me, GameWorld world, BuildingType type, int processingTick)
    {
        if (!BuildCatalog.IsBuildingBuildable(type)) return null;
        var spec = BuildCatalog.GetBuilding(type);

        if (me.Money < spec.Money || me.Oil < spec.Oil || me.Steel < spec.Steel
            || me.Electronics < spec.Electronics || me.Food < spec.Food || me.Manpower < spec.Manpower)
            return null;

        // Pick first owned province lacking this building (lex-Guid order for determinism).
        var province = me.OwnedProvinces
            .Where(p => p.Buildings.All(b => b.Type != type))
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (province is null) return null;

        // Debit. Mirrors OrderService.DebitForBuild.
        me.Money       -= spec.Money;
        me.Oil         -= spec.Oil;
        me.Steel       -= spec.Steel;
        me.Electronics -= spec.Electronics;
        me.Food        -= spec.Food;
        me.Manpower    -= spec.Manpower;

        return new ConstructionOrder
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            OwnerPlayerId = me.Id,
            OwnerPlayer = me,
            ProvinceId = province.Id,
            Province = province,
            OrderType = OrderType.BuildBuilding,
            UnitType = null,
            Quantity = 1,
            BuildingType = type,
            IssuedAtTick = processingTick,
            TicksRemaining = spec.TicksToBuild,
            Status = OrderStatus.Pending,
        };
    }

    internal static ConstructionOrder? TryQueueRecruitment(
        Player me, GameWorld world, UnitType type, int quantity, int processingTick)
    {
        if (!BuildCatalog.IsUnitBuildable(type)) return null;
        var spec = BuildCatalog.GetUnit(type);

        var f = quantity / 1000.0;
        long money       = (long)Math.Ceiling(spec.Money       * f);
        long oil         = (long)Math.Ceiling(spec.Oil         * f);
        long steel       = (long)Math.Ceiling(spec.Steel       * f);
        long electronics = (long)Math.Ceiling(spec.Electronics * f);
        long food        = (long)Math.Ceiling(spec.Food        * f);
        long manpower    = (long)Math.Ceiling(spec.Manpower    * f);

        if (me.Money < money || me.Oil < oil || me.Steel < steel
            || me.Electronics < electronics || me.Food < food || me.Manpower < manpower)
            return null;

        var province = me.OwnedProvinces
            .Where(p => p.Buildings.Any(b => b.Type == spec.RequiredBuilding))
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (province is null) return null;

        me.Money       -= money;
        me.Oil         -= oil;
        me.Steel       -= steel;
        me.Electronics -= electronics;
        me.Food        -= food;
        me.Manpower    -= manpower;

        return new ConstructionOrder
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            OwnerPlayerId = me.Id,
            OwnerPlayer = me,
            ProvinceId = province.Id,
            Province = province,
            OrderType = OrderType.BuildUnit,
            UnitType = type,
            Quantity = quantity,
            BuildingType = null,
            IssuedAtTick = processingTick,
            TicksRemaining = spec.TicksToBuild,
            Status = OrderStatus.Pending,
        };
    }
}
