# 06 — Backend API

Two surfaces: a REST API for snapshot reads and order writes, and a SignalR Hub for live state pushes.

## Conventions

- Base URL: `/api/`
- Auth: cookie-based for the page; JWT bearer for SignalR (see `10-AUTH-SECURITY.md`)
- All responses are JSON, camelCase, with `application/json`
- Error envelope: `{ "error": { "code": "INVALID_ORDER", "message": "..." } }`
- Validation errors → 400; auth → 401; ownership → 403; not-found → 404; conflicts → 409

## REST endpoints

### Auth
| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/auth/register` | Create account |
| POST | `/api/auth/login` | Login, returns JWT for SignalR + sets cookie |
| POST | `/api/auth/logout` | Clear cookie |
| GET | `/api/auth/me` | Current user info |

### Game world
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/worlds` | List joinable worlds |
| POST | `/api/worlds/{id}/join` | Join a world as a new player; pick nation/colors |
| GET | `/api/worlds/{id}/snapshot` | Full world snapshot for the calling player (filtered by fog) |
| GET | `/api/worlds/{id}/news?since={tick}` | Cable-news items since a given tick |
| GET | `/api/worlds/{id}/leaderboard` | Player scores |

### Provinces
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/worlds/{id}/provinces/{provinceId}` | Detail panel data (visible only to owner / fog-aware) |

### Units & orders
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/worlds/{id}/units/mine` | All units owned by the calling player |
| POST | `/api/worlds/{id}/orders/move` | Move a ground unit — `{ unitId, targetProvinceId }` |
| POST | `/api/worlds/{id}/orders/airstrike` | Air unit strikes target — `{ unitId, targetProvinceId }` |
| POST | `/api/worlds/{id}/orders/recon` | Recon drone sweep — `{ unitId, targetProvinceId }` |
| POST | `/api/worlds/{id}/orders/attack` | Ground attack — `{ unitId, targetProvinceId }` |
| POST | `/api/worlds/{id}/orders/build-unit` | `{ provinceId, unitType, quantity }` |
| POST | `/api/worlds/{id}/orders/build-building` | `{ provinceId, buildingType }` |
| DELETE | `/api/worlds/{id}/orders/{orderId}` | Cancel a pending order (refund partial) |

### Diplomacy (Phase 2)
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/worlds/{id}/diplomacy` | All my pairwise relations |
| POST | `/api/worlds/{id}/diplomacy/propose` | `{ toPlayerId, status }` |
| POST | `/api/worlds/{id}/diplomacy/respond` | `{ proposalId, accept }` |
| POST | `/api/worlds/{id}/diplomacy/declare-war` | `{ againstPlayerId }` |

### Cyber (Phase 3)
| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/worlds/{id}/cyber/launch` | `{ targetPlayerId, opType }` |

### Chat (Phase 2)
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/worlds/{id}/chat?since={messageId}` | Chat history |
| POST | `/api/worlds/{id}/chat` | Send a message |

## DTOs (shape sketch)

```ts
// What the client gets from /snapshot
interface WorldSnapshot {
  worldId: string;
  currentTick: number;
  tickIntervalSeconds: number;
  me: PlayerDto;
  players: PlayerSummaryDto[];          // all players, masked by fog
  provinces: ProvinceDto[];             // includes "lastSeenTick" if not currently visible
  myUnits: UnitDto[];                   // full detail
  visibleEnemyUnits: EnemyUnitDto[];    // partial detail
  newsItemsSinceLastVisit: NewsItemDto[];
}

interface PlayerDto {
  id: string;
  nationName: string;
  flagPrimaryHex: string;
  flagSecondaryHex: string;
  resources: {
    money: number;
    oil: number;
    steel: number;
    electronics: number;
    food: number;
    manpower: number;
  };
}

interface ProvinceDto {
  id: string;
  name: string;
  centerX: number;
  centerY: number;
  ownerPlayerId: string | null;
  ownerColorHex: string | null;
  type: ProvinceType;
  isCoastal: boolean;
  morale: number | null;                // null if foggy
  visible: boolean;
  lastSeenTick: number | null;
  buildings: BuildingDto[];
  garrisonStrength: number | null;
  adjacentProvinceIds: string[];
}

interface UnitDto {
  id: string;
  type: 'MechInfantry' | 'MainBattleTank' | 'AABattery' | 'CombatDrone' | 'MultiroleFighter' | string;
  domain: 'Ground' | 'Air' | 'Naval';
  strength: number;
  morale: number;
  experience: number;
  locationProvinceId: string | null;
  isInTransit: boolean;
}
```

## SignalR Hub: `GameHub` at `/hubs/game`

### Client → Server methods
| Method | Args | Purpose |
|--------|------|---------|
| `JoinWorld` | `worldId` | Add caller to the world's SignalR group |
| `LeaveWorld` | `worldId` | Remove from group |
| `Ping` | — | Keepalive |

### Server → Client events
| Event | Payload | When |
|-------|---------|------|
| `TickAdvanced` | `{ tick }` | Every successful tick |
| `ResourcesUpdated` | `{ playerId, money, oil, steel, electronics, food, manpower }` | Each tick, only to owner |
| `UnitMoved` | `{ unitId, fromProvinceId, toProvinceId, arrivalTick }` | Movement progresses |
| `UnitArrived` | `{ unitId, provinceId }` | Unit completes a leg |
| `UnitDestroyed` | `{ unitId, cause }` | Combat or attrition |
| `AirStrikeResolved` | `{ targetProvinceId, attacker, defenders, damage }` | After air phase |
| `CombatResolved` | `{ provinceId, attackerSummary, defenderSummary, casualties, winner }` | After ground phase |
| `ProvinceCaptured` | `{ provinceId, fromPlayerId, toPlayerId }` | Ownership change |
| `BuildingCompleted` | `{ buildingId, provinceId, type, level }` | Construction finishes |
| `NewsItem` | `NewsItemDto` | New cable-news headline |
| `DiplomacyChanged` | `{ fromPlayerId, toPlayerId, status }` | Phase 2 |
| `CyberOpResolved` | `{ targetPlayerId, opType, success, effect }` | Phase 3 |
| `ChatMessage` | `ChatMessageDto` | Phase 2 |
| `PlayerEliminated` | `{ playerId }` | Last province falls |
| `GameEnded` | `{ winnerPlayerId, reason }` | Game over |

### Hub semantics
- Group per world: clients are added to `world:{worldId}` on `JoinWorld`. The tick service broadcasts to that group.
- Per-player events (resources, fog-aware unit positions): sent to single connection IDs, looked up by `playerId`.
- Connection state: `OnConnectedAsync` records `(userId → connectionId)`; `OnDisconnectedAsync` removes.
- We tolerate brief disconnects and re-snapshot on reconnect.

## How the client uses both

```
Login → GET /api/worlds → POST /api/worlds/{id}/join
   ↓
GET /api/worlds/{id}/snapshot   ──► hydrate local store
   ↓
SignalR connect → JoinWorld(worldId)
   ↓
Server pushes events → store reconciles → UI re-renders
```

If SignalR drops and reconnects, the client re-fetches `/snapshot`. We assume diffs may have been missed.

## Order submission semantics (the cutoff rule)

When a player POSTs an order, the server:

1. Validates ownership and feasibility (rules below).
2. Acquires the per-world tick lock briefly to read `world.CurrentTick`.
3. Stamps the order with `IssuedAtTick = world.CurrentTick + 1` and `Status = Pending`.
4. Releases the lock and returns 200.

This guarantees orders submitted *while a tick is processing* are stamped for the *next* tick, never the one currently running. Combined with the tick processor's filter (`WHERE IssuedAtTick <= processingTick`), orders are processed in the tick named on them — no earlier, no later. See `07-GAME-LOOP.md` Concurrency & determinism for the full contract.

## Order validation rules (server-side, non-negotiable)

The order endpoints reject:
- Order on a unit you don't own → 403
- Move ground unit to a non-adjacent province (when not in transit) → 400
- Move into a hostile-owned province without war declaration → 400 (Phase 2)
- Air strike beyond unit's range → 400
- Air strike from an air unit not stationed at a province with an Air Base building → 400. (Note: every nation starts with an Air Base on the capital, so this rule never blocks Tick 1 actions — it just prevents you from using a captured airfield for strikes until you build one there.)
- Build on a province you don't own → 403
- Build a unit on a province lacking the required production building → 400
- Build with insufficient resources → 409
- Order while game is `Ended` → 409
- Order during quiet-hours window (Phase 2) → 409

If validation passes, the order row is inserted with `Status = Pending` and `IssuedAtTick = world.CurrentTick + 1`. The next tick processes it.
