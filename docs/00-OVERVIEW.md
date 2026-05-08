# 00 — Overview

## The name

**Stars & Steel** — locked in.

Why: stars-and-stripes plus the steel of modern military hardware. Two short, punchy words. Reads well on a Discord channel name or a Steam page. Pulpy, fun, easy to brand around — and unlike the original "Pax Americana" candidate, it doesn't pretend to be a Latin term nobody outside an IR seminar uses.

Solution: `StarsAndSteel.sln`. Namespace root: `StarsAndSteel`. DB: `StarsAndSteelDb`.

Alternates we considered (kept here for posterity, not for use): Pax Americana, Hegemony, Theater Command, Doctrine, Cold Front, American Dominion, Eagle's Reach.

## One-paragraph pitch

A persistent, real-time grand strategy game that runs in your browser. You command a modern nation — the United States, or one of dozens of other powers — on a shared world map. You manage a modern industrial-tech economy (money, oil, steel, electronics, food, manpower), field a combined-arms military (mech infantry, tanks, drones, fighter jets, bombers, AA), wage cyber and special-ops shadow wars, sign treaties, and break them. Games last days or weeks of real time — the world tick happens every 60 seconds whether you're online or not. You and your friends fill the great-power roster. AI rivals run the rest, each with a personality you'll learn to read.

## Inspiration
- **Conflict of Nations: World War 3** — modern theater, persistent world, real-time tick (the closest reference)
- **Supremacy 1914** — the persistent grand-strategy formula
- **Hearts of Iron** — combined arms, supply, fronts
- **Wargame: Red Dragon** — modern unit feel and combined-arms doctrine
- **Civilization** — research, wonders

## Setting

Near-future Earth, ~2030. Fraying alliances, regional power blocs, modern military hardware. The map is the real world (continents and oceans), divided into ~80 country-or-region provinces. Players pick a nation. The default featured nation is the **United States** — most marketing visuals lean on stars-and-stripes / Pentagon iconography — but every nation is fully playable and balanced.

## Who it's for

You and your friends, primarily. 5–12 nations per game is the sweet spot. AI rivals fill empty seats. Designed for 10-minute sessions a few times a day, not 4-hour binges.

## The core loop

1. **Log in** → check the breaking-news ticker for what happened while you were away
2. **Diagnose** → who struck, what fell, what your intel brief flagged
3. **Adjust** → production, research, diplomatic posture, cyber stance
4. **Order** → move ground forces, launch air strikes, queue construction, set rules of engagement
5. **Log off** → the world keeps spinning; come back in a few hours

## Design pillars

- **Combined-arms decisions.** Tanks die to attack helos. Helos die to AA. AA dies to bombers. Bombers die to fighters. You're constantly playing rock-paper-scissors at scale.
- **Asymmetric tools.** You don't have to win on tanks. Drone swarms, cyber warfare, special ops, and economic strangulation are all real strategies.
- **Diplomacy with teeth.** Alliances matter. Nuclear deterrence (when unlocked) shapes every conversation. Coalitions can crumble.
- **AI that has opinions.** Hawk, Industrialist, Isolationist, Schemer, Insurgent. Each plays differently — and remembers.
- **Modern news immersion.** A stylized cable-news feed pushes alerts: "BREAKING: drone swarm strikes naval base in the Aegean."

## MVP scope (what we ship first)

| Feature | In MVP? |
|---------|---------|
| Real-world map, ~80 provinces | ✅ |
| 6 resources: money, oil, steel, electronics, food, manpower | ✅ |
| 5 unit types: Mech Infantry, Main Battle Tank, AA Battery, Combat Drone, Multirole Fighter | ✅ |
| Real-time 60s tick | ✅ |
| Movement & combat orders | ✅ |
| Combined-arms combat (ground + air + AA layers) | ✅ |
| Cable-news event feed | ✅ |
| Fog of war + recon drones | ✅ |
| One AI personality (the Hawk) | ✅ |
| Login + nation selection | ✅ |
| Province click → context panel | ✅ |
| Diplomacy / alliances | ❌ (Phase 2) |
| Naval | ❌ (Phase 2) |
| Cyber warfare, espionage | ❌ (Phase 3) |
| Strategic missiles / nukes | ❌ (Phase 3) |
| Wonders / megaprojects | ❌ (Phase 3) |

## Full-vision scope (the buffet)

The full feature catalogue lives in `05-FEATURES.md`. MVP exists to prove the loop — economy + movement + combined-arms combat + AI — works. Everything else gets layered on once that core feels good.

## Hard constraints

- **Free libraries only.**
- **Browser-only client.** URL → playing.
- **Server-authoritative.** Every meaningful decision happens on the server.
- **Runs on your existing setup.** Visual Studio + SQL Server (Express is fine).

## Open questions for you

Before we lock the plan, your call on:

1. **Theme intensity.** Grounded (real countries, careful around real-world conflicts) or arcade ("USA vs Generic Red Power")?
2. **Map.** Real world full Earth, or a focused theater (Pacific, Europe-Middle East-Africa)?
3. **Nuclear weapons.** In or out? Great drama, balance is tricky, optics matter.
4. **Default focus.** USA prominently featured (skin, default picks), or every nation surfaced equally?
5. **Tone.** Serious cable-news cosplay, or cheekier ("Pentagon source confirms general was at the bakery")?
