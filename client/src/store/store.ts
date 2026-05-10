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
import type {
  AuthResponse, WorldSnapshot, SnapshotProvince, NewsItem,
  DiplomacyState, DiplomaticStatus, DiplomacyOffer,
  RelationChanged, OfferReceived, OfferResolved,
} from '../types/api';

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

// Bounded ring of cable-news headlines. Newest-first so the ticker UI just
// renders [0..n]. We keep at most NEWS_CAP entries to bound memory; the REST
// endpoint can re-backfill if the user wants history.
export const NEWS_CAP = 50;
export const $news = atom<NewsItem[]>([]);

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
    $news.set([]);
    $diplomacy.set(null);
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

/** Push a new headline to the front of the news ring, deduping by id and capping length. */
export function pushNews(item: NewsItem) {
  const cur = $news.get();
  if (cur.some(n => n.id === item.id)) return;
  const next = [item, ...cur];
  if (next.length > NEWS_CAP) next.length = NEWS_CAP;
  $news.set(next);
}

/** Replace the news ring (used on snapshot load / hub reconnect backfill). */
export function setNews(items: NewsItem[]) {
  // Server returns ascending by tick; ticker wants newest-first.
  const sorted = items.slice().sort((a, b) => b.tick - a.tick);
  if (sorted.length > NEWS_CAP) sorted.length = NEWS_CAP;
  $news.set(sorted);
}

// Convenience: locate a province row by id without going through the store.
export function findProvince(world: WorldSnapshot, id: string): SnapshotProvince | undefined {
  return world.provinces.find(p => p.id === id);
}

// ---- Diplomacy state -----------------------------------------------------
// Mirrors GET /api/worlds/{id}/diplomacy. Mutated incrementally by hub events
// (RelationChanged / OfferReceived / OfferResolved) so the panel re-renders
// without a full re-fetch.
export const $diplomacy = atom<DiplomacyState | null>(null);

export function setDiplomacy(state: DiplomacyState | null) {
  $diplomacy.set(state);
}

/** Look up the canonical (PartyA<PartyB) relation between caller and another player. */
export function findRelation(state: DiplomacyState, otherPlayerId: string): DiplomaticStatus {
  const a = state.callerPlayerId, b = otherPlayerId;
  const [lo, hi] = a < b ? [a, b] : [b, a];
  const row = state.relations.find(r => r.partyAPlayerId === lo && r.partyBPlayerId === hi);
  return row?.status ?? 'Peace';
}

export function applyRelationChanged(state: DiplomacyState, e: RelationChanged): DiplomacyState {
  const others = state.relations.filter(r =>
    !(r.partyAPlayerId === e.partyAPlayerId && r.partyBPlayerId === e.partyBPlayerId));
  return {
    ...state,
    relations: [...others, {
      partyAPlayerId: e.partyAPlayerId,
      partyBPlayerId: e.partyBPlayerId,
      status: e.newStatus,
      lastChangedAtTick: e.atTick,
    }],
  };
}

export function applyOfferReceived(state: DiplomacyState, e: OfferReceived): DiplomacyState {
  const offer: DiplomacyOffer = {
    offerId: e.offerId,
    senderPlayerId: e.senderPlayerId,
    receiverPlayerId: e.receiverPlayerId,
    kind: e.kind,
    status: 'Pending',
    proposedAtTick: e.proposedAtTick,
    expiresAtTick: e.expiresAtTick,
    resolvedAtTick: null,
  };
  const me = state.callerPlayerId;
  if (e.receiverPlayerId === me) {
    if (state.inbox.some(o => o.offerId === e.offerId)) return state;
    return { ...state, inbox: [...state.inbox, offer] };
  }
  if (e.senderPlayerId === me) {
    if (state.outbox.some(o => o.offerId === e.offerId)) return state;
    return { ...state, outbox: [...state.outbox, offer] };
  }
  return state; // event for two other players, ignored locally
}

export function applyOfferResolved(state: DiplomacyState, e: OfferResolved): DiplomacyState {
  // Resolved means terminal — just remove from both inbox and outbox.
  return {
    ...state,
    inbox: state.inbox.filter(o => o.offerId !== e.offerId),
    outbox: state.outbox.filter(o => o.offerId !== e.offerId),
  };
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
