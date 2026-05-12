// Side panel: shows the selected province and exposes the three MVP draft
// orders (move / build-unit / build-building). The panel is rebuilt whenever
// the selected province or the world snapshot changes.

import { $selectedProvince, $unitsAtSelected, $world } from '../store/store';
import { orderMove, orderBuildBuilding, orderBuildUnit, HttpError } from '../api/rest';
import type { SnapshotProvince, WorldSnapshot } from '../types/api';

const BUILDABLE_BUILDINGS = [
  'RecruitmentCenter', 'MilitaryBase', 'AirBase',
  'SteelMill', 'Refinery', 'FinancialDistrict',
] as const;

// Coastal-only buildings (filtered into the build menu when the selected
// province has IsCoastal = true). Phase 2I.
const COASTAL_ONLY_BUILDINGS = ['NavalYard'] as const;

const BUILDABLE_UNITS = [
  // Ground (per docs/04 §"Unit catalogue")
  'MechInfantry', 'NationalGuard', 'SpecialForces',
  'MainBattleTank', 'MobileArtillery', 'AABattery',
  // Air. StealthBomber is intentionally excluded — Phase 3 gates it behind research.
  'ReconDrone', 'CombatDrone', 'AttackHelicopter',
  'MultiroleFighter', 'StrategicBomber',
  // Naval (Phase 2I MVP-lite). Only buildable at coastal provinces with a NavalYard;
  // server enforces RequiredBuilding=NavalYard. We surface both regardless of coast
  // so the dropdown is stable; server will reject without a NavalYard.
  'Frigate', 'Destroyer',
  // Naval Aviation (Phase 2b). AircraftCarrier needs NavalYard. CarrierAirWing
  // additionally needs a friendly carrier docked at the build province with a
  // free wing slot — server enforces this and returns NoCarrierWithSpareCapacity
  // if not satisfied.
  'AircraftCarrier', 'CarrierAirWing',
] as const;

export function mountProvincePanel(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('province-panel');

  function rerender() {
    const province = $selectedProvince.get();
    const world = $world.get();
    container.innerHTML = '';
    if (!province || !world) {
      container.innerHTML = '<p class="hint">Click a province on the map to inspect it.</p>';
      return;
    }
    container.appendChild(renderHeader(province));
    container.appendChild(renderDetails(province));
    container.appendChild(renderOrderForms(world, province));
  }

  $selectedProvince.subscribe(rerender);
  // Re-render when units arrive/move so the move-from-here selector stays current.
  $unitsAtSelected.subscribe(rerender);
  $world.subscribe(rerender);
  rerender();
}

function renderHeader(p: SnapshotProvince): HTMLElement {
  const h = document.createElement('div');
  h.className = 'pp-header';
  const swatch = p.ownerColorHex
    ? `<span class="owner-swatch" style="background:${p.ownerColorHex}"></span>`
    : '';
  h.innerHTML = `${swatch}<h2>${escape(p.name)}</h2><span class="pp-type">${p.type}</span>`;
  return h;
}

function renderDetails(p: SnapshotProvince): HTMLElement {
  const d = document.createElement('div');
  d.className = 'pp-details';
  if (!p.visible) {
    d.innerHTML = `<p class="hint">Out of sight (fog of war).</p>`;
    return d;
  }
  const morale = p.moraleLevel == null ? '—' : `${p.moraleLevel}`;
  const garrison = p.garrisonStrength == null ? '—' : `${p.garrisonStrength}`;
  const buildings = p.buildings.length === 0
    ? '<em>None</em>'
    : p.buildings.map(b => `${b.type} L${b.level}`).join(', ');

  d.innerHTML = `
    <ul>
      <li><strong>Morale:</strong> ${morale}</li>
      <li><strong>Garrison:</strong> ${garrison}</li>
      <li><strong>Buildings:</strong> ${escape(buildings)}</li>
      <li><strong>Adjacent:</strong> ${p.adjacentProvinceIds.length}</li>
    </ul>`;

  // Phase 2b: carrier composition. List each of MY carriers at this province with
  // its embarked wings nested underneath, so the player can see which carrier is
  // hosting which wings. Hidden when there are no carriers here.
  const world = $world.get();
  if (world) {
    const myUnitsHere = world.myUnits.filter(u =>
      u.locationProvinceId === p.id && !u.isInTransit);
    const carriers = myUnitsHere.filter(u => u.type === 'AircraftCarrier');
    if (carriers.length > 0) {
      const cw = document.createElement('div');
      cw.className = 'pp-carriers';
      cw.innerHTML = '<h3>Carrier groups</h3>';
      const ul = document.createElement('ul');
      ul.className = 'pp-carrier-list';
      for (const carrier of carriers) {
        const wings = myUnitsHere.filter(u =>
          u.type === 'CarrierAirWing' && u.parentUnitId === carrier.id);
        const li = document.createElement('li');
        const wingHtml = wings.length === 0
          ? '<li class="pp-empty">no wings embarked</li>'
          : wings.map(w => `<li>${escape(w.type)} (str ${w.strength})</li>`).join('');
        li.innerHTML = `
          <div class="pp-carrier-head">
            <strong>${escape(carrier.type)}</strong>
            <span class="pp-strength">str ${carrier.strength}</span>
            <span class="pp-cap">${wings.length}/4 wings</span>
          </div>
          <ul class="pp-wing-list">${wingHtml}</ul>`;
        ul.appendChild(li);
      }
      cw.appendChild(ul);
      d.appendChild(cw);
    }
  }

  return d;
}

function renderOrderForms(world: WorldSnapshot, province: SnapshotProvince): HTMLElement {
  const wrap = document.createElement('div');
  wrap.className = 'pp-orders';

  const isMine = province.ownerPlayerId === world.me.playerId;
  const myUnitsHere = world.myUnits.filter(u =>
    u.locationProvinceId === province.id && !u.isInTransit);

  // ---- Move form ----
  if (myUnitsHere.length > 0 && province.adjacentProvinceIds.length > 0) {
    const f = document.createElement('form');
    f.className = 'order-form';
    f.innerHTML = `
      <h3>Move unit</h3>
      <label>Unit
        <select name="unit">
          ${myUnitsHere.map(u =>
            `<option value="${u.id}">${u.type} (str ${u.strength})</option>`).join('')}
        </select>
      </label>
      <label>To
        <select name="target">
          ${province.adjacentProvinceIds.map(id => {
            const adj = world.provinces.find(p => p.id === id);
            return `<option value="${id}">${adj ? escape(adj.name) : id}</option>`;
          }).join('')}
        </select>
      </label>
      <button type="submit">Issue order</button>
      <span class="status"></span>`;
    f.addEventListener('submit', async ev => {
      ev.preventDefault();
      const fd = new FormData(f);
      const status = f.querySelector('.status') as HTMLElement;
      try {
        await orderMove(world.worldId, {
          unitId: fd.get('unit') as string,
          targetProvinceId: fd.get('target') as string,
        });
        status.textContent = 'queued';
        status.className = 'status ok';
      } catch (e) {
        status.textContent = formatError(e);
        status.className = 'status err';
      }
    });
    wrap.appendChild(f);
  }

  // ---- Build building form (owner only) ----
  if (isMine) {
    const availableBuildings: readonly string[] = province.isCoastal
      ? [...BUILDABLE_BUILDINGS, ...COASTAL_ONLY_BUILDINGS]
      : BUILDABLE_BUILDINGS;
    const f = document.createElement('form');
    f.className = 'order-form';
    f.innerHTML = `
      <h3>Build building</h3>
      <label>Type
        <select name="bt">
          ${availableBuildings.map(b => `<option value="${b}">${b}</option>`).join('')}
        </select>
      </label>
      <button type="submit">Queue construction</button>
      <span class="status"></span>`;
    f.addEventListener('submit', async ev => {
      ev.preventDefault();
      const fd = new FormData(f);
      const status = f.querySelector('.status') as HTMLElement;
      try {
        const r = await orderBuildBuilding(world.worldId, {
          provinceId: province.id,
          buildingType: fd.get('bt') as string,
        });
        status.textContent = `queued (${r.ticksRemaining} ticks)`;
        status.className = 'status ok';
      } catch (e) {
        status.textContent = formatError(e);
        status.className = 'status err';
      }
    });
    wrap.appendChild(f);

    // ---- Build unit form (owner only) ----
    const uf = document.createElement('form');
    uf.className = 'order-form';
    uf.innerHTML = `
      <h3>Build unit</h3>
      <label>Type
        <select name="ut">
          ${BUILDABLE_UNITS.map(u => `<option value="${u}">${u}</option>`).join('')}
        </select>
      </label>
      <label>Quantity <input name="qty" type="number" min="1" max="10000" value="1000" /></label>
      <button type="submit">Queue training</button>
      <span class="status"></span>`;
    uf.addEventListener('submit', async ev => {
      ev.preventDefault();
      const fd = new FormData(uf);
      const status = uf.querySelector('.status') as HTMLElement;
      try {
        const r = await orderBuildUnit(world.worldId, {
          provinceId: province.id,
          unitType: fd.get('ut') as string,
          quantity: parseInt(fd.get('qty') as string, 10),
        });
        status.textContent = `queued (${r.ticksRemaining} ticks)`;
        status.className = 'status ok';
      } catch (e) {
        status.textContent = formatError(e);
        status.className = 'status err';
      }
    });
    wrap.appendChild(uf);
  }

  if (wrap.children.length === 0) {
    const p = document.createElement('p');
    p.className = 'hint';
    p.textContent = isMine
      ? 'No units stationed and no adjacent provinces.'
      : 'You do not control this province.';
    wrap.appendChild(p);
  }

  return wrap;
}

function escape(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[c]!);
}

function formatError(e: unknown): string {
  if (e instanceof HttpError) {
    if (typeof e.body === 'object' && e.body && 'detail' in e.body) {
      return String((e.body as { detail: unknown }).detail);
    }
    return `HTTP ${e.status}`;
  }
  return e instanceof Error ? e.message : 'failed';
}
