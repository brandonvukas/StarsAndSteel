// Generals panel (Phase 4a): surfaces the caller's theater commander —
// recruit when none exists, then assign / reassign to a friendly province.
// MVP cap is one general per player; while assigned to a province the
// engine applies +15% defender effective strength + outgoing damage at
// that province (CombatStep + GeneralsService.DefenderCombatBonus).
//
// Style mirrors researchPanel.ts: vanilla TS DOM, store subscriptions for
// re-render, status spans for inline ok/err feedback. State lives in
// $generals (caller's only); we re-fetch + replace after each POST so the
// UI never gets out of sync with the server-side one-per-player cap.

import { $generals, $world, setGenerals } from '../store/store';
import { recruitGeneral, assignGeneral, getGenerals, HttpError } from '../api/rest';
import type { GeneralDto, WorldSnapshot } from '../types/api';

export function mountGeneralsPanel(container: HTMLElement, worldId: string) {
  container.innerHTML = '';
  container.classList.add('generals-panel');

  function rerender() {
    const generals = $generals.get();
    const world = $world.get();
    container.innerHTML = '';
    if (!world) {
      container.innerHTML = '<p class="hint">Loading...</p>';
      return;
    }
    if (generals === null) {
      container.innerHTML = '<p class="hint">Loading generals...</p>';
      return;
    }

    container.appendChild(renderHeader());

    if (generals.length === 0) {
      container.appendChild(renderRecruitForm(worldId, world));
      return;
    }

    for (const g of generals) {
      container.appendChild(renderGeneralCard(worldId, world, g));
    }
  }

  $generals.subscribe(rerender);
  $world.subscribe(rerender);
  rerender();
}

function renderHeader(): HTMLElement {
  const h = document.createElement('div');
  h.className = 'gp-header';
  h.innerHTML = `
    <h2>Theater commanders</h2>
    <p class="hint">A general assigned to one of your provinces grants
    <strong>+15%</strong> defender effective strength and outgoing damage in
    ground combat there. Stacks multiplicatively with the
    <em>defense_in_depth</em> doctrine. MVP cap: one general per player.</p>`;
  return h;
}

function renderRecruitForm(worldId: string, world: WorldSnapshot): HTMLElement {
  const f = document.createElement('form');
  f.className = 'order-form';
  const canAfford = world.me.resources.money >= 2_500;
  f.innerHTML = `
    <h3>Recruit general</h3>
    <label>Name
      <input type="text" name="name" required maxlength="80" placeholder="e.g. Patton" />
    </label>
    <p class="hint">Cost: <strong>$2,500</strong>${canAfford ? '' : ' &mdash; insufficient funds'}</p>
    <button type="submit" ${canAfford ? '' : 'disabled'}>Recruit</button>
    <span class="status"></span>`;
  f.addEventListener('submit', async ev => {
    ev.preventDefault();
    const status = f.querySelector('.status') as HTMLElement;
    const fd = new FormData(f);
    const name = (fd.get('name') as string ?? '').trim();
    if (!name) {
      status.textContent = 'Name is required';
      status.className = 'status err';
      return;
    }
    try {
      await recruitGeneral(worldId, { name });
      // Re-fetch so the new (unassigned) general shows up; server is the
      // source of truth for the cap and for the money debit.
      setGenerals(await getGenerals(worldId));
      status.textContent = 'recruited';
      status.className = 'status ok';
    } catch (e) {
      status.textContent = formatError(e);
      status.className = 'status err';
    }
  });
  return f;
}

function renderGeneralCard(
  worldId: string,
  world: WorldSnapshot,
  general: GeneralDto,
): HTMLElement {
  const card = document.createElement('div');
  card.className = 'gp-card';
  const assignedProv = general.assignedProvinceId
    ? world.provinces.find(p => p.id === general.assignedProvinceId)
    : null;

  const header = document.createElement('div');
  header.className = 'gp-card-header';
  header.innerHTML = `
    <h3>${escape(general.name)}</h3>
    <p>XP level: <strong>${general.xpLevel}</strong></p>
    <p>Currently: ${
      assignedProv
        ? `assigned to <strong>${escape(assignedProv.name)}</strong> (+15% defender bonus)`
        : '<em>unassigned</em> (no bonus active)'
    }</p>`;
  card.appendChild(header);

  card.appendChild(renderAssignForm(worldId, world, general));
  return card;
}

function renderAssignForm(
  worldId: string,
  world: WorldSnapshot,
  general: GeneralDto,
): HTMLElement {
  const myProvinces = world.provinces
    .filter(p => p.ownerPlayerId === world.me.playerId)
    .slice()
    .sort((a, b) => a.name.localeCompare(b.name));

  const f = document.createElement('form');
  f.className = 'order-form';
  if (myProvinces.length === 0) {
    f.innerHTML = `<p class="hint">You don't currently own any provinces.</p>`;
    return f;
  }

  const verb = general.assignedProvinceId ? 'Reassign' : 'Assign';
  f.innerHTML = `
    <h3>${verb}</h3>
    <label>Province
      <select name="province">
        ${myProvinces.map(p => {
          const sel = p.id === general.assignedProvinceId ? ' selected' : '';
          return `<option value="${p.id}"${sel}>${escape(p.name)}</option>`;
        }).join('')}
      </select>
    </label>
    <button type="submit">${verb}</button>
    <span class="status"></span>`;

  f.addEventListener('submit', async ev => {
    ev.preventDefault();
    const status = f.querySelector('.status') as HTMLElement;
    const fd = new FormData(f);
    const provinceId = fd.get('province') as string;
    try {
      await assignGeneral(worldId, general.id, { provinceId });
      setGenerals(await getGenerals(worldId));
      status.textContent = 'assigned';
      status.className = 'status ok';
    } catch (e) {
      status.textContent = formatError(e);
      status.className = 'status err';
    }
  });
  return f;
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
