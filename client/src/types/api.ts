// TypeScript mirrors of the server's API DTOs. Keep these in sync with:
//   - StarsAndSteel.Core/Snapshots/WorldSnapshot.cs    (REST snapshot shape)
//   - StarsAndSteel.Api/Worlds/Dtos/*.cs               (world create/join/list)
//   - StarsAndSteel.Api/Auth/Dtos/*.cs                 (auth)
//   - StarsAndSteel.Api/Orders/Dtos/*.cs               (orders)
//   - StarsAndSteel.Api/Hubs/Dtos/TickEventDtos.cs     (hub events)
// Field naming follows ASP.NET's default System.Text.Json camelCase serializer.

// ---- Auth ----------------------------------------------------------------
export interface AuthResponse {
  userId: string;
  displayName: string;
  email: string;
  accessToken: string;
  accessTokenExpiresAt: string;
}

// ---- Worlds --------------------------------------------------------------
export interface WorldSummary {
  id: string;
  name: string;
  status: 'Lobby' | 'Active' | 'Paused' | 'Finished';
  currentTick: number;
  tickIntervalSeconds: number;
  createdAtUtc: string;
  playerCount: number;
  maxPlayers: number;
}

// ---- Snapshot (mirrors WorldSnapshot.cs) --------------------------------
export interface WorldSnapshot {
  worldId: string;
  name: string;
  status: string;
  currentTick: number;
  tickIntervalSeconds: number;
  nextTickDueUtc: string | null;
  me: SnapshotMe;
  players: SnapshotPlayerSummary[];
  provinces: SnapshotProvince[];
  myUnits: SnapshotMyUnit[];
  visibleEnemyUnits: SnapshotEnemyUnit[];
}

export interface SnapshotMe {
  playerId: string;
  nationName: string;
  flagPrimaryHex: string;
  flagSecondaryHex: string;
  resources: SnapshotResources;
  isAlive: boolean;
}

export interface SnapshotResources {
  money: number;
  oil: number;
  steel: number;
  electronics: number;
  food: number;
  manpower: number;
}

export interface SnapshotPlayerSummary {
  playerId: string;
  nationName: string;
  flagPrimaryHex: string;
  flagSecondaryHex: string;
  isAi: boolean;
  isAlive: boolean;
  ownedProvinceCount: number;
}

export interface SnapshotProvince {
  id: string;
  name: string;
  type: string;
  isCoastal: boolean;
  centerX: number;
  centerY: number;
  ownerPlayerId: string | null;
  ownerColorHex: string | null;
  visible: boolean;
  moraleLevel: number | null;
  garrisonStrength: number | null;
  buildings: SnapshotBuilding[];
  adjacentProvinceIds: string[];
}

export interface SnapshotBuilding {
  id: string;
  type: string;
  level: number;
}

export interface SnapshotMyUnit {
  id: string;
  type: string;
  domain: string;
  strength: number;
  morale: number;
  experience: number;
  locationProvinceId: string | null;
  isInTransit: boolean;
  transitFromProvinceId: string | null;
  transitToProvinceId: string | null;
  transitArrivalTick: number | null;
}

export interface SnapshotEnemyUnit {
  id: string;
  ownerPlayerId: string;
  type: string;
  domain: string;
  strength: number;
  locationProvinceId: string;
}

// ---- Order requests ------------------------------------------------------
export interface MoveOrderRequest {
  unitId: string;
  targetProvinceId: string;
}

export interface BuildBuildingRequest {
  provinceId: string;
  buildingType: string;
}

export interface BuildUnitRequest {
  provinceId: string;
  unitType: string;
  quantity: number;
}

// ---- Hub events (mirrors TickEventDtos.cs) ------------------------------
export interface TickAdvanced {
  tick: number;
  eventCount: number;
}

export interface ResourcesUpdated {
  tick: number;
  playerId: string;
  moneyDelta: number;
  oilDelta: number;
  steelDelta: number;
  electronicsDelta: number;
  foodDelta: number;
  manpowerDelta: number;
}

export interface UnitMoved {
  tick: number;
  unitId: string;
  ownerPlayerId: string;
  fromProvinceId: string;
  toProvinceId: string;
}

export interface UnitDestroyed {
  tick: number;
  unitId: string;
  ownerPlayerId: string;
  locationProvinceId: string | null;
  cause: string;
}

export interface ProvinceCaptured {
  tick: number;
  provinceId: string;
  fromPlayerId: string | null;
  toPlayerId: string;
}

export interface BuildingCompleted {
  tick: number;
  buildingId: string;
  ownerPlayerId: string;
  provinceId: string;
  type: string; // server enum-as-number; rendered via UnitType / BuildingType maps if needed
  level: number;
}

export interface UnitBuilt {
  tick: number;
  unitId: string;
  ownerPlayerId: string;
  provinceId: string;
  type: string;
  strength: number;
}

export interface CombatResolved {
  tick: number;
  provinceId: string;
  attackerPlayerId: string;
  defenderPlayerId: string;
  attackerStrengthLoss: number;
  defenderStrengthLoss: number;
  winnerPlayerId: string | null;
}

export interface AirStrikeResolved {
  tick: number;
  attackerUnitId: string;
  attackerPlayerId: string;
  targetProvinceId: string;
  attackerStrengthLoss: number;
  defenderStrengthLoss: number;
}

// ---- News (mirrors NewsItemDto + NewsPublished hub event) ---------------
// Severity/Category are serialized as their server-side string names because
// the API uses the string-enum JSON converter (per docs/02). Kept open as
// `string` so adding a new value server-side doesn't break the client build.
export interface NewsItem {
  id: string;
  tick: number;
  headline: string;
  body: string;
  severity: string; // 'Info' | 'Notable' | 'Breaking'
  category: string; // 'Combat' | 'Politics' | 'Economy' | ...
  relatedPlayerId: string | null;
}

export interface NewsPublished {
  tick: number;
  newsItemId: string;
  headline: string;
  body: string;
  severity: string;
  category: string;
  relatedPlayerId: string | null;
}

export interface VictoryAchieved {
  tick: number;
  winnerPlayerId: string;
  winnerNationName: string;
  ownedProvinceCount: number;
  totalProvinceCount: number;
}

export interface CoalitionVictoryAchieved {
  tick: number;
  winnerPlayerIds: string[];
  winnerNationNames: string[];
  ownedProvinceCount: number;
  totalProvinceCount: number;
}

export interface PlayerEliminated {
  tick: number;
  playerId: string;
  nationName: string;
}

// ---- Server-to-client method names (must match TickEventNames.cs) -------
export const HubEvents = {
  TickAdvanced: 'TickAdvanced',
  ResourcesUpdated: 'ResourcesUpdated',
  UnitMoved: 'UnitMoved',
  UnitDestroyed: 'UnitDestroyed',
  AirStrikeResolved: 'AirStrikeResolved',
  CombatResolved: 'CombatResolved',
  ProvinceCaptured: 'ProvinceCaptured',
  UnitBuilt: 'UnitBuilt',
  BuildingCompleted: 'BuildingCompleted',
  NewsPublished: 'NewsPublished',
  VictoryAchieved: 'VictoryAchieved',
  CoalitionVictoryAchieved: 'CoalitionVictoryAchieved',
  PlayerEliminated: 'PlayerEliminated',
  RelationChanged: 'RelationChanged',
  OfferReceived: 'OfferReceived',
  OfferResolved: 'OfferResolved',
} as const;

// ---- Diplomacy (mirrors DiplomacyDtos.cs + DiplomacyEventDtos.cs) -------
// Server enum-as-string converter sends these as strings.
export type DiplomaticStatus = 'Peace' | 'Allied' | 'NonAggression' | 'War' | 'TradeAgreement';
export type TreatyOfferKind = 'Peace' | 'NonAggression' | 'Alliance';
export type TreatyOfferStatus = 'Pending' | 'Accepted' | 'Rejected' | 'Revoked' | 'Expired';

export interface DiplomacyPlayer {
  playerId: string;
  nationName: string;
  flagPrimaryHex: string;
  flagSecondaryHex: string;
  isAi: boolean;
  isAlive: boolean;
}

export interface DiplomacyRelation {
  partyAPlayerId: string;
  partyBPlayerId: string;
  status: DiplomaticStatus;
  lastChangedAtTick: number;
}

export interface DiplomacyOffer {
  offerId: string;
  senderPlayerId: string;
  receiverPlayerId: string;
  kind: TreatyOfferKind;
  status: TreatyOfferStatus;
  proposedAtTick: number;
  expiresAtTick: number;
  resolvedAtTick: number | null;
}

export interface DiplomacyState {
  callerPlayerId: string;
  players: DiplomacyPlayer[];
  relations: DiplomacyRelation[];
  inbox: DiplomacyOffer[];
  outbox: DiplomacyOffer[];
}

// Hub event payloads
export interface RelationChanged {
  partyAPlayerId: string;
  partyBPlayerId: string;
  newStatus: DiplomaticStatus;
  atTick: number;
}

export interface OfferReceived {
  offerId: string;
  senderPlayerId: string;
  receiverPlayerId: string;
  kind: TreatyOfferKind;
  proposedAtTick: number;
  expiresAtTick: number;
}

export interface OfferResolved {
  offerId: string;
  senderPlayerId: string;
  receiverPlayerId: string;
  kind: TreatyOfferKind;
  status: TreatyOfferStatus;
  resolvedAtTick: number;
}
