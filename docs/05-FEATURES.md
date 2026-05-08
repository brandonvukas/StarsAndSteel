# 05 — Features

This is the full buffet, retuned for modern warfare. MVP delivers a slice; the rest layer in by phase. Cross out anything you don't want.

Each entry is tagged with the phase where it lands.

## Diplomacy & politics

### 🤝 Treaties & alliances — `Phase 2`
Pairwise: Peace, NonAggression, Allied, War, TradeAgreement. Allies share intel and can move forces through each other's territory. Breaking a treaty drops your global Reputation; some AI personalities never forget.

### 🇺🇳 Coalition victory — `Phase 2`
Alliances jointly holding 80% of map can declare a coalition victory. Must be 30+ in-game days old — no instant-win shenanigans.

### 💬 Secure comms (chat) — `Phase 2`
DMs and alliance channels. AI rivals "monitor" public chat and react to what they overhear (mention "I'm hitting Brandon" in alliance chat → AI ally Brandon's mood drops).

### 🚫 Sanctions & embargoes — `Phase 3`
Cut off trade with another nation. Reduces their money income; gains you reputation with their enemies.

### ☢️ Nuclear deterrence — `Phase 3` *(toggleable)*
Once a nation has nukes, others negotiate differently. Nukes don't have to be used to matter — they sit in the silo and shape every conversation. Toggle in lobby settings if your group prefers a non-nuclear game.

## Economy & infrastructure

### 🏭 Buildings — `Phase 1`
MVP buildings: Recruitment Center, Military Base, Air Base, Steel Mill, Refinery, Financial District. See `04-GAME-MECHANICS.md`.

Hardened Bunker, Tech Park, Logistics Hub, Cyber Operations Center, Missile Silo, and Naval Yard arrive in Phase 2/3 alongside the systems they support (research, logistics network, cyber, missiles, navy).

### 🛣️ Logistics network — `Phase 2`
Build Logistics Hubs in your provinces to create a fast-movement network. The first nation to invest in its backbone moves twice as fast as everyone else.

### 💹 Global commodities market — `Phase 4`
Shared market for oil, steel, electronics. Prices fluctuate with global supply. War shocks send prices wild. Speculate, hedge, profit.

### 🏛️ Megaprojects (Wonders) — `Phase 3`
Massive multi-day builds with global effects. Each only built once per game.
- **Strategic Defense Initiative** — owner intercepts 50% of incoming missiles globally
- **GPS Constellation** — owner sees real-time positions of every unit on the map
- **Carrier Strike Group** — owner gets a free Aircraft Carrier with elite escort
- **Hoover Dam Reborn** — owner gets +50% all resource output for 7 days
- **The Manhattan Program** — unlocks tactical nukes for owner only
- **Cyber Command HQ** — owner's cyber attacks always succeed; immune to incoming cyber

## Military & combat

### ✈️ Full air roster — `Phase 1` (basic), `Phase 2` (full)
Recon Drone, Combat Drone, Attack Helicopter, Multirole Fighter, Strategic Bomber, Stealth Bomber. Each with distinct role and counters. MVP ships Combat Drone + Multirole Fighter; the rest arrive in Phase 2.

### ⚓ Naval warfare — `Phase 2`
Sea provinces with their own adjacency. Frigates, Destroyers, Submarines, Aircraft Carriers. Carrier groups project air power into theaters far from home airbases. Blockades cut maritime trade.

### 🚀 Strategic strikes — `Phase 3`
Cruise missiles, ballistic missiles, ICBMs. Long range, single shot, expensive. Missile defense systems give partial coverage. Doctrine matters: a barrage of cheap cruise missiles can saturate defenses faster than a few ICBMs.

### 💻 Cyber warfare — `Phase 3`
Cyber Cells launch attacks per tick: shut down enemy production for 1 tick, leak intelligence, disable an AA battery just before your air strike. Cyber Operations Centers reduce enemy success rates against you. A whole shadow war runs in parallel to the kinetic one.

### 🪖 Special Forces operations — `Phase 3`
Insert SF teams covertly. Sabotage missions: take out a building, plant intelligence, eliminate a theater commander. High risk, high reward, deniable until they fail.

### 🛡️ Doctrines — `Phase 3`
Pick one at game start: **Maneuver** (-25% movement cost), **Firepower** (+15% damage), **Defense** (+15% defense, +25% bunker effectiveness). Switchable once mid-game.

## Information & atmosphere

### 📺 The Cable News Feed — `Phase 1`
The flagship feature. A live ticker plus a "now playing" segment that recaps the major event of each tick. Sample headlines:
- *"BREAKING: drone swarm strikes naval base in the Aegean — twelve confirmed casualties"*
- *"PROTESTS IN DETROIT enter third day as bread shortages worsen"*
- *"PRESIDENT BVUKAS denies allegations involving a Strasbourg bakery"*
- *"PENTAGON CONFIRMS: stealth bombers crossed into hostile airspace overnight"*

Generated server-side from event templates with a deterministic-per-world RNG so replays reproduce. Phase 4 stretch: optional free-tier LLM call to spice the headlines.

### 🛰️ Reconnaissance & fog of war — `Phase 1`
You see your land + provinces with your units' line of sight. Recon Drones lift fog over a chosen province for several ticks. Recon Satellites (wonders) extend this globally.

### 🕵️ Intelligence agencies — `Phase 3`
Recruit intelligence assets. Each tick they roll for: stealing tech progress, copying enemy unit positions, leaking diplomatic cables, instigating unrest in target province. Counterintelligence catches them; getting caught is a casus belli.

### 📡 Signals intercept — `Phase 4`
Tiny chance per tick that an enemy diplomatic message gets leaked to you or to the news feed. Pure chaos, very fun.

## Progression

### 🔬 Research tree — `Phase 2`
Spend money + electronics on research. Branches: Conventional Military, Air Power, Naval, Cyber, Logistics, Diplomacy. ~25 techs total. Unlocks unit upgrades, doctrines, effects.

### 🎖️ Theater commanders — `Phase 3`
Up to 3 generals per nation. Attach to a stack for combat bonuses. Gain XP. Have personality traits ("Aggressive Doctrine" — +20% attack, -10% defense).

### 🏆 Achievements & leaderboard — `Phase 4`
Per-account achievements (first conquest, perfect defense, comeback win). ELO-ish leaderboard for the friend group.

## Worldbuilding & immersion

### 🌦️ Weather & terrain — `Phase 4`
Sandstorms ground aircraft. Winter slows ground movement. Storms shut down naval ops. Forecasts visible 3 ticks ahead.

### 🌋 World events — `Phase 3`
Per-tick chance of:
- **Refugee crisis** — population shifts between provinces
- **Cyberattack of unknown origin** — random building disabled
- **Diplomatic summit (G20)** — all players get a free non-aggression offer for one tick
- **Coup attempt** — low-morale province threatens to defect
- **Tech breakthrough** — random player gets a free research roll
- **Economic recession** — global money production -15% for 5 ticks

### 🇺🇸 Custom flags & insignia — `Phase 4`
Pick your nation's name, flag colors, and a simple military insignia (built from selectable components). Persisted per-user.

### 📺 Replay viewer — `Phase 4`
Every tick is deterministic. Watch the rise and fall at 10x or 100x speed.

## AI & solo

### 🧠 AI personalities — `Phase 1` (one), `Phase 2` (full set)
Five archetypes: Hawk, Industrialist, Isolationist, Schemer, Insurgent. See `09-AI-OPPONENTS.md`.

### 🤖 Intelligence advisor — `Phase 4`
Optional in-game advisor that flags threats: "Brandon's nation is amassing aircraft within strike range of your eastern airbase." Could be rule-based or LLM-driven (free tier).

## Quality of life

### 📱 Mobile-friendly UI — `Phase 2`
Map and overlay should at least *work* on a phone. Probably not joyful, but enough to give an order on the bus.

### 🔔 Push notifications — `Phase 3`
Browser notifications when attacked or when a treaty offer arrives. Maybe email digests for offline-too-long players.

### 📊 Stats & graphs — `Phase 2`
Resource curves, military strength over time, territory share. Chart.js handles this trivially.

### ⏸️ Quiet hours — `Phase 2`
Optional setting per game: pause world tick from 11pm–7am local. Friend-group default: on. (You don't lose a war while asleep.)

---

## Recommended Phase 1 (MVP) feature set, locked

If you sign off, this is what we build first:
- ✅ Cable news feed (templated breaking-news headlines)
- ✅ Buildings: Recruitment Center, Military Base, Air Base, Steel Mill, Refinery, Financial District
- ✅ Fog of war
- ✅ Five units: Mech Infantry, MBT, AA Battery, Combat Drone, Multirole Fighter
- ✅ Combined-arms combat with air/ground/AA layers
- ✅ One AI personality (the Hawk)
- ✅ Total-domination + score victory
- ✅ ~80-province real-world map
- ✅ Deterministic per-world RNG (foundation for the future replay viewer)

A real, playable game. Everything else is icing.
