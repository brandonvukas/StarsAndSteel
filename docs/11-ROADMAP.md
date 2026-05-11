# 11 — Roadmap

Phased plan for getting from zero to a playable Stars & Steel. Each phase produces something *demoable* — no two-month dark phases.

I've sized phases in "focused weekends" (assuming this is a hobby project). Adjust expectations to taste.

---

## Phase 0 — Setup *(½ weekend)*

Getting your dev environment ready, no code yet.

- ☐ Confirm name + sign-off on this docs folder
- ☐ Install: Visual Studio 2022, .NET 10 SDK, SQL Server LocalDB, Node 20+
- ☐ `git init` the repo, commit `docs/`, set up `.gitignore`
- ☐ Create the empty `StarsAndSteel.sln` with the four backend projects
- ☐ Create the empty `client/` with Vite scaffold (`npm create vite@latest client -- --template vanilla-ts`)
- ☐ Create the empty `shared/` folder; stub `shared/map-data.json` with two test provinces; wire the Vite `@shared` alias and the server `<Content Include>` reference

**Demoable:** repo opens cleanly in VS. `dotnet build` and `npm run dev` both succeed with empty placeholders. Both server and client can read the stub `map-data.json`.

---

## Phase 1 — MVP foundation *(2 weekends optimistic, 4–6 realistic)*

The user's original ask, executed end to end.

- ☑ EF Core entities: `User : IdentityUser<Guid>`, `GameWorld` (with `RngState` + `RowVersion`), `Player`, `Province`, `ProvinceAdjacency` (composite PK + ordered-pair invariant), `Unit`, `UnitOrder`, `Building`, `NewsItem`, `AiMemory`
- ☑ `StarsAndSteelDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>`, configurations
- ☑ Migration 1: `InitialIdentity`
- ☑ Migration 2: `InitialGameWorld`
- ☐ ~~Migration 3: `SeedDefaultMap`~~ — superseded. Provinces have a non-nullable `GameWorldId` FK and are therefore per-world, not global. The `MapSeeder` (in `StarsAndSteel.Data/Seeding/`) is now a runtime helper that reads `shared/map-data.json` and produces row records; `WorldFactory` (Phase 1F) calls it inside the world-creation transaction so each new `GameWorld` gets its own copy of the map.
- ☑ Auth endpoints (`/api/auth/*`) with Identity + JWT (Phase 1D — cookie for SPA, JWT for SignalR, FluentValidation, rate-limited)
- ☑ World snapshot endpoint with fog-of-war filtering (Phase 1G): `GET /api/worlds/{id}/snapshot` returns `WorldSnapshot` (DTOs in `Core/Snapshots/`). `SnapshotService` (in `Game/Snapshots/`) is pure — visibility set = owned ∪ adjacent-to-owned; owned provinces show full detail (resources, buildings, garrison), adjacent show owner + polygon + stationed enemy units (morale masked), distant show polygon only. Enemy in-transit units are hidden. `Players[]` summary omits resources; `Me` includes them. 401 unauth, 403 non-member, 404 unknown world. 11 pure tests + 3 `[DockerFact]` integration tests.
- ☑ Order endpoints (Phase 1H): `OrdersController` with `move`, `attack`, `airstrike`, `build-unit`, `build-building`. Each holds the per-world `WorldLockRegistry` semaphore while it reads `world.CurrentTick`, validates ownership/feasibility through pure `OrderService` (in `Game/Orders/`), and stamps `IssuedAtTick = CurrentTick + 1`. Build orders debit player resources atomically with the order insert. New `ConstructionOrder` entity + `ConstructionOrderConfiguration` + Migration 3 `AddConstructionOrders` (separate from `UnitOrder` to avoid the polymorphic-nullable trap; see docs/03 design notes). Costs/times sourced from `BuildCatalog` (units = docs/04 table; buildings MVP-scoped, not in docs). 28 pure tests + 8 `[DockerFact]` integration tests (move accepted/non-adjacent/cross-player, build-building debit, build-unit ticks, insufficient resources 409, unknown enum 400, anonymous 401).
- ☑ World-join flow (Phase 1F): `WorldFactory` builds province graphs from `MapSeeder.Load()` inside a transaction; `WorldJoinService` assigns a candidate-capital, applies starter package (5000/1000/1000/500/1000/2000 resources, RC+MB+AB+FD lvl 1, 2× MechInf 1000 + 1× AA 500), and flips Lobby → Active on first join. `WorldsController`: `GET /api/worlds`, `POST /api/worlds`, `GET /api/worlds/{id}`, `POST /api/worlds/{id}/join` — join is held under the per-world `WorldLockRegistry` semaphore so it cannot race a tick.
- ☑ `GameTickService` skeleton with parallel per-world processing + per-world re-entrancy lock (Phase 1E)
- ☑ `TickProcessor` with the canonical pipeline (AI first, deterministic RNG seeded from `world.RngState`, optimistic-concurrency `RowVersion` check) — orchestrator + DeterministicRandom (LCG, persistable) + ITickStep contract landed in 1E. **Phase 1O completes the MVP gameplay step set: 11 steps wired** (AiTurn → ResourceProduction → LogisticsUpkeep → Attrition → Movement → AirStrike → Combat → Construction → MoraleRecovery → VictoryCheck → News). Naval/Cyber/random-event steps land in Phase 2/3.
- ☑ `ResourceProductionStep` (1E), `MovementStep`, `AirStrikeStep`, `CombatStep`, `ConstructionStep` (Phase 1I), plus **`LogisticsUpkeepStep`, `AttritionStep`, `MoraleRecoveryStep`, `VictoryCheckStep` (Phase 1O)** — all wired into `TickProcessor` canonical pipeline; `TickRunner` loads Units + pending UnitOrders/ConstructionOrders + ProvinceAdjacencies, applies `UnitsToInsert/BuildingsToInsert/UnitsToDelete` deltas, and cleans orphan UnitOrders before deleting Units.
- ☑ `CombatResolver` with combined-arms formula in `04-GAME-MECHANICS.md` + unit tests (Phase 1I): `CombatStats` matrix (NationalGuard/SpecialForces fold into MechInf row/col; Strategic/Stealth share bomber row), effective-strength formula `Strength × UnitTypeStrength × (0.5 + 0.5*morale/100) × (1 + 0.005*xp) × roll(0.85..1.15)`, +20% combined-arms (ground+air+AA on same side), pairwise damage proportional to target Strength share, defender wipe → ownership flip + morale -20. AirStrike: no range check, "raid and return"; StealthBomber 60% AA-bypass. 99 pure tests + 3 new `[DockerFact]` end-to-end tests in `TickPipelineEndToEndTests.cs` (move/build-building/build-unit through real HTTP+DB, force-tick by rewinding `NextTickDueUtc` and invoking `TickRunner.RunAsync`).
- ☑ News templates and step (Phase 1M) — `NewsTemplates` static catalogue (5 categories: ProvinceCaptured Breaking/Combat, AirStrikeResolved & CombatResolved Notable/Combat, UnitBuilt Info/Politics, BuildingCompleted Info/Economy) with multi-variant headlines + bodies. `PickVariant(IRandomSource)` selects deterministically via per-world RNG so replays produce identical headlines; `Render` substitutes `{key}` tokens and leaves unknown keys as literal `{token}` (deliberate non-throwing failure mode so a typo never stops a tick). `NewsStep` runs **last** in `TickProcessor` so it can react to all prior step events (snapshots `context.Events` before iterating to avoid recursing on its own `NewsPublishedEvent`s); suppresses the `CombatResolved` headline when a `ProvinceCaptured` fires same-tick same-province (one card per engagement). `NewsItemsToInsert` collection on `TickContext` mirrors `UnitsToInsert`; `TickRunner` inserts before `SaveChangesAsync` so a row is never visible without the world state that produced it. New `NewsPublishedEvent` flows through `TickBroadcaster` → `NewsPublished` SignalR DTO (now 10 hub events). New `GET /api/worlds/{id}/news?since={tick}` endpoint returns up to 200 ascending rows for reconnect backfill (403 for non-members; 404 for missing world). 14 new pure tests (5 `NewsTemplatesTests` + 9 `NewsStepTests` covering capture/combat-suppression/air strike/build/recursion-guard/non-headline events) + 2 new `[DockerFact]`s in `NewsEndToEndTests.cs` (AI=1 5-tick run produces persisted rows + endpoint backfill works; outsider gets 403). Client: `NewsItem`/`NewsPublished` types + `HubEvents.NewsPublished`; `$news` nanostore atom (cap 50, deduped by id, newest-first) with `pushNews`/`setNews` helpers; `getNews(worldId, since)` REST wrapper; `mountNewsTicker` HUD component below the map (severity-color-coded — Breaking red, Notable amber, Info blue) wired in `gameScreen.ts` with backfill on mount and on hub reconnect.
- ☑ `GameHub` with the events listed in `06-BACKEND-API.md` (Phase 1J): hub mounted at `/hubs/game`, JWT-only auth (clients pass `access_token` query string per the standard SignalR pattern; cookie auth doesn't survive the WebSocket upgrade reliably). `JoinWorld(worldId)` validates Player membership before adding to the per-world `world:{worldId:N}` group; `LeaveWorld` and `Ping` round out the C→S surface. `TickBroadcaster` (in `Api/Hubs/`) maps each pure `TickEvent` to a wire DTO (`Api/Hubs/Dtos/TickEventDtos.cs`) and broadcasts to the world group AFTER `TickRunner.SaveChangesAsync` commits — clients never observe state the DB doesn't hold. Terminal `TickAdvanced` event is always sent last as a barrier so clients can apply queued diffs atomically. Per-player filtering (resources, fog-aware unit positions) deferred to Phase 1K — for now everything broadcasts to the world group and clients filter. 2 new `[DockerFact]`s using `Microsoft.AspNetCore.SignalR.Client` 10.0.7 over `TestServer.CreateHandler` + `LongPolling` transport: anonymous-rejected and move-broadcasts-UnitMoved-then-TickAdvanced.
- ☑ Phaser scenes: Boot, Map (province polygons + interaction), Hud (Phase 1K)
- ☑ HTML overlay: top resource bar, province detail panel, unit panel, news ticker (Phase 1K — resource bar + province detail + draft-order forms; news ticker added in Phase 1M)
- ☑ State store + SignalR diff handlers + draft-order localStorage persistence (Phase 1K — `nanostores`-based atoms in `client/src/store/store.ts`; pure diff reducers in `client/src/diff/applyDiffs.ts`; auth persisted to `sessionStorage`. Hub wrapper in `client/src/api/hub.ts` consumes the 9 server→client events from Phase 1J. Login → lobby → game flow in `client/src/ui/{loginScreen,lobbyScreen,gameScreen}.ts`. Polygon geometry from `@shared/map-data.json`; correlation to snapshot rows by `(centerX, centerY)`.)
- ☑ One AI personality: **Hawk** (Phase 1L) — `WorldFactory.Build(name, seed, map, aiOpponentCount)` (MVP: 0 or 1) optionally seats a Hawk AI at world creation via shared `PlayerSpawner` helper (refactored out of `WorldJoinService` so AI seating reuses the same starter package + buildings + units). AI-only worlds stay in `Lobby` until a human joins (avoids ticking against no opponent). `HawkPlanner` (in `Game/Ai/`) is a pure greedy attack-or-recruit heuristic: scans owned provinces for adjacent enemies, picks the strongest non-AA ground stack, and issues `Attack` if `attackerStrength × CombatStats.UnitTypeStrength > 1.2 × defenderStrength`; otherwise queues a 1000-strength MechInfantry build at the lex-smallest owned province with a Recruitment Center (debits resources). `AiTurnStep` registered first in `TickProcessor` so AI orders join the same tick (`IssuedAtTick = ProcessingTick`, not +1, since they're already inside the tick). 7 new pure tests (5 HawkPlanner scenarios + 2 AiTurnStep) + 2 new `[DockerFact]`s in `AiOpponentEndToEndTests.cs` (AI=1 stays Lobby until human joins → AI acts within 3 ticks; AI=0 seats no AI player). `CreateWorldRequest.AiOpponentCount` validated 0..1 by FluentValidation.
- ☑ Real-world map (Phase 1N) — replaced the 2-province USA/Canada stub with a script-generated North America map. New `scripts/` directory with isolated `package.json` (deps: `@turf/turf` 7.2.0, `d3-geo` 3.1.1) hosts `build-map.mjs`: fetches Natural Earth admin_1 1:10m GeoJSON (cached to `.cache/`, ~40 MB one-time), filters to US/Canada/Mexico, merges Canada into 5 blocs (`canada-west/prairies/ontario/quebec/atlantic`) and Mexico into 3 blocs (`mexico-north/central/south`), projects through d3-geo Albers (NA-centered, parallels 29.5/45.5) onto a 1600×1000 viewport with 40 px padding, simplifies each polygon with turf at 1.5 px tolerance, computes shared-segment adjacencies (snap-to-grid 0.5 px hash) on the **un-simplified** projected geometry to avoid false-negative drops, patches sea crossings (HI↔CA/OR, AK↔Canada West), assigns one of eight resource profiles per state (tech/finance/industrial/oil/agricultural/resource/urban/mixed), verifies graph connectivity with union-find. Output: `shared/map-data.json` v2 with **58 provinces** (50 US states + 5 Canada blocs + 3 Mexico blocs, every province `ProvinceType.Capital` per design call), 138 land adjacencies, 171 KB. New JSON fields: `version`, `basePopulation`, `isCoastal`, `baseResourceOutput` — DTOs in `MapSeeder` already matched the v2 shape. Phaser canvas resized 800×600 → 1600×1000 with `Scale.FIT` + `CENTER_BOTH` so the full map renders at any window size. `MapSeederTests` rewritten to cover graph invariants (58 provinces, all Capital, all adjacencies reference known ids, fully connected, deterministic Guids); `WorldsEndpointsTests`, `OrdersEndpointTests`, and `SnapshotEndpointTests` updated to discover the spawn province + neighbours from the snapshot rather than hard-coding "United States"/"Canada" (PlayerSpawner picks non-deterministically across 58 capitals via per-world Guid order; refining spawn rules deferred to Phase 2). 122 pure pass / 27 Docker skip.
- ☑ Five units live: Mech Infantry, Main Battle Tank, AA Battery, Combat Drone, Multirole Fighter (in fact all 12 unit types are catalogued in `BuildCatalog` and `UpkeepCatalog`, `CombatStats` covers them; the buildable surface in MVP includes the headline five plus the rest)
- ☑ MVP buildings live: Recruitment Center, Military Base, Air Base, Steel Mill, Refinery, Financial District (six MVP buildings wired in `BuildCatalog` with full cost/build-time; ResourceProductionStep applies the +20%/+30%/+25% multipliers per docs/04)
- ☑ **Phase 1O — gameplay completion**: `LogisticsUpkeepStep` drains per-stack upkeep from owner pools (food/manpower for ground, money/oil for air) using new `UpkeepCatalog`; `AttritionStep` taxes 2% strength + 5 morale per tick on units in non-owned territory (queues stacks at 0 strength to `UnitsToDelete` with `UnitDestroyedEvent` cause="Attrition"); `MoraleRecoveryStep` recovers +1 morale per tick on owned non-besieged provinces and on garrisoned friendly units; `VictoryCheckStep` flips world to `Ended` + sets `EndedAt` + marks losers `IsAlive=false` when one player owns ≥80% of provinces (also handles immediate per-player elimination at 0 provinces, MVP simplification of docs/07 §EventStep's 3-tick grace). New `VictoryAchievedEvent` + `PlayerEliminatedEvent` records; `NewsTemplates` extended with Breaking/Politics victory + Notable/Politics elimination headlines; `NewsStep` switch handles both event types. Canonical step order: AiTurn → ResourceProduction → LogisticsUpkeep → Attrition → Movement → AirStrike → Combat → Construction → MoraleRecovery → VictoryCheck → News. **150 pure pass / 27 Docker skip** (+28 from 1N: 7 MoraleRecovery, 7 Attrition, 6 LogisticsUpkeep, 6 VictoryCheck, 2 NewsStep coverage for new events).

**Demoable:** you can register, join the world, see the world map, queue a build, watch a tick happen, watch the Hawk AI hit your border with a drone strike, and see a breaking-news headline about it.

This is where we'd publish a playable build to your friends.

---

## Phase 2 — Diplomacy & depth *(2–4 weekends)*

Turn the demo into a real strategic game with social mechanics.

- ☐ DiplomaticRelation entity + endpoints + Hub events
- ☐ Diplomacy panel UI
- ☐ Treaty proposal/response flow
- ☐ Coalition victory condition
- ☐ Chat system (per-world + per-alliance)
- ☑ Research / tech tree (basic — 12 techs)
- ☐ Add air units: Recon Drone, Attack Helicopter, Strategic Bomber
- ☐ Add ground unit: Special Forces, Mobile Artillery, National Guard
- ☐ Naval Phase 2a: Frigate + Destroyer + sea-province adjacencies
- ☐ Aircraft Carriers (Phase 2b)
- ☐ Remaining AI personalities: Industrialist, Isolationist
- ☐ Schemer with dynamic recalculation
- ☐ Stats / graphs panel (Chart.js)
- ☐ Quiet-hours setting
- ☐ Mobile-friendly layout pass
- ☐ Logistics network bonus

**Demoable:** five-player session with humans + AI, alliances forming and breaking, research being raced, real strategic decisions to make.

---

## Phase 3 — Strategic warfare *(2–4 weekends)*

Make the late game spicy.

- ☐ Stealth Bomber + research-gated stealth drones
- ☐ Submarines
- ☐ Strategic missiles (cruise, ballistic) + missile silos
- ☐ Tactical nukes (toggleable per game)
- ☐ Cyber warfare cells + Cyber Operations Centers
- ☐ Special Forces operations (sabotage, intel)
- ☐ Theater commanders (generals)
- ☐ Doctrines (Maneuver / Firepower / Defense)
- ☐ Wonders / megaprojects (5 to start)
- ☐ Random world events
- ☐ Insurgent (Wildcard) AI personality
- ☐ Sanctions / embargoes

**Demoable:** late-game tension feels distinct from early game, with high-impact strategic moves available.

---

## Phase 4 — Polish & content *(open-ended)*

The fun-stretch list. Tackle in any order.

- ☐ Global commodities market
- ☐ Replay viewer
- ☐ Custom flags / nation insignia
- ☐ AI intelligence advisor (rules-based or local LLM)
- ☐ Achievement system
- ☐ Leaderboard
- ☐ Weather & seasons
- ☐ Signals intercept
- ☐ Recon Satellite wonder + GPS Constellation
- ☐ LLM-generated news headlines (optional, free tier or local model)

---

## Working agreement

A few rules of engagement that have served past hobby projects of this scale well:

- **Trunk-based development.** One branch (`main`). Feature flags for in-progress work.
- **Test what hurts.** Combat resolver, tick processor, AI scoring — these get unit tests. UI doesn't need them yet.
- **Demo every phase.** End each phase with a 30-minute playtest with a friend or two. Adjust before moving on.
- **Numbers will change.** Every constant in `04-GAME-MECHANICS.md` is provisional. Don't get attached.
- **Ship the core loop, then add.** Better to have a janky-but-complete MVP than a polished half of MVP.

---

## What I need from you to start Phase 1

Once you've reviewed the docs and signed off (or pushed back on) the design:

1. Confirm the **name** (or pick from alternates in `00-OVERVIEW.md`).
2. Confirm the **MVP scope** at the bottom of `05-FEATURES.md`.
3. Confirm the **tech choices** in `01-ARCHITECTURE.md`.
4. Tell me the **map style** — full-Earth real-world (default) or a focused theater?
5. **Nukes** — in for Phase 3, or skip entirely?
6. **Featured nation default** — USA prominently, or every nation surfaced equally?

After that, the very first commit is exactly what you originally asked for: the EF Core entities and the initial migration. From there we follow the Phase 1 checklist.
