// Thin wrappers around fetch() for the REST API surface. Cookie auth is
// automatic in the browser (the server sets the auth cookie on /login) so we
// don't have to attach anything except for routes that explicitly require JSON.

import type {
  AuthResponse, WorldSummary, WorldSnapshot, NewsItem,
  MoveOrderRequest, BuildBuildingRequest, BuildUnitRequest, MissileLaunchRequest,
  DiplomacyState, TreatyOfferKind,
  ResearchState,
  ChatMessageDto, SendChatMessageRequest, SendChatMessageResponse,
  MeResponse, UpdateQuietHoursRequest,
  GeneralDto, RecruitGeneralRequest, AssignGeneralRequest,
  GeneralRecruited, GeneralAssigned,
  SabotageOrderRequest, CyberAttackOrderRequest, CyberAttackOrderAccepted,
  WonderRow,
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

// ---- Account / settings (Phase 2L) ---------------------------------------
export const getMe = () =>
  call<MeResponse>('GET', '/api/auth/me');

export const updateQuietHours = (req: UpdateQuietHoursRequest) =>
  call<MeResponse>('PUT', '/api/auth/me/quiet-hours', req);

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

export const orderLaunchMissile = (worldId: string, req: MissileLaunchRequest) =>
  call<{ orderId: string }>('POST', `/api/worlds/${worldId}/orders/launch-missile`, req);

// Phase 4a: SF sabotage. Server validates SpecialForces unit, ownership, and
// adjacency to target. Resolves at next tick (one random building destroyed +
// 200 strength casualties to the SF stack + -10 morale on target province).
export const orderSabotage = (worldId: string, req: SabotageOrderRequest) =>
  call<{ orderId: string; unitId: string; orderType: string; targetProvinceId: string; issuedAtTick: number }>(
    'POST', `/api/worlds/${worldId}/orders/sabotage`, req);

// Phase 4a: cyber attack. Server validates CyberOperationsCenter at launch
// province, cyber_warfare tech unlocked, and target ≠ launch. Money + electronics
// debited up front; 50/50 effect (DrainMoney / SlowResearch) rolled at tick.
export const orderCyberAttack = (worldId: string, req: CyberAttackOrderRequest) =>
  call<CyberAttackOrderAccepted>(
    'POST', `/api/worlds/${worldId}/orders/cyber-attack`, req);

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

// ---- Research ------------------------------------------------------------
export const getResearch = (worldId: string) =>
  call<ResearchState>('GET', `/api/worlds/${worldId}/research`);

export const startResearch = (worldId: string, techId: string) =>
  call<{ techId: string; ticksToResearch: number }>(
    'POST', `/api/worlds/${worldId}/research/start`, { techId });

// ---- Generals (Phase 4a) -------------------------------------------------
// Caller's generals only — server scopes by JWT subject.
export const getGenerals = (worldId: string) =>
  call<GeneralDto[]>('GET', `/api/worlds/${worldId}/generals`);

// Recruit a single general (one-per-player MVP cap, $2,500). Newly-recruited
// general is unassigned; assign separately to apply the +15% defender bonus.
export const recruitGeneral = (worldId: string, req: RecruitGeneralRequest) =>
  call<GeneralRecruited>('POST', `/api/worlds/${worldId}/generals`, req);

// Reassign (no cooldown) — target province must be owned by caller.
export const assignGeneral = (worldId: string, generalId: string, req: AssignGeneralRequest) =>
  call<GeneralAssigned>('POST', `/api/worlds/${worldId}/generals/${generalId}/assign`, req);

// ---- Chat ----------------------------------------------------------------
export const getChatHistory = (worldId: string, take = 50) =>
  call<ChatMessageDto[]>('GET', `/api/worlds/${worldId}/chat?take=${take}`);

export const sendChatMessage = (worldId: string, req: SendChatMessageRequest) =>
  call<SendChatMessageResponse>('POST', `/api/worlds/${worldId}/chat/send`, req);

// ---- Wonders (Phase 4b1) -------------------------------------------------
// Global one-per-game catalogue + per-world status. Wonders are built via the
// regular orderBuildBuilding endpoint with one of the wonder BuildingType
// values; this read-only endpoint just powers the Wonders panel.
export const getWonders = (worldId: string) =>
  call<WonderRow[]>('GET', `/api/worlds/${worldId}/wonders`);

export { HttpError };
