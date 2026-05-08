# 08 — Frontend (TypeScript + Phaser.js)

The browser client has two visual layers stacked on each other:

1. **Phaser canvas (the map)** — provinces, units, animations, fog of war, click handling on the map.
2. **HTML/CSS overlay (the UI)** — top resource bar, side panels, modals, newspaper, diplomacy. DOM is way better than canvas for any UI that's mostly text + buttons.

They communicate via a shared TypeScript store.

## Folder layout (recap from `02-PROJECT-STRUCTURE.md`)

```
client/
├── src/
│   ├── main.ts              <-- bootstrap: auth, hub, phaser
│   ├── game/
│   │   ├── PhaserGame.ts    <-- Phaser.Game instance config
│   │   ├── scenes/
│   │   │   ├── BootScene.ts
│   │   │   ├── MapScene.ts
│   │   │   └── HudScene.ts
│   │   └── render/
│   ├── ui/                  <-- HTML overlay components
│   ├── net/                 <-- api.ts + hub.ts
│   ├── state/store.ts
│   └── styles/ui.css
└── index.html
```

## Phaser scenes

### `BootScene`
Loads assets: a province texture atlas, unit sprite sheet, font atlas, fog-of-war shader. Hands off to `MapScene` once done.

### `MapScene`
The main game scene. Responsibilities:
- Render province polygons (filled with owner color, stroked with border)
- Render unit sprites at province centers, with strength label
- Render the fog-of-war overlay (a translucent dark gradient over unseen tiles)
- Handle camera: pan with mouse drag, zoom with wheel, pinch on touch
- Handle province click → emit `province:clicked` event for the UI overlay to react

Province polygons come from a static JSON file shared by client and server:
```json
{
  "provinces": [
    {
      "id": "prov-001",
      "name": "Königsberg",
      "polygon": [[120,340],[180,330],[200,400],[140,420]],
      "center": [160,370]
    }
  ]
}
```

The polygon and adjacency data lives in `shared/map-data.json` at the repo root (see `02-PROJECT-STRUCTURE.md`). The server's `MapSeeder` and the client's `MapScene` both load this exact file. Vite is configured with an `@shared` alias so the client imports it as `import mapData from '@shared/map-data.json'`. Single source of truth — they cannot drift.

### `HudScene`
A persistent always-on-top Phaser scene for things that should follow the map but stay readable: hover tooltips, unit-selected highlights, animated combat puffs. Lighter than `MapScene` and only re-renders on relevant changes.

## HTML overlay (the UI)

A single `<div id="ui-root">` over the canvas. Components are tiny TypeScript modules that render templates and bind to the store.

We keep it framework-light — no React in MVP. Plain TS + DOM. The UI is mostly conditional panels:

- **Top bar (always visible)** — resources (with deltas per tick), current tick, my nation flag
- **Left panel** — province detail when one is selected
- **Right panel** — unit panel: my units, build queue
- **Bottom bar** — newspaper ticker (most recent headlines, click to expand)
- **Modals** — diplomacy, research, full newspaper, end-of-game

### Why no React?
We can add it later if the UI gets large. For MVP, the surface area is small enough that vanilla TS + a tiny custom render-on-state-change wrapper is faster than reaching for a framework. (And we control the bundle size, which matters for first load.)

## State store

A single mutable object guarded by a typed update API:

```ts
// state/store.ts
export interface WorldState {
  worldId: string;
  currentTick: number;
  me: Player;
  players: Map<string, Player>;
  provinces: Map<string, Province>;
  units: Map<string, Unit>;
  pendingOrders: Order[];
  newspaper: NewspaperItem[];
}

const listeners = new Set<(s: WorldState) => void>();
let state: WorldState;

export function subscribe(fn: (s: WorldState) => void) { listeners.add(fn); return () => listeners.delete(fn); }
export function getState() { return state; }
export function applyDiff(diff: TickDiff) {
  // mutate state from each event
  // notify listeners once
}
```

Phaser scenes and UI components both `subscribe`. When the store updates, they redraw the bits that depend on what changed.

## Networking

### REST wrapper (`net/api.ts`)
```ts
export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch('/api' + path, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });
  if (!res.ok) throw new ApiError(await res.text(), res.status);
  return res.json();
}
```

### SignalR wrapper (`net/hub.ts`)
```ts
import * as signalR from '@microsoft/signalr';

export async function connectHub(token: string, worldId: string) {
  const conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/game', { accessTokenFactory: () => token })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  conn.on('TickAdvanced', e => store.applyDiff({ type: 'TickAdvanced', ...e }));
  conn.on('UnitMoved',     e => store.applyDiff({ type: 'UnitMoved', ...e }));
  conn.on('CombatResolved',e => store.applyDiff({ type: 'CombatResolved', ...e }));
  // ... rest of the events from 06-BACKEND-API.md

  conn.onreconnected(async () => {
    // refetch snapshot to be safe
    const snap = await api(`/worlds/${worldId}/snapshot`);
    store.hydrate(snap);
  });

  await conn.start();
  await conn.invoke('JoinWorld', worldId);
  return conn;
}
```

## Render loop & animation

Phaser tweens unit sprites between province centers when `UnitMoved` fires — visual smoothing of a discrete tick step. Combat events trigger a particle burst (`HudScene`). Newspaper items slide in from the right.

We deliberately avoid per-frame logic. Everything is event-driven: state changes → minimum redraw. This keeps the client cheap and predictable.

## Draft orders (UX persistence)

A "draft order" is one a player has started building in the UI but not yet submitted (e.g., they clicked their tank, opened the move overlay, but didn't pick a target). Drafts live in `localStorage` keyed by `pa.draft.{worldId}.{userId}` and are restored when the page reloads:

```ts
// state/drafts.ts
export interface DraftOrder {
  unitId: string;
  partial: Partial<OrderPayload>;
  createdAtMs: number;
}

export function saveDraft(d: DraftOrder) { /* localStorage.setItem(...) */ }
export function loadDrafts(): DraftOrder[] { /* localStorage.getItem(...) */ }
export function clearDraft(unitId: string) { /* ... */ }
```

Drafts are pure UI state — the server never sees them. They expire after 24 hours so a stale draft doesn't haunt you forever.

## Build & dev

```
cd client
npm install
npm run dev      # Vite dev server on :5173, proxying /api and /hubs to :5000
npm run build    # outputs dist/, served by ASP.NET Core in prod
```

In `Program.cs` (server) we add `app.UseStaticFiles()` pointing at the client `dist/`, plus a SPA fallback so `/` → `index.html` and the client router handles the rest. Or just keep it dead simple: one route `/play` returns the index.html.

## Accessibility & UX notes

- Color-blind safe palette for nation colors (we'll use Okabe-Ito).
- Every important UI also has a textual representation (a list of provinces, not just the map).
- Keyboard shortcuts: `M` toggle map view, `N` newspaper, `D` diplomacy, `1–9` jump to my provinces.
- A first-game tutorial overlay walks the player through their first move and build order.
