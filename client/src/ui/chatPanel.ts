// Chat panel: tabbed Global / Alliance / Direct view + send form.
// Reactive over $chat, $world, $diplomacy. Server-side history is pre-filtered;
// live hub events are world-broadcast so we re-apply the same visibility rules
// client-side (sender's allies for Alliance, sender/recipient for Direct).

import { $chat, $world, $diplomacy, pushChat } from '../store/store';
import { sendChatMessage, HttpError } from '../api/rest';
import type { ChatMessageDto, ChatScope, DiplomacyState, WorldSnapshot } from '../types/api';

type Tab = 'global' | 'alliance' | 'direct';

interface PanelState {
  tab: Tab;
  /** Selected DM target playerId; null = no recipient picked yet. */
  dmTargetId: string | null;
  /** Pending input text (preserved across re-renders). */
  draft: string;
  /** Last send error to surface inline. */
  lastError: string | null;
}

const state: PanelState = {
  tab: 'global',
  dmTargetId: null,
  draft: '',
  lastError: null,
};

export function mountChatPanel(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('chat-panel');

  function rerender() {
    const world = $world.get();
    const dipl = $diplomacy.get();
    const messages = $chat.get();

    container.innerHTML = '';
    if (!world) {
      container.innerHTML = '<p class="hint">Loading chat...</p>';
      return;
    }

    container.appendChild(renderTabs());
    container.appendChild(renderMessages(messages, world, dipl));
    container.appendChild(renderComposer(world, dipl, rerender));
  }

  $chat.subscribe(rerender);
  $world.subscribe(rerender);
  $diplomacy.subscribe(rerender);
  rerender();
}

function renderTabs(): HTMLElement {
  const tabs = document.createElement('nav');
  tabs.className = 'chat-tabs';
  const defs: { tab: Tab; label: string }[] = [
    { tab: 'global', label: 'Global' },
    { tab: 'alliance', label: 'Alliance' },
    { tab: 'direct', label: 'Direct' },
  ];
  for (const d of defs) {
    const btn = document.createElement('button');
    btn.textContent = d.label;
    btn.className = state.tab === d.tab ? 'active' : '';
    btn.onclick = () => {
      state.tab = d.tab;
      state.lastError = null;
      // Force a re-render via store ping (cheapest is to set chat to itself).
      $chat.set($chat.get());
    };
    tabs.appendChild(btn);
  }
  return tabs;
}

function renderMessages(
  messages: ChatMessageDto[],
  world: WorldSnapshot,
  dipl: DiplomacyState | null,
): HTMLElement {
  const list = document.createElement('div');
  list.className = 'chat-messages';
  const me = world.me.playerId;
  const allies = computeAllies(dipl, me);

  const visible = messages.filter(m => isVisibleInTab(m, state.tab, me, allies, state.dmTargetId));
  if (visible.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'hint';
    empty.textContent = 'No messages yet.';
    list.appendChild(empty);
    return list;
  }

  const playerName = playerNameLookup(world);
  for (const m of visible) {
    const row = document.createElement('div');
    row.className = 'chat-msg';
    if (m.fromPlayerId === me) row.classList.add('chat-msg-mine');
    const time = new Date(m.sentAtUtc).toLocaleTimeString();
    const from = playerName(m.fromPlayerId);
    const to = m.scope === 'Direct' && m.toPlayerId ? ` → ${escape(playerName(m.toPlayerId))}` : '';
    row.innerHTML = `
      <span class="chat-meta">[${time}] <strong>${escape(from)}</strong>${to}
        <span class="chat-scope chat-scope-${m.scope.toLowerCase()}">${m.scope}</span>
      </span>
      <span class="chat-body">${escape(m.body)}</span>`;
    list.appendChild(row);
  }

  // Auto-scroll to newest after render.
  setTimeout(() => { list.scrollTop = list.scrollHeight; }, 0);
  return list;
}

function renderComposer(
  world: WorldSnapshot,
  dipl: DiplomacyState | null,
  rerender: () => void,
): HTMLElement {
  const form = document.createElement('form');
  form.className = 'chat-composer';

  // DM target dropdown only when on Direct tab.
  if (state.tab === 'direct') {
    const select = document.createElement('select');
    const placeholder = document.createElement('option');
    placeholder.value = '';
    placeholder.textContent = '— pick a recipient —';
    select.appendChild(placeholder);
    const me = world.me.playerId;
    // Use diplomacy roster if available (richer player metadata), else fall back to snapshot players.
    const players = dipl?.players?.map(p => ({ id: p.playerId, name: p.nationName, alive: p.isAlive }))
      ?? world.players.map(p => ({ id: p.playerId, name: p.nationName, alive: p.isAlive }));
    for (const p of players) {
      if (p.id === me || !p.alive) continue;
      const opt = document.createElement('option');
      opt.value = p.id;
      opt.textContent = p.name;
      if (state.dmTargetId === p.id) opt.selected = true;
      select.appendChild(opt);
    }
    select.onchange = () => {
      state.dmTargetId = select.value || null;
      state.lastError = null;
      rerender();
    };
    form.appendChild(select);
  }

  const input = document.createElement('input');
  input.type = 'text';
  input.maxLength = 500;
  input.placeholder = scopePlaceholder(state.tab);
  input.value = state.draft;
  input.oninput = () => { state.draft = input.value; };
  form.appendChild(input);

  const submit = document.createElement('button');
  submit.type = 'submit';
  submit.textContent = 'Send';
  form.appendChild(submit);

  if (state.lastError) {
    const err = document.createElement('span');
    err.className = 'chat-err';
    err.textContent = state.lastError;
    form.appendChild(err);
  }

  form.onsubmit = async e => {
    e.preventDefault();
    state.lastError = null;
    const body = state.draft.trim();
    if (body.length === 0) return;
    const scope = tabToScope(state.tab);
    if (scope === 'Direct' && !state.dmTargetId) {
      state.lastError = 'Pick a recipient first.';
      rerender();
      return;
    }
    submit.disabled = true;
    try {
      await sendChatMessage(world.worldId, {
        scope,
        toPlayerId: scope === 'Direct' ? state.dmTargetId : null,
        body,
      });
      state.draft = '';
      // Hub event will push the message into the store; no optimistic insert needed.
    } catch (err) {
      state.lastError = formatErr(err);
    } finally {
      submit.disabled = false;
      rerender();
    }
  };

  return form;
}

// ---- visibility helpers ---------------------------------------------------

function isVisibleInTab(
  m: ChatMessageDto,
  tab: Tab,
  me: string,
  allies: Set<string>,
  dmTargetId: string | null,
): boolean {
  switch (tab) {
    case 'global': return m.scope === 'Global';
    case 'alliance':
      // Alliance messages from self or any current ally.
      return m.scope === 'Alliance' && (m.fromPlayerId === me || allies.has(m.fromPlayerId));
    case 'direct': {
      if (m.scope !== 'Direct') return false;
      if (m.fromPlayerId !== me && m.toPlayerId !== me) return false;
      // If a target is selected, narrow the thread to that pair.
      if (dmTargetId) {
        return (m.fromPlayerId === me && m.toPlayerId === dmTargetId)
            || (m.fromPlayerId === dmTargetId && m.toPlayerId === me);
      }
      return true;
    }
  }
}

function computeAllies(dipl: DiplomacyState | null, me: string): Set<string> {
  const set = new Set<string>();
  if (!dipl) return set;
  for (const r of dipl.relations) {
    if (r.status !== 'Allied') continue;
    if (r.partyAPlayerId === me) set.add(r.partyBPlayerId);
    else if (r.partyBPlayerId === me) set.add(r.partyAPlayerId);
  }
  return set;
}

function playerNameLookup(world: WorldSnapshot): (id: string) => string {
  const map = new Map<string, string>();
  for (const p of world.players) map.set(p.playerId, p.nationName);
  map.set(world.me.playerId, world.me.nationName);
  return id => map.get(id) ?? id.slice(0, 8);
}

function tabToScope(tab: Tab): ChatScope {
  switch (tab) {
    case 'global': return 'Global';
    case 'alliance': return 'Alliance';
    case 'direct': return 'Direct';
  }
}

function scopePlaceholder(tab: Tab): string {
  switch (tab) {
    case 'global': return 'Broadcast to everyone in the world';
    case 'alliance': return 'Visible to your current allies';
    case 'direct': return 'Direct message';
  }
}

/** Push a chat message into the store. Exported for the hub wiring in gameScreen. */
export function ingestChatMessage(m: ChatMessageDto) {
  pushChat(m);
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
