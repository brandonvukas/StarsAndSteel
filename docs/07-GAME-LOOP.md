# 07 — The Game Loop (Background Tick)

The whole game is a loop: every 60 seconds, a `BackgroundService` wakes up, processes one tick, and broadcasts the results. This is the heart of the project.

## `GameTickService`

Lives in `StarsAndSteel.Api/BackgroundServices/GameTickService.cs`. Inherits `BackgroundService`. Its job: run a tick on every active world at the world's configured interval.

Skeleton:

```csharp
public class GameTickService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<GameTickService> _logger;

    // Per-world locks prevent two overlapping ticks for the same world if a
    // tick ever takes longer than the poll interval.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _worldLocks = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Guid[] dueWorldIds;
                using (var scope = _services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
                    dueWorldIds = await db.GameWorlds
                        .Where(w => w.Status == GameStatus.Active &&
                                    w.NextTickDueUtc <= DateTime.UtcNow)
                        .Select(w => w.Id)
                        .ToArrayAsync(stoppingToken);
                }

                // Process due worlds in parallel — one slow world must not starve others.
                var tasks = dueWorldIds.Select(id => ProcessWorldAsync(id, stoppingToken));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tick loop failure — continuing");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task ProcessWorldAsync(Guid worldId, CancellationToken ct)
    {
        var gate = _worldLocks.GetOrAdd(worldId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) return; // a previous tick is still running for this world
        try
        {
            using var scope = _services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<TickProcessor>();

            var sw = Stopwatch.StartNew();
            var diff = await processor.ProcessOneTickAsync(worldId, ct);
            sw.Stop();

            if (sw.ElapsedMilliseconds > 1000)
                _logger.LogWarning("Tick for world {WorldId} took {Ms}ms (budget 150ms)", worldId, sw.ElapsedMilliseconds);

            await BroadcastDiffAsync(worldId, diff, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tick for world {WorldId} failed — skipping", worldId);
        }
        finally
        {
            gate.Release();
        }
    }
}
```

Two design choices worth calling out:

1. **Worlds tick in parallel.** Each due world runs on its own scope/transaction. One stuck world cannot starve the others.
2. **Per-world re-entrancy lock.** If a tick somehow takes longer than the 1-second poll interval, the next loop iteration sees the world is "due" again — the semaphore prevents double-processing.

We poll every second instead of sleeping for the tick interval because different worlds may have different intervals, and we want to react to a world being created mid-run.

## What happens in a single tick

`TickProcessor.ProcessOneTickAsync(worldId)` runs these steps in order, all inside a single DB transaction:

```
0. SnapshotPhase            — load world with eager-loads; capture `processingTick = world.CurrentTick + 1`
                              and the per-world deterministic RNG (seeded from world.RngState)
1. AiTurnStep               — each AI player decides orders based on the PRE-tick state and enqueues them
                              with IssuedAtTick = processingTick (so they are processed THIS tick alongside
                              any human orders submitted before the cutoff). Determinism: AI sees the same
                              state every replay, RNG is seeded.
2. OrderCutoff              — read pending orders WHERE IssuedAtTick <= processingTick AND Status = Pending.
                              Orders submitted by humans after this point will carry IssuedAtTick = processingTick + 1
                              and process next tick. Enforced by the API stamping IssuedAtTick from
                              world.CurrentTick + 1 only when no tick is in progress (per-world lock).
3. ResourceProductionStep   — every owned province produces, applies building bonuses, adds to player pools
4. AttritionStep            — units in low-supply or hostile territory lose strength/morale
5. MovementStep             — drains pending Move orders, advances in-transit units, computes arrivals
6. AirStrikeStep            — air units launch ordered strikes (resolves vs defending fighters + AA)
7. CombatStep               — ground combat at provinces where attackers arrived
8. ConstructionStep         — advance build orders, complete units / buildings when ready
9. CyberStep                — (Phase 3) resolve queued cyber operations
10. EventStep               — random world events; morale recovery; eliminations
11. NewsStep                — generate cable-news items from this tick's outcomes
12. PersistRngState         — write back the advanced RNG state to world.RngState
13. DiffSerializationStep   — produce the SignalR diff payload
14. AdvanceTick             — world.CurrentTick = processingTick; world.NextTickDueUtc = now + interval;
                              bump world.RowVersion (concurrency token)
```

**Why AI runs first:** for replay determinism, every action processed in tick T must be derivable from the world state at the start of T plus the persisted RNG. If AI ran *after* combat in the same tick, its decisions would depend on mid-tick state that isn't part of any persisted snapshot, breaking replay. By running AI first against the pre-tick state, we get a clean property: **state(T+1) = f(state(T), pendingOrders, rngState(T))**.

The trade-off: AI reacts one tick later than humans to combat outcomes (it sees combat at T+1 when deciding orders for T+2). At a 60s tick this is invisible.

Each step is pure-ish: takes the world snapshot from EF, mutates entities (which EF tracks), and emits a list of "events" (UnitMoved, AirStrikeResolved, ProvinceCaptured, etc.).

After all steps, we `SaveChanges` once. Then we publish events to SignalR. Atomicity matters: if step 7 throws, we roll back and skip the tick rather than corrupt the world.

## Per-step detail (MVP)

### 1. ResourceProductionStep
```
for each player in world:
  for each province they own:
    pool += baseOutput * sumOf(building bonuses) * moraleMultiplier
  player.Money/Oil/Steel/Electronics/Food/Manpower += pool
emit ResourcesUpdated (per player)
```

### 2. AttritionStep
```
for each unit in transit through hostile territory:
  if no supply line back to friendly territory:
    unit.Strength -= 2%
    unit.Morale -= 5
```

### 3. MovementStep
```
for each pending Move order:
  if unit can move this tick:
    advance along adjacency path
    if arrived: unit.IsInTransit = false; emit UnitArrived
    else: emit UnitMoved with new ETA
```

### 4. AirStrikeStep
```
for each pending AirStrike order:
  attackers = ordered air units within range
  defenders = enemy fighters at target + AA batteries at target
  resolve interception (defenders shoot attackers per matrix in 04-GAME-MECHANICS)
  surviving attackers damage ground units / buildings at target
  emit AirStrikeResolved
```

### 5. CombatStep
```
for each province where >1 player has ground units present this tick:
  apply pre-damage from any AirStrike resolved this tick
  resolve via CombatResolver (combined-arms formula)
  apply casualties, emit CombatResolved
  if attacker wins and defender empty: change ownership; emit ProvinceCaptured
```

### 6. ConstructionStep
```
for each in-progress build order:
  ticks_remaining -= 1
  if zero: instantiate Unit or Building; emit BuildingCompleted / UnitBuilt
```

### 8. AiTurnStep
For each AI player, ask `IAiPersonality.DecideOrders(worldSnapshot, rng)` for orders to enqueue. Inserted with `IssuedAtTick = processingTick` so they process *this* tick (the AI is operating on pre-tick state, same as humans who submitted before the order cutoff). The shared per-world RNG is passed in so any randomness is replay-deterministic.

Note: this step is numbered 1 in the canonical order above (it runs first). It's described here under its old number for readers who skim the per-step section.

### 9. EventStep
- Roll for random events (Phase 3+).
- Increment morale recovery: each owned, non-besieged province +1 morale (capped at 100).
- Check elimination: if a player has 0 provinces for 3 ticks, mark `IsAlive = false`, emit `PlayerEliminated`.
- Check victory; if met, mark world `Ended`, emit `GameEnded`.

### 10. NewsStep
Templated headlines from `StarsAndSteel.Game/News/templates.json`:
```json
{
  "ProvinceCaptured": [
    "BREAKING: {attacker} forces seize {province} — {defender} in retreat",
    "{province} FALLS — {defender} command issues no comment",
    "STARS RISING OVER {province}: {attacker} flag raised at dawn"
  ],
  "AirStrikeResolved.AttackerSuccess": [
    "{attacker} drone swarm strikes {province} — heavy casualties reported",
    "Pentagon source: stealth bombers crossed into {province} airspace overnight"
  ],
  "PlayerEliminated": [
    "GOVERNMENT OF {nation} CAPITULATES",
    "{nation} REMOVED FROM THE MAP — analysts assess regional power vacuum"
  ]
}
```
Pick a template per event using the per-world RNG (seeded from `world.RngState` at the start of the tick) so replays reproduce exactly.

### 11. DiffSerializationStep
Bundle all events from this tick into a `TickDiff`. The Hub broadcasts each event individually so clients can react granularly.

### 12. AdvanceTick
Increment counter, set next due time, save, commit transaction, broadcast `TickAdvanced`.

## Performance budget

Even at our scale (5–12 players, ~80 provinces), one tick should resolve in <150ms. The DB round trip dominates; everything else is in-memory math. Two practices keep us honest:
- Eager-load the world for the tick: `db.GameWorlds.Include(...).Include(...)` once at the top, mutate in-memory, save once.
- Avoid N+1: fetch all units / provinces / orders in batched queries.

## Failure modes & recovery

- **DB write fails mid-tick** → transaction rolls back; tick log marked failed; we re-attempt next loop cycle. World stays consistent.
- **One world's tick throws** → log, skip, continue with other worlds.
- **Service restart** → on startup, the loop simply resumes; `NextTickDueUtc` may already be in the past, in which case we tick immediately.
- **Tick takes >60s** → we just run the next one as soon as we can.

## Tick logging (cheap but invaluable)

For each tick we write a single `TickLog` row:
- WorldId, Tick, StartUtc, EndUtc, EventCount, ErrorIfAny

Two reasons: (1) easy to see if the tick service is healthy; (2) feeds the future replay viewer.

## Concurrency & determinism contract

The tick is the only writer that mutates game state. Everything else (order endpoints, chat, diplomacy proposals) only inserts rows that are *consumed* by the tick. This gives us a simple contract:

- **Order submission cutoff.** When a player submits an order, the API stamps `IssuedAtTick = world.CurrentTick + 1` (the tick that will process next). The order endpoint takes the same per-world `SemaphoreSlim` used by the tick service for a brief read of `CurrentTick`, then releases — so an order submitted *while a tick is processing* is guaranteed to land in the *next* tick, not the one currently running.
- **Optimistic concurrency on `GameWorld`.** `GameWorld.RowVersion` (rowversion / SQL Server timestamp) is bumped on every `AdvanceTick`. The tick processor reads the world with its row version and uses it as a concurrency token on `SaveChanges`. If two ticks ever race for the same world (they shouldn't — see the per-world lock — but belt and suspenders), the second one throws `DbUpdateConcurrencyException` and aborts cleanly.
- **Deterministic RNG.** Each `GameWorld` has an `RngState` (long). At the start of each tick we seed a `Random` (or a simple linear-congruential generator if we want to be portable) from this state. All randomness in the tick — combat rolls, news template selection, world events, AI tie-breaking — pulls from this one RNG. At the end of the tick we serialize the advanced state back to `RngState`. This makes replays bit-exact: given the same starting state, persisted RNG, and the same sequence of orders, every tick produces the same outcome.
- **Replay invariant.** `state(T+1) = f(state(T), ordersIssuedAtT+1, rngState(T))`. Nothing outside this triple can affect the result.
