// Reactive global store. Built on nanostores: tiny atoms with subscribe()
// semantics, no React/Preact required, ~1 KB gzipped.
//
// Layout:
//   $auth       - logged-in user, JWT for SignalR  (null when logged out)
//   $world      - current WorldSnapshot            (null until /snapshot loads)
//   $selected   - selected province id             (null = nothing selected)
//   $draftOrder - in-progress order before submit  (null = no draft)
//   $tick       - last TickAdvanced barrier we observed
//
// Mutation is exclusively through the helper functions below so the diff layer
// (api/diffs.ts) and UI controllers don't have to know nanostores' API.

import { atom, computed } from 'nanostores';
import type { AuthResponse, WorldSnapshot, SnapshotProvince } from '../types/api';

export interface DraftOrder {
  kind: 'move' | 'build-unit' | 'build-building';
  // Move: which unit, target province
  unitId?: string;
  // Build-unit: type + quantity at province
  unitType?: string;
  quantity?: number;
  // Build-building: type at province
  buildingType?: string;
  // All orders
  provinceId?: string;
  targetProvinceId?: string;
}

export const $auth = atom<AuthResponse | null>(loadAuthFromStorage());
export const $world = atom<WorldSnapshot | null>(null);
export const $selectedProvinceId = atom<string | null>(null);
export const $draftOrder = atom<DraftOrder | null>(null);
export const $tick = atom<number>(0);

// Derived: the SnapshotProvince row currently selected, if any.
export const $selectedProvince = computed(
  [$world, $selectedProvinceId],
  (world, id) => {
    if (!world || !id) return null;
    return world.provinces.find(p => p.id === id) ?? null;
  },
);

// Derived: own units stationed at the selected province.
export const $unitsAtSelected = computed(
  [$world, $selectedProvinceId],
  (world, id) => {
    if (!world || !id) return [] as WorldSnapshot['myUnits'];
    return world.myUnits.filter(u => u.locationProvinceId === id && !u.isInTransit);
  },
);

// ---- mutators ------------------------------------------------------------

export function setAuth(auth: AuthResponse | null) {
  $auth.set(auth);
  if (auth) {
    sessionStorage.setItem('sas.auth', JSON.stringify(auth));
  } else {
    sessionStorage.removeItem('sas.auth');
    $world.set(null);
    $selectedProvinceId.set(null);
    $draftOrder.set(null);
  }
}

export function setWorld(snap: WorldSnapshot) {
  $world.set(snap);
  $tick.set(snap.currentTick);
}

/** Replace the current world with a mutated copy. Used by diff handlers. */
export function patchWorld(mutator: (draft: WorldSnapshot) => WorldSnapshot) {
  const cur = $world.get();
  if (!cur) return;
  $world.set(mutator(cur));
}

export function selectProvince(id: string | null) {
  $selectedProvinceId.set(id);
  $draftOrder.set(null); // selecting a new province cancels in-progress drafts
}

export function setDraftOrder(draft: DraftOrder | null) {
  $draftOrder.set(draft);
}

export function bumpTick(tick: number) {
  $tick.set(tick);
}

// Convenience: locate a province row by id without going through the store.
export function findProvince(world: WorldSnapshot, id: string): SnapshotProvince | undefined {
  return world.provinces.find(p => p.id === id);
}

// ---- session persistence -------------------------------------------------
// We persist the auth response (NOT the JWT secret-of-secrets, but the token
// itself and metadata) to sessionStorage so a page refresh during dev doesn't
// kick the user back to login. sessionStorage is per-tab; clearing it is an
// explicit logout.

function loadAuthFromStorage(): AuthResponse | null {
  try {
    const raw = sessionStorage.getItem('sas.auth');
    if (!raw) return null;
    const parsed = JSON.parse(raw) as AuthResponse;
    // Token expiry guard: if it's already expired, don't even try.
    if (Date.parse(parsed.accessTokenExpiresAt) <= Date.now()) {
      sessionStorage.removeItem('sas.auth');
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}
