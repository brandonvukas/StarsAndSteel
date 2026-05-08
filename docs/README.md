# Stars & Steel — Build Plan

Working title: **Stars & Steel** — a persistent, real-time, browser-based modern grand strategy game in the spirit of *Conflict of Nations: World War 3*. You command a modern nation on a living world map. Drones, fighter jets, strategic bombers, tanks, cyber warfare. Friends fill the great-power seats; AI rivals run the rest. The world keeps ticking whether you're logged in or not.

The American flavor is woven throughout — USA is the default featured nation, the iconography leans on stars-and-stripes/Pentagon-briefing-room aesthetics, and the news feed reads like a cable network.

This folder is the **plan** — no code yet. Read through these files and confirm (or push back on) the design before we start building.

## How to read these docs

Read them top-to-bottom. They progress from "what is this game?" to "how do we build it?"

| # | File | What's in it |
|---|------|--------------|
| 00 | [OVERVIEW.md](./00-OVERVIEW.md) | Name, pitch, MVP vs full vision, target experience |
| 01 | [ARCHITECTURE.md](./01-ARCHITECTURE.md) | System diagram, tech stack, key principles |
| 02 | [PROJECT-STRUCTURE.md](./02-PROJECT-STRUCTURE.md) | Solution / folder layout for backend + frontend |
| 03 | [DATABASE-SCHEMA.md](./03-DATABASE-SCHEMA.md) | EF Core models, relationships, migration plan |
| 04 | [GAME-MECHANICS.md](./04-GAME-MECHANICS.md) | Resources, modern unit roster, combined-arms combat |
| 05 | [FEATURES.md](./05-FEATURES.md) | Diplomacy, research, cyber, espionage, wonders, events |
| 06 | [BACKEND-API.md](./06-BACKEND-API.md) | REST endpoints + SignalR hub methods |
| 07 | [GAME-LOOP.md](./07-GAME-LOOP.md) | The BackgroundService tick — what happens every 60s |
| 08 | [FRONTEND.md](./08-FRONTEND.md) | Phaser.js + TypeScript scenes, map rendering, UI |
| 09 | [AI-OPPONENTS.md](./09-AI-OPPONENTS.md) | Hawk / Industrialist / Isolationist / Schemer / Insurgent |
| 10 | [AUTH-SECURITY.md](./10-AUTH-SECURITY.md) | Auth, anti-cheat, server authority, rate limits |
| 11 | [ROADMAP.md](./11-ROADMAP.md) | Phased build plan with milestones |
| – | [ESTIMATES.md](./ESTIMATES.md) | Time, cost, risks |
| – | [preview.html](./preview.html) | Friend-facing pitch page (open in a browser) |

## What to verify before we start

1. **Name** — happy with *Stars & Steel*, or pick from the alternates in `00-OVERVIEW.md`?
2. **Setting** — modern world (~2030), real countries on a real-world map. ✅ confirmed.
3. **Map** — full-Earth real-world, ~80 country/region provinces. ✅ confirmed.
4. **Featured nation** — default USA-first, or every nation equally surfaced?
5. **Nukes** — yes/no for the strategic-weapons phase? They're great drama but the optics matter.
6. **Tick rate** — defaulting to 60 seconds per tick, 1 in-game day ≈ 1 real hour. OK?
7. **Feature scope** — `05-FEATURES.md` has the buffet. Cross out scope creep.

## Recent revisions (post first-pass review)

The docs were tightened after a critique pass. Highlights:
- Tick service now processes worlds in **parallel** with per-world re-entrancy locks (one slow world can't starve others). See `07-GAME-LOOP.md`.
- AI runs as the **first** step of the tick (against pre-tick state) so the replay-determinism contract `state(T+1) = f(state(T), orders, rng(T))` actually holds. See `07-GAME-LOOP.md` and `09-AI-OPPONENTS.md`.
- `GameWorld` gets a persisted `RngState` and `RowVersion` (rowversion). All in-tick randomness pulls from the seeded RNG.
- `User` uses `IdentityUser<Guid>`; DbContext is `IdentityDbContext<User, IdentityRole<Guid>, Guid>`. Avoids the silent string-PK trap.
- `ProvinceAdjacency` has a composite PK with the `A.Id < B.Id` invariant — one row per edge.
- Order submission now has an explicit cutoff rule (`IssuedAtTick = CurrentTick + 1`, stamped under the per-world lock).
- **Nation starting state** is fully specified: starting resources, capital with Recruitment Center / Military Base / Air Base / Financial District, two infantry stacks and one AA battery. Resolves "where does my first drone get built?"
- `shared/map-data.json` lives at the repo root; both server seeder and client `MapScene` consume it via `<Content Include>` (server) and Vite `@shared` alias (client).
- Hardened Bunker pulled out of MVP buildings (it's tied to research and was half-defined).
- Draft orders persist to `localStorage` so a refresh doesn't lose your in-progress unit selections.

Once you sign off, the order of operations is in `11-ROADMAP.md` — Phase 1 still starts with EF Core models + the initial migration, exactly as you originally asked for.
