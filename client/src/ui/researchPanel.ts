// Research panel: lists the tech catalogue grouped by category. Each tech row
// shows progress (locked / in-progress with bar / unlocked), prerequisite
// status, and a Start button when eligible. Reactive over $research and $world.

import { $research, $world } from '../store/store';
import { startResearch, HttpError } from '../api/rest';
import type { ResearchState, TechSpec, ResearchProgress } from '../types/api';

type Status = 'unlocked' | 'in-progress' | 'available' | 'locked';

export function mountResearchPanel(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('research-panel');

  function rerender() {
    const state = $research.get();
    const world = $world.get();
    container.innerHTML = '';
    if (!state || !world) {
      container.innerHTML = '<p class="hint">Loading research...</p>';
      return;
    }
    container.appendChild(renderTree(world.worldId, state));
  }

  $research.subscribe(rerender);
  $world.subscribe(rerender);
  rerender();
}

function renderTree(worldId: string, state: ResearchState): HTMLElement {
  const wrap = document.createElement('div');
  const progressById = new Map(state.myProgress.map(p => [p.techId, p]));

  const categories = ['Military', 'Industry', 'Doctrine', 'Logistics'] as const;
  for (const cat of categories) {
    const section = document.createElement('section');
    section.className = 'rp-section';
    const h = document.createElement('h3');
    h.textContent = cat;
    section.appendChild(h);

    const techs = state.catalog.filter(t => t.category === cat);
    for (const tech of techs) {
      section.appendChild(renderTechRow(worldId, state, tech, progressById));
    }
    wrap.appendChild(section);
  }
  return wrap;
}

function renderTechRow(
  worldId: string,
  state: ResearchState,
  tech: TechSpec,
  progressById: Map<string, ResearchProgress>,
): HTMLElement {
  const row = document.createElement('div');
  row.className = 'rp-tech';
  const progress = progressById.get(tech.id);
  const status = computeStatus(tech, progress, progressById);
  row.classList.add(`rp-${status}`);

  const head = document.createElement('div');
  head.className = 'rp-tech-head';
  head.innerHTML = `
    <strong>${escape(tech.name)}</strong>
    <span class="rp-status-badge rp-${status}">${statusLabel(status, progress)}</span>`;
  row.appendChild(head);

  const summary = document.createElement('p');
  summary.className = 'rp-summary';
  summary.textContent = tech.summary;
  row.appendChild(summary);

  if (tech.prerequisites.length > 0) {
    const pre = document.createElement('p');
    pre.className = 'rp-prereq';
    const labels = tech.prerequisites.map(id => {
      const ok = progressById.get(id)?.isUnlocked ?? false;
      const name = state.catalog.find(t => t.id === id)?.name ?? id;
      return `<span class="rp-pre ${ok ? 'rp-pre-ok' : 'rp-pre-missing'}">${escape(name)}</span>`;
    }).join(' ');
    pre.innerHTML = `Requires: ${labels}`;
    row.appendChild(pre);
  }

  if (status === 'in-progress' && progress) {
    const bar = document.createElement('div');
    bar.className = 'rp-bar';
    const pct = Math.min(100, Math.round((progress.progressPoints / progress.ticksToResearch) * 100));
    bar.innerHTML = `
      <div class="rp-bar-fill" style="width:${pct}%"></div>
      <span class="rp-bar-label">${progress.progressPoints} / ${progress.ticksToResearch} ticks</span>`;
    row.appendChild(bar);
  }

  if (status === 'available' || status === 'locked') {
    const cost = document.createElement('p');
    cost.className = 'rp-cost';
    cost.textContent =
      `Cost: $${tech.moneyCost.toLocaleString()} + ${tech.electronicsCost.toLocaleString()} electronics · ${tech.ticksToResearch} ticks`;
    row.appendChild(cost);
  }

  if (status === 'available') {
    const actions = document.createElement('div');
    actions.className = 'rp-actions';
    const btn = document.createElement('button');
    btn.textContent = 'Start research';
    const statusEl = document.createElement('span');
    statusEl.className = 'rp-action-status';
    btn.onclick = async () => {
      btn.disabled = true;
      statusEl.textContent = '';
      statusEl.className = 'rp-action-status';
      try {
        await startResearch(worldId, tech.id);
        statusEl.textContent = 'Started';
        statusEl.classList.add('rp-ok');
      } catch (err) {
        statusEl.textContent = formatErr(err);
        statusEl.classList.add('rp-err');
      } finally {
        btn.disabled = false;
      }
    };
    actions.appendChild(btn);
    actions.appendChild(statusEl);
    row.appendChild(actions);
  }

  return row;
}

function computeStatus(
  tech: TechSpec,
  progress: ResearchProgress | undefined,
  progressById: Map<string, ResearchProgress>,
): Status {
  if (progress?.isUnlocked) return 'unlocked';
  if (progress) return 'in-progress';
  const prereqsMet = tech.prerequisites.every(id => progressById.get(id)?.isUnlocked);
  return prereqsMet ? 'available' : 'locked';
}

function statusLabel(status: Status, progress: ResearchProgress | undefined): string {
  switch (status) {
    case 'unlocked': return 'UNLOCKED';
    case 'in-progress': {
      if (!progress) return 'IN PROGRESS';
      const pct = Math.round((progress.progressPoints / progress.ticksToResearch) * 100);
      return `${pct}%`;
    }
    case 'available': return 'AVAILABLE';
    case 'locked': return 'LOCKED';
  }
}

function formatErr(err: unknown): string {
  if (err instanceof HttpError) {
    const body = err.body as { error?: string; detail?: string; title?: string } | null;
    if (body?.error) return body.error;
    if (body?.detail) return body.detail;
    if (body?.title) return body.title;
    return `HTTP ${err.status}`;
  }
  return (err as Error)?.message ?? 'Unknown error';
}

function escape(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[c]!));
}
