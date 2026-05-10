// Thin wrappers around fetch() for the REST API surface. Cookie auth is
// automatic in the browser (the server sets the auth cookie on /login) so we
// don't have to attach anything except for routes that explicitly require JSON.

import type {
  AuthResponse, WorldSummary, WorldSnapshot, NewsItem,
  MoveOrderRequest, BuildBuildingRequest, BuildUnitRequest,
  DiplomacyState, TreatyOfferKind,
} from '../types/api';

class HttpError extends Error {
  readonly status: number;
  readonly body: unknown;
  constructor(status: number, body: unknown) {
    super(`HTTP ${status}`);
    this.status = status;
    this.body = body;
  }
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const init: RequestInit = {
    method,
    credentials: 'include', // include the auth cookie set by /login
  };
  if (body !== undefined) {
    init.headers = { 'Content-Type': 'application/json' };
    init.body = JSON.stringify(body);
  }

  const res = await fetch(path, init);
  // 204 / empty bodies tolerated.
  let parsed: unknown = null;
  const text = await res.text();
  if (text.length > 0) {
    try { parsed = JSON.parse(text); } catch { parsed = text; }
  }
  if (!res.ok) throw new HttpError(res.status, parsed);
  return parsed as T;
}

// ---- Auth ----------------------------------------------------------------
export const register = (email: string, displayName: string, password: string) =>
  call<void>('POST', '/api/auth/register', { email, displayName, password });

// The server accepts either email or display name in a single field; the client
// sends whatever the user typed and lets the server resolve it.
export const login = (emailOrDisplayName: string, password: string) =>
  call<AuthResponse>('POST', '/api/auth/login', { emailOrDisplayName, password });

export const logout = () =>
  call<void>('POST', '/api/auth/logout');

// ---- Worlds --------------------------------------------------------------
export const listWorlds = () =>
  call<WorldSummary[]>('GET', '/api/worlds');

export const createWorld = (name: string, mapSeed = 42) =>
  call<WorldSummary>('POST', '/api/worlds', { name, mapSeed });

export const joinWorld = (worldId: string, nationName: string,
                          flagPrimaryHex: string, flagSecondaryHex: string) =>
  call<void>('POST', `/api/worlds/${worldId}/join`,
    { nationName, flagPrimaryHex, flagSecondaryHex });

export const getSnapshot = (worldId: string) =>
  call<WorldSnapshot>('GET', `/api/worlds/${worldId}/snapshot`);

// Reconnect backfill: fetch headlines newer than `since` (default 0 = all,
// capped at 200 server-side). Used by the news ticker after a hub reconnect.
export const getNews = (worldId: string, since = 0) =>
  call<NewsItem[]>('GET', `/api/worlds/${worldId}/news?since=${since}`);

// ---- Orders --------------------------------------------------------------
export const orderMove = (worldId: string, req: MoveOrderRequest) =>
  call<{ orderId: string }>('POST', `/api/worlds/${worldId}/orders/move`, req);

export const orderBuildBuilding = (worldId: string, req: BuildBuildingRequest) =>
  call<{ orderId: string; ticksRemaining: number }>(
    'POST', `/api/worlds/${worldId}/orders/build-building`, req);

export const orderBuildUnit = (worldId: string, req: BuildUnitRequest) =>
  call<{ orderId: string; ticksRemaining: number }>(
    'POST', `/api/worlds/${worldId}/orders/build-unit`, req);

// ---- Diplomacy -----------------------------------------------------------
export const getDiplomacy = (worldId: string) =>
  call<DiplomacyState>('GET', `/api/worlds/${worldId}/diplomacy`);

export const declareWar = (worldId: string, targetPlayerId: string) =>
  call<unknown>('POST', `/api/worlds/${worldId}/diplomacy/declare-war`, { targetPlayerId });

export const proposeTreaty = (worldId: string, receiverPlayerId: string, kind: TreatyOfferKind) =>
  call<unknown>('POST', `/api/worlds/${worldId}/diplomacy/propose`, { receiverPlayerId, kind });

export const acceptOffer = (worldId: string, offerId: string) =>
  call<unknown>('POST', `/api/worlds/${worldId}/diplomacy/accept`, { offerId });

export const rejectOffer = (worldId: string, offerId: string) =>
  call<unknown>('POST', `/api/worlds/${worldId}/diplomacy/reject`, { offerId });

export const revokeOffer = (worldId: string, offerId: string) =>
  call<unknown>('POST', `/api/worlds/${worldId}/diplomacy/revoke`, { offerId });

export { HttpError };
