// Wonders panel (Phase 4b1): catalogue of one-per-game wonders + their
// per-world status. Renders one card per wonder showing the effect summary,
// build cost, ticks-to-build, and either the owner (Built/InProgress) or a
// "Build at..." dropdown over the caller's owned provinces (Available).
//
// Wonders are submitted through the regular orderBuildBuilding endpoint with
// one of the wonder BuildingType values; the server-side WondersController
// is read-only. After a successful submit we re-fetch so the row flips to
// InProgress immediately.
//
// Style mirrors generalsPanel.ts and researchPanel.ts: vanilla TS DOM, store
// subscriptions, inline status spans for ok/err feedback.

import { $wonders, $world, setWonders } from '../store/store';
import { getWonders, orderBuildBuilding, HttpError } from '../api/rest';
import type { WonderRow, WonderCost, WorldSnapshot } from '../types/api';

export function mountWondersPanel(container: HTMLElement, worldId: string) {
  container.innerHTML = '';
  container.classList.add('wonders-panel');

  function rerender() {
    const wonders = $wonders.get();
    const world = $world.get();
    container.innerHTML = '';
    if (!world) {
      container.innerHTML = '<p class="hint">Loading...</p>';
      return;
    }
    if (wonders === null) {
      container.innerHTML = '<p class="hint">Loading wonders...</p>';
      return;
    }

    container.appendChild(renderHeader());

    if (wonders.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'hint';
      empty.textContent = 'No wonders defined.';
      container.appendChild(empty);
      return;
    }

    for (const w of wonders) {
      container.appendChild(renderWonderCard(worldId, world, w));
    }
  }

  $wonders.subscribe(rerender);
  $world.subscribe(rerender);
  rerender();
}

function renderHeader(): HTMLElement {
  const h = document.createElement('div');
  h.className = 'wp-header';
  h.innerHTML = `
    <h2>Wonders of the modern world</h2>
    <p class="hint">Each wonder is built <strong>once per game</strong>, anywhere
    on the map, by whoever finishes it first. Effects are permanent for the
    owner. Pick your project carefully — the race is global.</p>`;
  return h;
}

function renderWonderCard(
  worldId: string,
  world: WorldSnapshot,
  wonder: WonderRow,
): HTMLElement {
  const card = document.createElement('div');
  card.className = `wp-card wp-status-${wonder.status.toLowerCase()}`;

  const header = document.createElement('div');
  header.className = 'wp-card-header';
  header.innerHTML = `
    <h3>${escape(wonder.name)} <span class="wp-status">${escape(wonder.status)}</span></h3>
    <p class="wp-summary">${escape(wonder.summary)}</p>
    <p class="hint">Cost: ${formatCost(wonder.cost)} &middot; ${wonder.ticksToBuild} ticks to build</p>`;
  card.appendChild(header);

  if (wonder.status === 'Built') {
    const claimed = document.createElement('p');
    claimed.className = 'wp-claim';
    claimed.innerHTML = `Built by <strong>${escape(wonder.ownerNationName ?? '?')}</strong>` +
      (wonder.provinceName ? ` at <strong>${escape(wonder.provinceName)}</strong>` : '');
    card.appendChild(claimed);
    return card;
  }

  if (wonder.status === 'InProgress') {
    const claimed = document.createElement('p');
    claimed.className = 'wp-claim';
    const remaining = wonder.ticksRemaining ?? wonder.ticksToBuild;
    claimed.innerHTML = `Under construction by <strong>${escape(wonder.ownerNationName ?? '?')}</strong>` +
      (wonder.provinceName ? ` at <strong>${escape(wonder.provinceName)}</strong>` : '') +
      ` &middot; ${remaining} ticks remaining`;
    card.appendChild(claimed);
    return card;
  }

  // Available: render Build form over the caller's owned provinces.
  card.appendChild(renderBuildForm(worldId, world, wonder));
  return card;
}

function renderBuildForm(
  worldId: string,
  world: WorldSnapshot,
  wonder: WonderRow,
): HTMLElement {
  const myProvinces = world.provinces
    .filter(p => p.ownerPlayerId === world.me.playerId)
    .slice()
    .sort((a, b) => a.name.localeCompare(b.name));

  const f = document.createElement('form');
  f.className = 'order-form';

  if (myProvinces.length === 0) {
    f.innerHTML = `<p class="hint">You don't own any provinces — capture territory before pursuing a wonder.</p>`;
    return f;
  }

  const canAfford = canAffordCost(world, wonder.cost);
  f.innerHTML = `
    <h3>Begin construction</h3>
    <label>Build at province
      <select name="province">
        ${myProvinces.map(p => `<option value="${p.id}">${escape(p.name)}</option>`).join('')}
      </select>
    </label>
    ${canAfford ? '' : '<p class="hint err">Insufficient resources for this wonder.</p>'}
    <button type="submit"${canAfford ? '' : ' disabled'}>Begin</button>
    <span class="status"></span>`;

  f.addEventListener('submit', async ev => {
    ev.preventDefault();
    const status = f.querySelector('.status') as HTMLElement;
    const fd = new FormData(f);
    const provinceId = fd.get('province') as string;
    try {
      await orderBuildBuilding(worldId, { provinceId, buildingType: wonder.type });
      // Re-fetch so the row flips Available -> InProgress and the dropdown
      // is replaced with the in-progress claim line.
      setWonders(await getWonders(worldId));
      status.textContent = 'construction begun';
      status.className = 'status ok';
    } catch (e) {
      status.textContent = formatError(e);
      status.className = 'status err';
    }
  });
  return f;
}

function canAffordCost(world: WorldSnapshot, cost: WonderCost): boolean {
  const r = world.me.resources;
  return r.money >= cost.money
      && r.oil >= cost.oil
      && r.steel >= cost.steel
      && r.electronics >= cost.electronics
      && r.food >= cost.food
      && r.manpower >= cost.manpower;
}

function formatCost(cost: WonderCost): string {
  const parts: string[] = [];
  if (cost.money) parts.push(`$${cost.money.toLocaleString()}`);
  if (cost.oil) parts.push(`${cost.oil.toLocaleString()} oil`);
  if (cost.steel) parts.push(`${cost.steel.toLocaleString()} steel`);
  if (cost.electronics) parts.push(`${cost.electronics.toLocaleString()} elec`);
  if (cost.food) parts.push(`${cost.food.toLocaleString()} food`);
  if (cost.manpower) parts.push(`${cost.manpower.toLocaleString()} manpower`);
  return parts.join(' / ');
}

function escape(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[c]!));
}

function formatError(e: unknown): string {
  if (e instanceof HttpError) {
    if (typeof e.body === 'object' && e.body && 'detail' in e.body) {
      return String((e.body as { detail: unknown }).detail);
    }
    return `HTTP ${e.status}`;
  }
  return e instanceof Error ? e.message : 'Failed';
}
