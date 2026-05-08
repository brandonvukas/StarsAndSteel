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

- ☐ EF Core entities: `User : IdentityUser<Guid>`, `GameWorld` (with `RngState` + `RowVersion`), `Player`, `Province`, `ProvinceAdjacency` (composite PK + ordered-pair invariant), `Unit`, `UnitOrder`, `Building`, `NewsItem`, `AiMemory`
- ☐ `StarsAndSteelDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>`, configurations
- ☑ Migration 1: `InitialIdentity`
- ☑ Migration 2: `InitialGameWorld`
- ☐ ~~Migration 3: `SeedDefaultMap`~~ — superseded. Provinces have a non-nullable `GameWorldId` FK and are therefore per-world, not global. The `MapSeeder` (in `StarsAndSteel.Data/Seeding/`) is now a runtime helper that reads `shared/map-data.json` and produces row records; `WorldFactory` (Phase 1F) calls it inside the world-creation transaction so each new `GameWorld` gets its own copy of the map.
- ☑ Auth endpoints (`/api/auth/*`) with Identity + JWT (Phase 1D — cookie for SPA, JWT for SignalR, FluentValidation, rate-limited)
- ☐ World snapshot endpoint with fog-of-war filtering
- ☐ Order endpoints: move, attack, airstrike, build-unit, build-building — with the cutoff rule (stamps `IssuedAtTick = CurrentTick + 1` under the per-world lock)
- ☑ World-join flow (Phase 1F): `WorldFactory` builds province graphs from `MapSeeder.Load()` inside a transaction; `WorldJoinService` assigns a candidate-capital, applies starter package (5000/1000/1000/500/1000/2000 resources, RC+MB+AB+FD lvl 1, 2× MechInf 1000 + 1× AA 500), and flips Lobby → Active on first join. `WorldsController`: `GET /api/worlds`, `POST /api/worlds`, `GET /api/worlds/{id}`, `POST /api/worlds/{id}/join` — join is held under the per-world `WorldLockRegistry` semaphore so it cannot race a tick.
- ☑ `GameTickService` skeleton with parallel per-world processing + per-world re-entrancy lock (Phase 1E)
- ◐ `TickProcessor` with the 14-step pipeline (AI first, deterministic RNG seeded from `world.RngState`, optimistic-concurrency `RowVersion` check) — orchestrator + DeterministicRandom (LCG, persistable) + ITickStep contract landed in 1E; remaining 13 steps follow.
- ◐ `ResourceProductionStep`, `MovementStep`, `AirStrikeStep`, `CombatStep`, `ConstructionStep` — `ResourceProductionStep` shipped in 1E (formula matches docs/04 incl. building bonuses + morale).
- ☐ `CombatResolver` with combined-arms formula in `04-GAME-MECHANICS.md` + unit tests
- ☐ News templates and step (RNG pulls from per-world state)
- ☐ `GameHub` with the events listed in `06-BACKEND-API.md`
- ☐ Phaser scenes: Boot, Map (province polygons + interaction), Hud
- ☐ HTML overlay: top resource bar, province detail panel, unit panel, news ticker
- ☐ State store + SignalR diff handlers + draft-order localStorage persistence
- ☐ One AI personality: **Hawk**
- ☐ Five units live: Mech Infantry, Main Battle Tank, AA Battery, Combat Drone, Multirole Fighter
- ☐ MVP buildings live: Recruitment Center, Military Base, Air Base, Steel Mill, Refinery, Financial District

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
- ☐ Research / tech tree (basic — 12 techs)
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
