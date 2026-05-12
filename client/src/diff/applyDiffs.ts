// Pure-ish reducers: take a WorldSnapshot, apply one tick event, return a new
// WorldSnapshot. We use shallow copies for changed slices and reuse references
// for the rest, so subsequent equality checks (e.g., in nanostores' computed)
// fast-path correctly.
//
// These functions are called by the hub layer (api/hub.ts handlers in main.ts)
// and also exported for unit testing in isolation.

import type {
  WorldSnapshot, ResourcesUpdated, UnitMoved, UnitDestroyed,
  ProvinceCaptured, BuildingCompleted, UnitBuilt,
} from '../types/api';

export function applyResourcesUpdated(
  world: WorldSnapshot, e: ResourcesUpdated,
): WorldSnapshot {
  // Only the calling player's own row carries resources.
  if (e.playerId !== world.me.playerId) return world;

  const r = world.me.resources;
  return {
    ...world,
    me: {
      ...world.me,
      resources: {
        money: r.money + e.moneyDelta,
        oil: r.oil + e.oilDelta,
        steel: r.steel + e.steelDelta,
        electronics: r.electronics + e.electronicsDelta,
        food: r.food + e.foodDelta,
        manpower: r.manpower + e.manpowerDelta,
      },
    },
  };
}

export function applyUnitMoved(world: WorldSnapshot, e: UnitMoved): WorldSnapshot {
  // Update our own units only; visible enemy units come from re-snapshot
  // because their visibility set may have changed too.
  const idx = world.myUnits.findIndex(u => u.id === e.unitId);
  if (idx < 0) return world;

  const moved = { ...world.myUnits[idx], locationProvinceId: e.toProvinceId };
  const next = world.myUnits.slice();
  next[idx] = moved;
  return { ...world, myUnits: next };
}

export function applyUnitDestroyed(world: WorldSnapshot, e: UnitDestroyed): WorldSnapshot {
  // Could be ours or an enemy's; check both lists.
  const myIdx = world.myUnits.findIndex(u => u.id === e.unitId);
  if (myIdx >= 0) {
    const next = world.myUnits.slice();
    next.splice(myIdx, 1);
    return { ...world, myUnits: next };
  }
  const enemyIdx = world.visibleEnemyUnits.findIndex(u => u.id === e.unitId);
  if (enemyIdx >= 0) {
    const next = world.visibleEnemyUnits.slice();
    next.splice(enemyIdx, 1);
    return { ...world, visibleEnemyUnits: next };
  }
  return world;
}

export function applyProvinceCaptured(
  world: WorldSnapshot, e: ProvinceCaptured,
): WorldSnapshot {
  const idx = world.provinces.findIndex(p => p.id === e.provinceId);
  if (idx < 0) return world;

  // Look up the new owner's color from the players[] roster, if known.
  const newOwner = world.players.find(p => p.playerId === e.toPlayerId);
  const captured = {
    ...world.provinces[idx],
    ownerPlayerId: e.toPlayerId,
    ownerColorHex: newOwner?.flagPrimaryHex ?? world.provinces[idx].ownerColorHex,
  };
  const next = world.provinces.slice();
  next[idx] = captured;
  return { ...world, provinces: next };
}

export function applyBuildingCompleted(
  world: WorldSnapshot, e: BuildingCompleted,
): WorldSnapshot {
  const idx = world.provinces.findIndex(p => p.id === e.provinceId);
  if (idx < 0) return world;
  if (!world.provinces[idx].visible) return world; // we wouldn't see it anyway

  const updated = {
    ...world.provinces[idx],
    buildings: [
      ...world.provinces[idx].buildings,
      { id: e.buildingId, type: String(e.type), level: e.level },
    ],
  };
  const next = world.provinces.slice();
  next[idx] = updated;
  return { ...world, provinces: next };
}

export function applyUnitBuilt(world: WorldSnapshot, e: UnitBuilt): WorldSnapshot {
  // Only add to MyUnits when the new unit belongs to us.
  if (e.ownerPlayerId !== world.me.playerId) return world;

  return {
    ...world,
    myUnits: [
      ...world.myUnits,
      {
        id: e.unitId,
        type: String(e.type),
        // Domain isn't carried on the event; the snapshot will fix this on
        // next /snapshot. Default to 'Ground' so HUDs that filter by domain
        // don't blow up; this is the worst case if the new unit is air/naval.
        domain: 'Ground',
        strength: e.strength,
        morale: 100,
        experience: 0,
        locationProvinceId: e.provinceId,
        isInTransit: false,
        transitFromProvinceId: null,
        transitToProvinceId: null,
        transitArrivalTick: null,
        // Phase 2b: new units are never embarked at build time; the snapshot
        // (or a future ParentChanged event) reconciles parentage if a wing is
        // immediately auto-loaded onto a carrier.
        parentUnitId: null,
      },
    ],
  };
}
