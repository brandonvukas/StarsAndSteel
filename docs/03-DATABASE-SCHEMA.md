# 03 — Database Schema

EF Core 10, code-first, SQL Server. Every game-relevant entity is in `StarsAndSteel.Core/Entities/`. Configuration (keys, indexes, relationships) lives in `StarsAndSteel.Data/Configurations/` via `IEntityTypeConfiguration<T>` so the entities themselves stay clean.

## Entity overview

```
User ──< Player >── GameWorld
            │         │
            │         ├──< Province ──< Building
            │         │       │
            │         │       └──< Unit ──< UnitOrder
            │         │
            │         ├──< DiplomaticRelation
            │         ├──< ResearchProgress
            │         ├──< NewsItem
            │         ├──< CyberOperation       (Phase 3)
            │         └──< ChatMessage
            └── 1:1 AiMemory  (only for AI players)
```

A `User` is the human account. A `Player` is that user's seat in a specific `GameWorld`. (One human can be in multiple games.) Provinces, units, etc. all belong to a GameWorld so we can host multiple worlds in the same DB.

## Entities (MVP set)

These are the entities for Phase 1. The full set in `05-FEATURES.md` adds more (cyber ops, strategic missiles, wonders) later.

### `User` — the human account
Inherits `IdentityUser<Guid>` from ASP.NET Identity (the generic variant — the non-generic `IdentityUser` defaults to a `string` PK, which we don't want). Gets Email, PasswordHash, etc. for free.
- `Id` (Guid, PK) — provided by `IdentityUser<Guid>`
- `DisplayName`
- `CreatedAt`
- Navigation: `ICollection<Player> Players`

> **EF Core wiring note for Migration 1:** the DbContext must inherit `IdentityDbContext<User, IdentityRole<Guid>, Guid>` (not the default `IdentityDbContext`) and the Identity service registration must use `AddIdentity<User, IdentityRole<Guid>>()`. Easy to miss; trips up the first migration if you don't.

### `GameWorld` — one game / scenario
- `Id` (Guid, PK)
- `Name` ("World 1", "Sandbox", "Pacific Theater", etc.)
- `Status` enum: `Lobby | Active | Ended`
- `CurrentTick` (int) — incremented every 60s by the BackgroundService
- `TickIntervalSeconds` (int, default 60)
- `NextTickDueUtc` (DateTime) — when the tick service should next process this world
- `CreatedAt`
- `StartedAt`, `EndedAt` (nullable)
- `MapSeed` (int) — for deterministic map generation
- `RngState` (long) — persisted state for the per-world deterministic RNG. Seeded from `MapSeed` at world start, advanced and re-saved every tick. See `07-GAME-LOOP.md` Concurrency & determinism section.
- `RowVersion` (byte[8], `[Timestamp]`) — SQL Server `rowversion`, used as an optimistic-concurrency token by the tick processor.
- Navigation: `Players`, `Provinces`, `NewsItems`

### `Player` — a seat in a game
- `Id` (Guid, PK)
- `UserId` (FK → User, nullable for AI players)
- `GameWorldId` (FK)
- `IsAi` (bool)
- `AiPersonality` (enum: `Hawk | Industrialist | Isolationist | Schemer | Insurgent`, nullable)
- `NationName` ("United States", "Pacific Coalition", "Federation of Brazil", whatever)
- `FlagPrimaryHex`, `FlagSecondaryHex` (string, e.g. `#B22234`, `#1A4F8B`)
- `IsAlive` (bool) — flips false when last province falls
- Resources (denormalized for fast read):
  - `Money` (long)
  - `Oil` (long)
  - `Steel` (long)
  - `Electronics` (long)
  - `Food` (long)
  - `Manpower` (long)
- Navigation: `OwnedProvinces`, `OwnedUnits`

### `Province` — the atomic territory
- `Id` (Guid, PK)
- `GameWorldId` (FK)
- `Name`
- `Type` enum: `Urban | Industrial | Tech | Agricultural | Resource | Capital`
- `OwnerPlayerId` (FK → Player, nullable for neutral provinces)
- `IsCoastal` (bool) — required for naval units
- `CenterX`, `CenterY` (float) — for map rendering
- `MoraleLevel` (int 0–100)
- `BasePopulation` (int)
- `BaseResourceOutput` per tick:
  - `MoneyPerTick`, `OilPerTick`, `SteelPerTick`, `ElectronicsPerTick`, `FoodPerTick`, `ManpowerPerTick`
- Navigation: `Buildings`, `UnitsStationed`, `AdjacentProvinces`

### `ProvinceAdjacency` — undirected edge between two provinces
Stored once per pair, not twice. Invariant: `ProvinceAId < ProvinceBId` (compare Guids). All adjacency queries go through a helper that does `WHERE ProvinceAId = @id OR ProvinceBId = @id` — never assume a direction.
- Composite PK: `(ProvinceAId, ProvinceBId)`
- `ProvinceAId` (FK → Province)
- `ProvinceBId` (FK → Province)
- `TerrainCost` (float, default 1.0) — mountains/rivers/deserts slow movement
- `IsSeaCrossing` (bool) — only naval / air can traverse

### `Unit` — a stack of military units
A "unit" is a stack, not a single soldier. One row = "12,000 mech infantry in Detroit owned by USA."
- `Id` (Guid, PK)
- `GameWorldId` (FK)
- `OwnerPlayerId` (FK)
- `LocationProvinceId` (FK, nullable when in transit)
- `Type` enum: `MechInfantry | NationalGuard | SpecialForces | MainBattleTank | MobileArtillery | AABattery | ReconDrone | CombatDrone | AttackHelicopter | MultiroleFighter | StrategicBomber | StealthBomber` (MVP uses subset)
- `Domain` enum: `Ground | Air | Naval` (computed from Type, stored for fast filter)
- `Strength` (int) — 0 = destroyed
- `Morale` (int 0–100)
- `Experience` (int 0–100) — combat veteran bonus
- `IsInTransit` (bool)
- `TransitFromProvinceId`, `TransitToProvinceId` (FK, nullable)
- `TransitArrivalTick` (int, nullable)
- `HomeBaseProvinceId` (FK, nullable) — for air units (range calculations)
- Navigation: `Orders`

### `UnitOrder` — pending or active orders queued by players
- `Id` (Guid, PK)
- `UnitId` (FK)
- `OrderType` enum: `Move | Attack | Hold | Patrol | AirStrike | ReconSweep`
- `TargetProvinceId` (FK, nullable)
- `IssuedAtTick` (int) — the tick at which this order is eligible to be processed. Stamped server-side as `world.CurrentTick + 1` at submission. Orders submitted while a tick is processing are guaranteed to land in the *next* tick (see `07-GAME-LOOP.md` cutoff rules).
- `Status` enum: `Pending | InProgress | Complete | Cancelled`

### `Building` — improvement on a province
- `Id` (Guid, PK)
- `ProvinceId` (FK)
- `Type` enum: `RecruitmentCenter | MilitaryBase | AirBase | NavalYard | SteelMill | Refinery | TechPark | AgriculturalSector | FinancialDistrict | LogisticsHub | HardenedBunker | MissileSilo | CyberOperationsCenter`
- `Level` (int 1–5)
- `ConstructedAtTick`

### `DiplomaticRelation` — pairwise relationship
- `Id` (Guid, PK)
- `GameWorldId` (FK)
- `FromPlayerId` (FK)
- `ToPlayerId` (FK)
- `Status` enum: `Peace | Allied | NonAggression | War | TradeAgreement`
- `TrustScore` (int -100..100) — used by AI memory
- `LastChangedAtTick`

### `ResearchProgress` — tech tree progress per player
- `Id` (Guid, PK)
- `PlayerId` (FK)
- `TechId` (string — keyed to a static catalogue)
- `ProgressPoints` (int)
- `IsUnlocked` (bool)

### `NewsItem` — log of notable events surfaced in the cable-news feed
- `Id` (Guid, PK)
- `GameWorldId` (FK)
- `Tick` (int)
- `Headline` (string)
- `Body` (string)
- `Severity` enum: `Info | Notable | Breaking`
- `Category` enum: `Combat | Diplomacy | Politics | Economy | Cyber`
- `RelatedPlayerId` (FK, nullable)

### `ChatMessage` — per-game player chat
- `Id` (Guid, PK)
- `GameWorldId` (FK)
- `FromPlayerId` (FK)
- `ToPlayerId` (FK, nullable for "global")
- `Body` (string, max 500)
- `SentAtUtc`

### `AiMemory` — per-AI-player persisted memory blob
One row per AI player. See `09-AI-OPPONENTS.md` for the field meanings.
- `PlayerId` (Guid, PK + FK → Player)
- `MemoryJson` (nvarchar(max)) — serialized memory (grudges, current target, mode, etc.). JSON keeps the schema flexible while we tune AI; we promote fields out of the blob if they ever need to be queried.

## Indexes (the ones that matter)

```
[Provinces]            (GameWorldId)
[Provinces]            (GameWorldId, OwnerPlayerId)   -- "all my provinces"
[Units]                (GameWorldId, OwnerPlayerId)
[Units]                (LocationProvinceId)
[Units]                (Domain)                       -- "all enemy aircraft this tick"
[UnitOrders]           (Status, IssuedAtTick)         -- "pending orders this tick"
[NewsItems]            (GameWorldId, Tick DESC)
[DiplomaticRelations]  (GameWorldId, FromPlayerId)
```

## Migration plan

Three migrations so each is reviewable on its own.

### Migration 1 — `InitialIdentity`
ASP.NET Identity tables (`AspNetUsers`, roles, claims):
```
dotnet ef migrations add InitialIdentity -p src/StarsAndSteel.Data -s src/StarsAndSteel.Api
```

### Migration 2 — `InitialGameWorld`
Adds: `GameWorlds`, `Players`, `Provinces`, `ProvinceAdjacencies`, `Units`, `UnitOrders`, `Buildings`, `NewsItems`, `ChatMessages`, `DiplomaticRelations`, `ResearchProgress`.
```
dotnet ef migrations add InitialGameWorld -p src/StarsAndSteel.Data -s src/StarsAndSteel.Api
```

### ~~Migration 3 — `SeedDefaultMap`~~ (superseded; see below)
Originally planned as a data-only migration that called a `MapSeeder` to insert the ~80 starter provinces.

**Replaced by:** runtime seeding via `WorldFactory`. Provinces have a non-nullable `GameWorldId`, which makes them per-world rather than global. Putting the map into a migration would either (a) require seeding a placeholder world, or (b) leave provinces orphaned. Neither is clean.

The new design:
- `MapSeeder` (`StarsAndSteel.Data/Seeding/MapSeeder.cs`) reads `shared/map-data.json` and exposes the parsed map as flat row records (`ProvinceRow`, `AdjacencyRow`). Stable string IDs in the JSON (e.g. `"test-usa"`) hash deterministically to Guids so re-running on a fresh DB produces identical PKs.
- `WorldFactory` (Phase 1F+) calls `MapSeeder.Load()` inside the world-creation transaction. Each new `GameWorld` gets its own deterministic copy of the province graph.

The seeder still reads from the canonical `shared/map-data.json` (see `02-PROJECT-STRUCTURE.md`) so the server's province list and the client's polygon list cannot drift.

## Nation starting state

When a player joins a `GameWorld` (human via `POST /api/worlds/{id}/join`, or AI via lobby fill), they are assigned a starting capital province and given a fixed starter package. This is what every nation begins with — no exceptions, no tuning per nation in MVP. Balance comes from province placement, not from asymmetric starts.

### Starting resources (per player)

| Money | Oil | Steel | Electronics | Food | Manpower |
|------:|----:|------:|------------:|-----:|---------:|
| 5,000 | 1,000 | 1,000 | 500 | 1,000 | 2,000 |

Enough for a few build orders and a couple of weeks of upkeep before production matters.

### Starting province (the capital)

Every player gets exactly one province at game start, marked as `Capital`. It comes pre-built with:

| Building | Level |
|----------|------:|
| Recruitment Center | 1 |
| Military Base | 1 |
| Air Base | 1 |
| Financial District | 1 |

The Air Base inclusion is intentional — it resolves the "where does my first Combat Drone get built?" question. Every nation can produce every MVP unit type from tick 1 if they choose, on their capital.

### Starting units (per player)

| Unit | Quantity | Stationed at |
|------|---------:|--------------|
| Mechanized Infantry | 2 stacks of strength 1,000 | Capital |
| AA Battery | 1 stack of strength 500 | Capital |

No starter aircraft, no starter armor. Players who want them must build them. This makes the first 5–10 ticks meaningfully about decisions — what do I build first? — rather than executing a pre-determined plan.

### Capital assignment

The map seeder marks ~12 provinces as candidate capitals (geographically spread, balanced resource neighborhoods). Players are assigned candidates in join order; AI fills the rest. If two friend groups want to play "USA vs Russia" specifically, the lobby allows pre-assignment by world creator (Phase 2 nicety; MVP is first-come-first-served from the candidate pool).

## Connection string

The connection string is **not** committed to the repo. It lives in .NET user-secrets so each developer can target their own SQL instance (LocalDB, named instance, container, etc.) without touching tracked files.

First-time setup on a fresh clone:

```pwsh
# from repo root; the Api project already has a UserSecretsId so init is idempotent
dotnet user-secrets set "ConnectionStrings:StarsAndSteelDb" `
  "Server=YOUR_INSTANCE;Database=StarsAndSteel;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True" `
  --project src/StarsAndSteel.Api/StarsAndSteel.Api.csproj
```

Replace `YOUR_INSTANCE` with one of:
- `(localdb)\MSSQLLocalDB` — built-in lightweight engine
- `localhost\SQLEXPRESS` — SQL Server Express default instance name
- `MACHINE\NAMEDINSTANCE` — full SQL Server with a named instance (e.g. `BVUKAS5080\MSSQL2025`)
- `localhost,1433` — Dockerized SQL Server with SQL auth (then add `User Id=sa;Password=...;` instead of `Trusted_Connection=True`)

Once set, EF Core tooling picks it up automatically because `Api` is the startup project and user-secrets are wired into `WebApplication.CreateBuilder` in Development by default.

Verify with:
```pwsh
dotnet user-secrets list --project src/StarsAndSteel.Api/StarsAndSteel.Api.csproj
dotnet ef migrations list -p src/StarsAndSteel.Data -s src/StarsAndSteel.Api
```

## Sample C# entity (for flavor, not final)

```csharp
public class Province
{
    public Guid Id { get; set; }
    public Guid GameWorldId { get; set; }
    public string Name { get; set; } = default!;
    public ProvinceType Type { get; set; }
    public bool IsCoastal { get; set; }

    public Guid? OwnerPlayerId { get; set; }
    public Player? OwnerPlayer { get; set; }

    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public int MoraleLevel { get; set; } = 100;

    public int MoneyPerTick { get; set; }
    public int OilPerTick { get; set; }
    public int SteelPerTick { get; set; }
    public int ElectronicsPerTick { get; set; }
    public int FoodPerTick { get; set; }
    public int ManpowerPerTick { get; set; }

    public ICollection<Building> Buildings { get; set; } = new List<Building>();
    public ICollection<Unit> UnitsStationed { get; set; } = new List<Unit>();
}
```
