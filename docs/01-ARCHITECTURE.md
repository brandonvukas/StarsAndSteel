# 01 — Architecture

## High-level diagram

```
+----------------------------------+              +-----------------------------------------+
|   Browser (TypeScript + Phaser)  |              |       ASP.NET Core 10 Backend            |
|                                  |   HTTPS      |                                         |
|   - Phaser.js 3 (map render)     |  <-------->  |   /api/*    REST endpoints              |
|   - HTML/CSS UI overlay          |   WebSocket  |   /hubs/game  SignalR Hub               |
|   - SignalR JS client            |  <-------->  |                                         |
|   - Vite dev server / static     |              |   GameTickService : BackgroundService   |
+----------------------------------+              |   EF Core 10                             |
                                                  +-----------+-----------------------------+
                                                              |
                                                              |  TCP 1433
                                                              v
                                                  +-----------------------------+
                                                  |  SQL Server (StarsAndSteelDb) |
                                                  +-----------------------------+
```

Three big pieces: a thin client, a fat server, and a database. The server is where every meaningful decision is made.

## Tech stack

| Layer | Choice | Why this and not the alternative |
|-------|--------|----------------------------------|
| Server framework | **ASP.NET Core 10** (LTS) | Free, fast, current LTS through Nov 2028. |
| Web protocol | REST + SignalR | REST for snapshots and orders; SignalR for live pushes. WebSockets directly would mean reinventing reconnect/backoff. |
| ORM | **EF Core 10** | First-class .NET ORM, code-first migrations, good SQL Server provider. Dapper is faster but you don't need that yet. |
| Database | **SQL Server** (Express is fine) | You said you have it. Postgres would be cheaper at scale but irrelevant here. |
| Auth | **ASP.NET Core Identity** + JWT bearer | Free, integrated, works with SignalR via access_token query param. |
| Background work | **BackgroundService** / `IHostedService` | Built into the runtime. No need for Hangfire / Quartz at MVP scale. |
| Logging | **Serilog** (free) → console + rolling file | Better DX than the default `ILogger`. |
| Validation | **FluentValidation** (free) | Cleaner than data annotations on DTOs. |
| Tests | **xUnit** + **FluentAssertions** + **Testcontainers** for SQL | All free; Testcontainers spins up a real SQL Server in Docker for integration tests. |
| Client lang | **TypeScript 5** | Type safety pays for itself in a state-heavy game UI. |
| Bundler | **Vite** | Fast HMR, zero-config for TS. |
| Renderer | **Phaser.js 3** (MIT) | 2D map, sprites, input — sweet spot for a province-based strategy game. |
| Client UI | **Plain HTML/CSS** layered over the Phaser canvas | DOM is way better than canvas for menus, lists, dialogs. We mix the two. |
| Client charts | **Chart.js** (MIT) | Resource graphs, leaderboards. |
| Client realtime | **@microsoft/signalr** (MIT) | Official client. |
| Icons / fonts | **Lucide** + **Google Fonts** | Free. |

## Architectural principles

### 1. Server is the source of truth
The client never decides anything important. It can show a unit moving smoothly between provinces with a tween, but the *fact* that the unit moved came from a server tick. If the server says the unit is in province X, it's in X — anything else on the client is animation.

### 2. Order-based input, not real-time control
Players don't drag units around like an RTS. They submit *orders* ("Infantry stack #42 from Königsberg → Warsaw"). Orders sit in the DB until the next tick consumes them. This makes the game tractable, fair across timezones, and resistant to lag.

### 3. The world ticks
A single `BackgroundService` runs continuously and processes a "tick" every 60 seconds (configurable). On each tick: produce resources, advance unit movement, resolve combat, run AI decisions, fire events, broadcast state. The whole game is "what happens at tick T?" repeated forever.

### 4. Push state via SignalR, not polling
On connect, the client gets a snapshot via REST (`GET /api/game/state`). After that, the server pushes diffs via SignalR (`UnitMoved`, `ProvinceCaptured`, `ResourceUpdate`, `CombatResolved`, `NewspaperItem`). The client merges deltas into a local store. No polling.

### 5. Deterministic ticks
The tick processor is pure-ish: given the world state at T and the orders submitted by T, it produces the world state at T+1. We can replay any window from order history + tick logs. Useful for debugging, useful for the "replay viewer" feature later.

### 6. One DB connection pool, no microservices
Single ASP.NET Core process talking to a single SQL Server database. Microservices are not free, and we are five players plus AI. We can split later if any of us ever becomes Mark Zuckerberg.

## What lives where

| Concern | Layer | Notes |
|---------|-------|-------|
| Identity / login | Server | ASP.NET Identity with cookie auth for the page, JWT for the SignalR hub |
| Game world snapshot | DB | EF Core entities |
| Pending orders | DB | Inserted by API, drained by tick service |
| Tick processing | Server (BackgroundService) | One process, one timer, one transaction per tick |
| Combat math | Server | See `04-GAME-MECHANICS.md` |
| AI decisions | Server | Run inside the tick (or one tick behind, see `09-AI-OPPONENTS.md`) |
| Map rendering | Client (Phaser) | Province polygons, unit sprites, fog of war |
| UI / menus | Client (HTML overlay) | Diplomacy panel, build menu, newspaper |
| State store | Client (TypeScript) | Single store, updated from REST snapshot + SignalR diffs |

## Diagram of a typical action

```
Player clicks "Move infantry → Warsaw"
        |
        v
Client: POST /api/orders/move  (REST)
        |
        v
Server: validate (does player own unit? is move legal?)
        |
        v
Server: insert row in [Orders] table  -> 200 OK
        |
        v
... time passes, up to 60 seconds ...
        |
        v
GameTickService wakes up
   - reads pending orders
   - applies movement: unit now in transit, ETA tick T+3
   - persists state
   - broadcasts UnitOrderAccepted via SignalR
        |
        v
Client: SignalR receives UnitOrderAccepted, updates local store, animates
```
