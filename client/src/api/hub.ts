// SignalR client wrapper. One HubConnection per logged-in user; reconnects
// automatically. Per docs/06: on reconnect, callers should re-fetch /snapshot
// because diffs may have been missed.

import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { HubEvents } from '../types/api';
import type {
  TickAdvanced, ResourcesUpdated, UnitMoved, UnitDestroyed,
  ProvinceCaptured, BuildingCompleted, UnitBuilt,
  CombatResolved, AirStrikeResolved, NewsPublished,
  RelationChanged, OfferReceived, OfferResolved,
} from '../types/api';

export interface HubHandlers {
  onTickAdvanced?: (e: TickAdvanced) => void;
  onResourcesUpdated?: (e: ResourcesUpdated) => void;
  onUnitMoved?: (e: UnitMoved) => void;
  onUnitDestroyed?: (e: UnitDestroyed) => void;
  onProvinceCaptured?: (e: ProvinceCaptured) => void;
  onBuildingCompleted?: (e: BuildingCompleted) => void;
  onUnitBuilt?: (e: UnitBuilt) => void;
  onCombatResolved?: (e: CombatResolved) => void;
  onAirStrikeResolved?: (e: AirStrikeResolved) => void;
  onNewsPublished?: (e: NewsPublished) => void;
  onRelationChanged?: (e: RelationChanged) => void;
  onOfferReceived?: (e: OfferReceived) => void;
  onOfferResolved?: (e: OfferResolved) => void;
  /** Fired after the connection drops and the server snapshot may be stale. */
  onReconnected?: () => void;
}

export class GameHubClient {
  private connection: HubConnection | null = null;
  private readonly accessToken: () => string | null;
  private readonly handlers: HubHandlers;

  constructor(accessToken: () => string | null, handlers: HubHandlers) {
    this.accessToken = accessToken;
    this.handlers = handlers;
  }

  async connect(): Promise<void> {
    if (this.connection) return; // idempotent

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/game', {
        // SignalR appends ?access_token=... when this returns a string; the
        // server's JwtBearer.OnMessageReceived handler picks it up because the
        // path starts with /hubs.
        accessTokenFactory: () => this.accessToken() ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.bindHandlers();
    await this.connection.start();
  }

  async joinWorld(worldId: string): Promise<void> {
    if (!this.connection) throw new Error('Hub not connected');
    await this.connection.invoke('JoinWorld', worldId);
  }

  async leaveWorld(worldId: string): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('LeaveWorld', worldId);
  }

  async disconnect(): Promise<void> {
    if (!this.connection) return;
    await this.connection.stop();
    this.connection = null;
  }

  private bindHandlers() {
    const c = this.connection!;
    const h = this.handlers;

    if (h.onTickAdvanced)       c.on(HubEvents.TickAdvanced, h.onTickAdvanced);
    if (h.onResourcesUpdated)   c.on(HubEvents.ResourcesUpdated, h.onResourcesUpdated);
    if (h.onUnitMoved)          c.on(HubEvents.UnitMoved, h.onUnitMoved);
    if (h.onUnitDestroyed)      c.on(HubEvents.UnitDestroyed, h.onUnitDestroyed);
    if (h.onProvinceCaptured)   c.on(HubEvents.ProvinceCaptured, h.onProvinceCaptured);
    if (h.onBuildingCompleted)  c.on(HubEvents.BuildingCompleted, h.onBuildingCompleted);
    if (h.onUnitBuilt)          c.on(HubEvents.UnitBuilt, h.onUnitBuilt);
    if (h.onCombatResolved)     c.on(HubEvents.CombatResolved, h.onCombatResolved);
    if (h.onAirStrikeResolved)  c.on(HubEvents.AirStrikeResolved, h.onAirStrikeResolved);
    if (h.onNewsPublished)      c.on(HubEvents.NewsPublished, h.onNewsPublished);
    if (h.onRelationChanged)    c.on(HubEvents.RelationChanged, h.onRelationChanged);
    if (h.onOfferReceived)      c.on(HubEvents.OfferReceived, h.onOfferReceived);
    if (h.onOfferResolved)      c.on(HubEvents.OfferResolved, h.onOfferResolved);

    if (h.onReconnected) c.onreconnected(h.onReconnected);
  }
}
