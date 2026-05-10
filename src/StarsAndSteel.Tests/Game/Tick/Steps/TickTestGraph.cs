using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

/// <summary>
/// POCO-graph helpers shared by the per-step tests in Phase 1I. Mirrors the helper
/// shape used by <c>ResourceProductionStepTests</c>; intentionally small (no fluent
/// builders) to keep test setup readable.
/// </summary>
internal static class TickTestGraph
{
    public static GameWorld NewWorld(int seed = 1) => new()
    {
        Id = Guid.NewGuid(),
        Name = "T",
        Status = GameWorldStatus.Active,
        CurrentTick = 0,
        TickIntervalSeconds = 60,
        NextTickDueUtc = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        MapSeed = seed,
        RngState = seed,
        RowVersion = new byte[8],
    };

    public static Player AddPlayer(GameWorld world, string name) => AddPlayer(world, name, 0, 0, 0, 0, 0, 0);

    public static Player AddPlayer(GameWorld world, string name,
        long money, long oil, long steel, long electronics, long food, long manpower)
    {
        var p = new Player
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            IsAi = false,
            NationName = name,
            FlagPrimaryHex = "#fff",
            FlagSecondaryHex = "#000",
            IsAlive = true,
            Money = money,
            Oil = oil,
            Steel = steel,
            Electronics = electronics,
            Food = food,
            Manpower = manpower,
        };
        world.Players.Add(p);
        return p;
    }

    public static Province AddProvince(GameWorld world, Player? owner, string name = "P", ProvinceType type = ProvinceType.Industrial)
    {
        var p = new Province
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            Name = name,
            Type = type,
            OwnerPlayerId = owner?.Id,
            OwnerPlayer = owner,
            MoraleLevel = 100,
        };
        world.Provinces.Add(p);
        owner?.OwnedProvinces.Add(p);
        return p;
    }

    public static ProvinceAdjacency Adj(Province a, Province b)
    {
        // Maintain ProvinceAId < ProvinceBId invariant (docs/03).
        var (lo, hi) = a.Id.CompareTo(b.Id) < 0 ? (a, b) : (b, a);
        return new ProvinceAdjacency
        {
            ProvinceAId = lo.Id,
            ProvinceA = lo,
            ProvinceBId = hi.Id,
            ProvinceB = hi,
            TerrainCost = 1.0f,
        };
    }

    public static Unit AddUnit(GameWorld world, Player owner, Province location, UnitType type, int strength,
        int morale = 100, int xp = 0)
    {
        var domain = type >= UnitType.ReconDrone ? UnitDomain.Air : UnitDomain.Ground;
        var u = new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            OwnerPlayerId = owner.Id,
            OwnerPlayer = owner,
            LocationProvinceId = location.Id,
            LocationProvince = location,
            Type = type,
            Domain = domain,
            Strength = strength,
            Morale = morale,
            Experience = xp,
        };
        location.UnitsStationed.Add(u);
        owner.OwnedUnits.Add(u);
        return u;
    }

    public static Building AddBuilding(Province province, BuildingType type, int level = 1)
    {
        var b = new Building
        {
            Id = Guid.NewGuid(),
            ProvinceId = province.Id,
            Province = province,
            Type = type,
            Level = level,
            ConstructedAtTick = 0,
        };
        province.Buildings.Add(b);
        return b;
    }

    public static UnitOrder MoveOrder(Unit unit, Province target, int issuedAtTick = 1) => new()
    {
        Id = Guid.NewGuid(),
        UnitId = unit.Id,
        Unit = unit,
        OrderType = OrderType.Move,
        TargetProvinceId = target.Id,
        TargetProvince = target,
        IssuedAtTick = issuedAtTick,
        Status = OrderStatus.Pending,
    };

    public static UnitOrder AttackOrder(Unit unit, Province target, int issuedAtTick = 1) => new()
    {
        Id = Guid.NewGuid(),
        UnitId = unit.Id,
        Unit = unit,
        OrderType = OrderType.Attack,
        TargetProvinceId = target.Id,
        TargetProvince = target,
        IssuedAtTick = issuedAtTick,
        Status = OrderStatus.Pending,
    };

    public static UnitOrder AirStrikeOrder(Unit unit, Province target, int issuedAtTick = 1) => new()
    {
        Id = Guid.NewGuid(),
        UnitId = unit.Id,
        Unit = unit,
        OrderType = OrderType.AirStrike,
        TargetProvinceId = target.Id,
        TargetProvince = target,
        IssuedAtTick = issuedAtTick,
        Status = OrderStatus.Pending,
    };

    public static ConstructionOrder BuildUnitOrder(GameWorld w, Player owner, Province p, UnitType type, int qty, int ticksRemaining, int issuedAtTick = 1) => new()
    {
        Id = Guid.NewGuid(),
        GameWorldId = w.Id,
        GameWorld = w,
        OwnerPlayerId = owner.Id,
        OwnerPlayer = owner,
        ProvinceId = p.Id,
        Province = p,
        OrderType = OrderType.BuildUnit,
        UnitType = type,
        Quantity = qty,
        IssuedAtTick = issuedAtTick,
        TicksRemaining = ticksRemaining,
        Status = OrderStatus.Pending,
    };

    public static ConstructionOrder BuildBuildingOrder(GameWorld w, Player owner, Province p, BuildingType type, int ticksRemaining, int issuedAtTick = 1) => new()
    {
        Id = Guid.NewGuid(),
        GameWorldId = w.Id,
        GameWorld = w,
        OwnerPlayerId = owner.Id,
        OwnerPlayer = owner,
        ProvinceId = p.Id,
        Province = p,
        OrderType = OrderType.BuildBuilding,
        BuildingType = type,
        Quantity = 1,
        IssuedAtTick = issuedAtTick,
        TicksRemaining = ticksRemaining,
        Status = OrderStatus.Pending,
    };

    public static TickContext Context(GameWorld world,
        IList<Unit>? units = null,
        IList<UnitOrder>? unitOrders = null,
        IList<ConstructionOrder>? constructionOrders = null,
        IList<ProvinceAdjacency>? adjacencies = null,
        IList<TreatyOffer>? pendingTreatyOffers = null,
        long? rngSeed = null)
    {
        return new TickContext(
            world,
            processingTick: world.CurrentTick + 1,
            rng: new DeterministicRandom(rngSeed ?? world.RngState),
            units: units ?? new List<Unit>(),
            pendingUnitOrders: unitOrders ?? new List<UnitOrder>(),
            pendingConstructionOrders: constructionOrders ?? new List<ConstructionOrder>(),
            adjacencies: adjacencies ?? new List<ProvinceAdjacency>(),
            pendingTreatyOffers: pendingTreatyOffers ?? new List<TreatyOffer>());
    }
}
