// Lobby: list / create / join worlds. After joining, calls onJoined(worldId).

import { listWorlds, createWorld, joinWorld, HttpError } from '../api/rest';
import type { WorldSummary } from '../types/api';

export function mountLobbyScreen(host: HTMLElement, onJoined: (worldId: string) => void) {
  host.innerHTML = `
    <div class="lobby">
      <h1>Worlds</h1>
      <ul id="world-list" class="world-list"><li>loading…</li></ul>
      <hr/>
      <h2>Create a new world</h2>
      <form id="create-form">
        <label>Name <input name="name" required minlength="3" maxlength="40" /></label>
        <button type="submit">Create</button>
        <span class="status"></span>
      </form>
    </div>`;

  const listEl = host.querySelector<HTMLUListElement>('#world-list')!;
  const createForm = host.querySelector<HTMLFormElement>('#create-form')!;

  refresh().catch(e => listEl.innerHTML = `<li>Failed to load: ${formatErr(e)}</li>`);

  createForm.addEventListener('submit', async ev => {
    ev.preventDefault();
    const fd = new FormData(createForm);
    const status = createForm.querySelector<HTMLElement>('.status')!;
    try {
      await createWorld(fd.get('name') as string);
      status.textContent = 'created';
      await refresh();
    } catch (e) {
      status.textContent = formatErr(e);
    }
  });

  async function refresh() {
    const worlds = await listWorlds();
    if (worlds.length === 0) {
      listEl.innerHTML = '<li class="hint">No worlds yet — create one below.</li>';
      return;
    }
    listEl.innerHTML = '';
    for (const w of worlds) listEl.appendChild(renderRow(w));
  }

  function renderRow(w: WorldSummary): HTMLLIElement {
    const li = document.createElement('li');
    li.innerHTML = `
      <div class="world-row">
        <strong>${escape(w.name)}</strong>
        <span>${w.status} · tick ${w.currentTick} · ${w.playerCount}/${w.maxPlayers} players</span>
        <form class="join-form">
          <input name="nation" placeholder="Nation name" required minlength="2" maxlength="32" />
          <input name="primary" type="color" value="#c81e1e" title="Flag primary" />
          <input name="secondary" type="color" value="#ffffff" title="Flag secondary" />
          <button type="submit">Join</button>
          <span class="status"></span>
        </form>
      </div>`;
    const form = li.querySelector<HTMLFormElement>('.join-form')!;
    form.addEventListener('submit', async ev => {
      ev.preventDefault();
      const fd = new FormData(form);
      const status = form.querySelector<HTMLElement>('.status')!;
      try {
        await joinWorld(w.id,
          fd.get('nation') as string,
          fd.get('primary') as string,
          fd.get('secondary') as string);
        onJoined(w.id);
      } catch (e) {
        // 409 "already joined" is the expected re-entry path: the user
        // refreshed, came back later, or clicked Join twice. Drop them
        // straight into the game instead of showing an error.
        if (e instanceof HttpError && e.status === 409 && isAlreadyJoined(e)) {
          onJoined(w.id);
          return;
        }
        status.textContent = formatErr(e);
      }
    });
    return li;
  }
}

function isAlreadyJoined(e: HttpError): boolean {
  const body = e.body as { error?: unknown } | null | undefined;
  return typeof body?.error === 'string' && body.error.toLowerCase().includes('already joined');
}

function escape(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[c]!);
}

function formatErr(e: unknown): string {
  if (e instanceof HttpError) {
    if (e.body && typeof e.body === 'object') {
      const body = e.body as { detail?: unknown; error?: unknown };
      if (typeof body.detail === 'string') return body.detail;
      if (typeof body.error === 'string') return body.error;
    }
    return `HTTP ${e.status}`;
  }
  return e instanceof Error ? e.message : 'failed';
}
