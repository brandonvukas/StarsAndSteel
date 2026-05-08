# 04 — Game Mechanics

Modern military, modern logistics, combined-arms combat. Numbers below are starting values — tune after playtests.

## Time

- **1 tick = 60 real seconds** (configurable on the GameWorld).
- **1 in-game day = 60 ticks (1 real hour).**
- **1 in-game month = 30 days (~1.25 real days).**
- A typical game lasts 2–6 in-game years (≈ 4–12 real weeks). For our friend group we'll run accelerated games at maybe 30s ticks.

## Resources

All resource amounts are stored as `long` in C# / `BIGINT` in SQL Server. Unit and building costs in the tables below are listed in plain integers but flow through `long` arithmetic — late-game inflation never overflows.

| Resource | Earned from | Spent on |
|----------|-------------|----------|
| **Money ($)** | Urban / financial provinces, trade | Universal currency; pays for everything |
| **Oil** | Resource provinces with oilfields, refineries | Fuel for vehicles, aircraft, ships |
| **Steel** | Industrial provinces, steel mills | Armor, ships, infrastructure |
| **Electronics** | Tech provinces, tech parks | Drones, missiles, advanced systems, cyber |
| **Food** | Agricultural provinces, ag sectors | Population & morale; shortage = unrest |
| **Manpower** | Population-heavy provinces, recruitment centers | Personnel for every unit |

Each tick, owned provinces produce their `*PerTick` values into the player's pool. Buildings multiply this:
- **Refinery** → +30%/level oil
- **Steel Mill** → +25%/level steel
- **Tech Park** → +25%/level electronics
- **Agricultural Sector** → +25%/level food
- **Financial District** → +20%/level money
- **Recruitment Center** → +15%/level manpower
- **Logistics Hub** → no resource bonus, but movement through this province is +50% per level

## Provinces

Six types, each with characteristic resource output:

| Type | Money | Oil | Steel | Electronics | Food | Manpower |
|------|------|-----|-------|------------|------|----------|
| Urban | High | 0 | Low | Med | Low | High |
| Industrial | Med | Low | High | Low | 0 | Med |
| Tech | High | 0 | Low | High | 0 | Med |
| Agricultural | Low | 0 | 0 | 0 | High | Med |
| Resource | Low | High | Med | 0 | Low | Low |
| Capital | Med | Med | Med | Med | Med | High |

**Capital rules:** if your capital falls, all your provinces take -50 morale. If it stays fallen for 5 ticks, you're eliminated.

**Morale:** 0–100 per province. Drops when adjacent provinces fall, when you lose battles, when supplies run short. Climbs when you win, build infrastructure, finish propaganda research. Below 30 morale a province produces 50% resources; below 10 it stops and risks defection.

## Unit roster

### Ground

| Unit | Role | Cost | Build | Move |
|------|------|------|-------|------|
| Mechanized Infantry | Cheap, all-purpose | $200 + 100 steel + 100 manpower | 5 ticks | 1.0 |
| National Guard | Cheap defense, weak offense | $100 + 50 steel + 100 manpower | 4 ticks | 1.5 |
| Special Forces | Elite, sabotage, recon | $500 + 50 steel + 50 manpower + 50 electronics | 10 ticks | 0.8 |
| Main Battle Tank | Heavy hitter | $600 + 400 steel + 50 manpower + 100 oil | 12 ticks | 1.2 |
| Mobile Artillery | Long-range ground strike | $500 + 250 steel + 50 manpower + 50 oil | 10 ticks | 1.5 |
| AA Battery | Shoots aircraft & drones | $400 + 200 steel + 100 electronics | 8 ticks | 1.5 |

### Air

| Unit | Role | Cost | Build | Range |
|------|------|------|-------|-------|
| Recon Drone | Cheap scout, lifts fog over a target | $200 + 100 electronics + 50 oil | 4 ticks | 5 provinces |
| Combat Drone | Cheap strike, fragile to AA & fighters | $400 + 200 electronics + 100 oil | 8 ticks | 4 provinces |
| Attack Helicopter | Close air support, kills armor | $700 + 200 steel + 200 electronics + 200 oil | 10 ticks | 3 provinces |
| Multirole Fighter | Air supremacy + ground strike | $1,200 + 500 steel + 400 electronics + 300 oil | 14 ticks | 6 provinces |
| Strategic Bomber | Massive payload, fragile w/o escort | $2,000 + 800 steel + 400 electronics + 500 oil | 18 ticks | 10 provinces |
| Stealth Bomber | Penetrates AA, expensive | $3,500 + 800 steel + 1,200 electronics + 500 oil | 24 ticks | 12 provinces |

### Naval (Phase 2-3)

| Unit | Role |
|------|------|
| Frigate | All-purpose escort |
| Destroyer | Anti-air + anti-ship |
| Submarine | Stealth + missile carrier |
| Aircraft Carrier | Mobile air base |

### Strategic (Phase 3)

| Unit | Role |
|------|------|
| Cruise Missile | Single long-range strike, can be intercepted |
| Ballistic Missile | Intercontinental, harder to intercept |
| Tactical Nuke | Devastating, optional toggle, late-game deterrent |

### Cyber & Special (Phase 3)

| Unit | Role |
|------|------|
| Cyber Warfare Cell | Sabotages enemy production digitally |
| Recon Satellite | Wonder-tier; lifts fog over chosen province per tick |

## MVP units, locked

Phase 1 ships with **five units** that already create rock-paper-scissors gameplay:

- **Mechanized Infantry** (cheap ground)
- **Main Battle Tank** (heavy ground)
- **AA Battery** (anti-air screen)
- **Combat Drone** (cheap air strike)
- **Multirole Fighter** (air superiority + escort)

Add Recon Drone, Helicopter, Bombers, Special Forces in Phase 2.

## Combat (combined arms)

Combat resolves on the tick when an attacker's units enter (or strike from range) a defended province.

### Three-layer engagement

1. **Air phase** — air units engage first.
   - Defending fighters intercept attacking aircraft (drones, bombers, helos).
   - AA batteries fire at attacking aircraft.
   - Surviving attacking aircraft strike ground targets, weakening defender pre-ground combat.
2. **Ground phase** — surviving ground forces engage with the same effective-strength formula:
   ```
   effective = Σ (stack.Strength × unitTypeStrength × moraleMult × xpMult × terrainMult × randomRoll)
   ```
3. **Strategic phase** — long-range strikes (cruise/ballistic missiles) resolve last and bypass ground combat.

### Unit interaction matrix

Rows do damage to columns. Stars are damage tier (★ low → ★★★ devastating). "—" means cannot engage.

|             | MechInf | MBT  | Art  | AA   | Drone | Fighter | Helo | Bomber |
|-------------|:-------:|:----:|:----:|:----:|:-----:|:-------:|:----:|:------:|
| MechInf →   |  ★★     |  ★   |  ★   |  ★★  |  —    |  —      |  —   |  —     |
| MBT →       |  ★★★    | ★★★  |  ★★  |  ★★  |  —    |  —      |  —   |  —     |
| Art →       |  ★★★    |  ★★  |  ★   | ★★★  |  —    |  —      |  —   |  —     |
| AA →        |  —      |  —   |  —   |  —   | ★★★   |  ★★     | ★★★  | ★★★    |
| Drone →     |  ★★     |  ★★  | ★★★  |  ★   |  —    |  —      |  —   |  —     |
| Fighter →   |  —      |  —   |  —   |  —   | ★★★   | ★★★     | ★★★  | ★★★    |
| Helo →      |  ★★★    | ★★★  |  ★★  |  ★   |  —    |  —      |  —   |  —     |
| Bomber →    |  ★★★    | ★★★  | ★★★  |  ★★  |  —    |  —      |  —   |  —     |

The big takeaways: tanks dominate ground but die to helos and bombers. AA is the only check on enemy air. Fighters dominate other air. Bombers need fighter escorts to survive. There is no dominant unit.

### Combined-arms bonus

A force fielding **at least one ground + one air + one anti-air screen** gets +20% effective strength. Encourages real combined-arms play instead of "spam tanks."

### Air supremacy

A province with surviving fighters at end-of-combat denies that airspace next tick. Enemy aircraft passing through that province are intercepted automatically.

### Stealth

Stealth bombers and (Phase 3) stealth drones roll 60% to bypass AA. Brutal when they get through, useless when they don't. Research can boost to 80%.

### Hardened bunkers *(Phase 2+)*

Defenders inside a Hardened Bunker get +30% defense per bunker level (capped at level 5 = +150%). Reduces casualties further. Bunkers can be sieged: each tick of siege drains -2 effective level.

Hardened Bunker is a Phase 2 building, not in MVP — it's tied to the research tree and adds another vector to combat tuning. Pulling it out of MVP keeps the first combat-balance pass simpler.

## Movement

- Issue a `Move` order; ground units move along the shortest adjacency path.
- Each adjacency has a `terrainCost` (1.0 plains, 1.3 desert, 1.5 river, 2.0 mountain).
- A unit's movement-per-tick is `1.0 / unit.MoveCost` adjacency-units.
- Air units don't pay terrain costs; they hop between airbases / carriers within their `Range`.
- Crossing into hostile territory without a war declaration cancels the order. (Phase 2.)

## Recruitment / production

A player issues a `Build` order on one of their provinces. The province must have the appropriate building:

- **Recruitment Center** for infantry / National Guard
- **Military Base** for armor, artillery, AA
- **Air Base** for drones, helos, fighters, bombers
- **Naval Yard** (coastal) for ships (Phase 2)
- **Missile Silo** for ballistic missiles (Phase 3)

Every nation's capital starts with Recruitment Center, Military Base, **Air Base**, and Financial District at level 1 (see `03-DATABASE-SCHEMA.md` → Nation starting state). This means every player can build any MVP unit type from tick 1 on their capital, and *only* on their capital until they expand. Building these structures on captured/built provinces unlocks production there.

The order takes N ticks and consumes resources upfront. Province shows a "Constructing X (3 ticks remaining)" badge.

## Victory conditions

- **Total domination** — control 80% of score-weighted provinces.
- **Coalition victory** — alliance jointly holds 80% (Phase 2).
- **Capital sweep** — eliminate every other player's capital.
- **Score victory (timed)** — at game-end-date, highest score wins.

MVP ships with total domination + score victory. Coalition arrives with Phase 2.

## Tunable constants

All numbers above live in `GameConstants.cs` in `StarsAndSteel.Game`. We will absolutely change them after playtests.
