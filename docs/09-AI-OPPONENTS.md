# 09 — AI Opponents

The AI is what makes a 5-player game feel like a 12-player game. We give AI rivals *personalities*, not just difficulty levels — that's what creates memorable moments ("classic Schemer move, hitting the cyber attack two ticks before the air raid").

## Where the AI runs

In the tick — specifically `AiTurnStep`. For each AI player:

```csharp
public interface IAiPersonality
{
    AiPersonalityKind Kind { get; }
    IReadOnlyList<UnitOrder> DecideOrders(WorldSnapshot world, Player me, AiMemory memory, Random rng);
    DiplomaticAction? DecideDiplomacy(WorldSnapshot world, Player me, AiMemory memory, Random rng);
    CyberOperation? DecideCyber(WorldSnapshot world, Player me, AiMemory memory, Random rng); // Phase 3
}
```

The result is orders to enqueue (processed *this* tick — see `07-GAME-LOOP.md`, AI runs as the first step against pre-tick state for replay determinism) and optional diplomatic / cyber actions. The AI never bypasses the order system — it plays the same game humans play, with the same validation.

The `Random rng` parameter is the per-world deterministic RNG seeded from `GameWorld.RngState`. Any AI tie-breaking, scorer jitter, or Insurgent-style chaos pulls from this RNG so replays reproduce exactly.

## Decision architecture: utility AI

Utility AI rather than a behavior tree. Reason: utility AI scales with new actions cleanly (just add a scorer) and produces less robotic behavior.

For each potential action (build a unit, move a stack, launch an air strike, propose alliance, run a cyber op), the AI computes a utility score in [0,1]. It picks the highest-scoring actions up to a "decisions per tick" budget.

A scorer:
```
score(action) = baseDesirability(action)
              × personalityMultiplier(action.kind)
              × situationModifier(world, me)
              × memoryBias(memory, action.target)
```

For example, "launch air strike on player B's airbase province X":
- `baseDesirability` = 0.6 if I have aircraft within range and B's AA is below threshold, else 0.1
- `personalityMultiplier` for a Hawk = 1.5 on AirStrike, for an Isolationist = 0.3
- `situationModifier` = +0.2 if I have air supremacy in the theater, -0.3 if I'm losing ground
- `memoryBias` = +0.3 if B betrayed me, -0.4 if B is my ally

## The five personalities

### 🦅 Hawk (MVP)
- Multipliers: Attack ×1.5, AirStrike ×1.4, Build Military ×1.3, Diplomacy ×0.6
- Goal: project power and dominate by force
- Diplomacy: declares war easily on weaker neighbors; rarely proposes alliance
- Tells: forward airbases, large fighter wings, aggressive border deployments
- Doctrine pick: Firepower
- *"Peace through superior firepower."*

### 🏢 Industrialist
- Multipliers: Build Economy ×1.5, Diplomacy ×1.4, Attack ×0.6
- Goal: out-produce everyone; win on score or wear opponents down economically
- Diplomacy: signs trade agreements eagerly; honors deals; punishes betrayals via embargo
- Tells: tech parks, refineries, financial districts everywhere; light military
- Doctrine pick: Defense
- *"Why bomb them when you can sell them oil?"*

### 🛡️ Isolationist
- Multipliers: Build Defense ×1.7, AA Investment ×1.5, Attack ×0.4, Diplomacy ×1.0
- Goal: survive; let everyone else bleed each other
- Diplomacy: signs every non-aggression offer; never proposes alliance
- Tells: hardened bunkers and AA batteries on every border, fortress mentality
- Doctrine pick: Defense
- *"America First. The world is a distraction."*

### 🕵️ Schemer (MVP late-add)
- Multipliers: Cyber ×1.6, Espionage ×1.5, conventional Attack ×0.7, Diplomacy dynamic
- Goal: win through asymmetric tools — cyber, intel, special ops
- Diplomacy: pretends friendliness; backstabs at the perfect moment
- Tells: cyber operations centers, tech parks, surprisingly small standing army
- Doctrine pick: Maneuver
- *"The war is won before the first shot."*

### 💥 Insurgent (Wildcard)
- Multipliers: Random per game from a wide range; rerolls weekly
- Goal: chaos
- Diplomacy: declares wars without warning, signs peace just as randomly
- Tells: makes no sense, that's the point
- Doctrine pick: Random
- *"Watch this."*

We default each new AI player to a personality randomly, weighted toward Hawk in MVP because it creates the most action.

## AI memory

Each AI player keeps a small persisted state blob (table `AiMemory`, see `03-DATABASE-SCHEMA.md`):
- `lastBetrayedByPlayerIds` — long-memory grudge tracker
- `currentTargetPlayerId` — who I'm currently focused on
- `lastDiplomaticOfferTick` — rate-limit per target
- `attemptedAttacksFailed` — increments after losses, biases toward backing off
- `cyberAttacksLandedAgainstMe` — feeds counter-cyber posture (Phase 3)
- `airSupremacyTargets` — provinces where I've achieved air dominance
- `economicGoalMode` — "earlyExpansion" / "consolidate" / "lateGameRush"

Stored as JSON in a single column for now (schema-flexible while we tune); fields get promoted out if they ever need to be queried directly. Memory is loaded/saved alongside the rest of the tick transaction so it stays consistent with world state.

## What the AI considers each tick

For each AI player, in order:
1. **Crisis check** — am I losing a war, about to lose my capital, or under cyber siege? If yes, pivot to defense.
2. **Production** — set production sliders / queue units according to current goal mode and personality.
3. **Construction** — queue 1–2 buildings on highest-value provinces.
4. **Movement** — for each idle stack, score nearby targets (defend, attack, reposition) and pick the best.
5. **Air operations** — for each idle air unit, score: patrol-for-supremacy, escort, strike, recon. (Phase 1.5+.)
6. **Diplomacy** — once per N ticks, consider proposing/breaking treaties. (Phase 2.)
7. **Cyber** — Schemer prioritizes; others use opportunistically. (Phase 3.)
8. **Research** — pick the next tech aligned with goal mode. (Phase 2.)

Decisions per tick are budgeted (default 5). Prevents AI from spamming the order table and overwhelming humans with simultaneous attacks across 12 fronts.

## Difficulty tuning

Difficulty isn't a separate axis from personality. Instead it scales:
- **Resource bonus** — Easy: -25%, Normal: 0, Hard: +25%, Brutal: +50%
- **Decision quality** — Easy: 30% of decisions are deliberately suboptimal; Normal: 10%; Hard: 0%; Brutal: +1 decision per tick
- **Vision** — Easy: AI uses fog of war; Hard+: AI sees the whole map (yes, it cheats, but it makes single-player tougher)

Default for the friend group: Normal.

## Why not LLMs?

Tempting and we could optionally use one for spicy chat-room messages or news headlines later. For *decisions*:
- Latency — ~150ms tick budgets, LLMs don't fit
- Determinism — replays reproduce exactly, which requires the same input → same output. LLMs are not bit-deterministic across calls or versions.
- Cost — even free tiers eventually meter you at "every AI every minute" rate

Utility AI gives us 90% of the felt-intelligence at 0.1% of the complexity, and it slots cleanly into the deterministic-tick contract (see `07-GAME-LOOP.md`).

## Testing the AI

In `StarsAndSteel.Tests` a `SimulatedGame` harness runs N ticks of an AI-vs-AI game and asserts:
- No personality wins more than 60% of self-play matchups (rough balance)
- AIs don't get stuck (every personality issues at least 1 order per 5 ticks on average)
- AIs handle edge cases (no provinces left, no neighbors, surrounded, all aircraft destroyed)

Catches regressions when we tune scorers.
