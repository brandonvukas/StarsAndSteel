// Diplomacy panel: lists every other player in the world with their current
// relation to the caller and exposes the action buttons (declare war / propose
// peace / propose alliance / propose NAP). The inbox shows offers addressed to
// the caller (Accept / Reject); the outbox shows their own pending offers
// (Revoke). Reactive over $diplomacy and $world.

import {
  $diplomacy, $world, findRelation, findSanctions,
} from '../store/store';
import {
  declareWar, proposeTreaty, acceptOffer, rejectOffer, revokeOffer,
  sanctionPlayer, liftSanction,
  HttpError,
} from '../api/rest';
import type {
  DiplomacyState, DiplomacyPlayer, DiplomacyOffer,
  DiplomaticStatus, TreatyOfferKind,
} from '../types/api';

export function mountDiplomacyPanel(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('diplomacy-panel');

  function rerender() {
    const state = $diplomacy.get();
    const world = $world.get();
    container.innerHTML = '';
    if (!state || !world) {
      container.innerHTML = '<p class="hint">Loading diplomacy...</p>';
      return;
    }
    container.appendChild(renderInbox(world.worldId, state));
    container.appendChild(renderOutbox(world.worldId, state));
    container.appendChild(renderRoster(world.worldId, state));
  }

  $diplomacy.subscribe(rerender);
  $world.subscribe(rerender);
  rerender();
}

function renderInbox(worldId: string, state: DiplomacyState): HTMLElement {
  const wrap = document.createElement('section');
  wrap.className = 'dp-section';
  const h = document.createElement('h3');
  h.textContent = `Inbox (${state.inbox.length})`;
  wrap.appendChild(h);
  if (state.inbox.length === 0) {
    const p = document.createElement('p');
    p.className = 'hint';
    p.textContent = 'No pending offers.';
    wrap.appendChild(p);
    return wrap;
  }
  for (const offer of state.inbox) {
    wrap.appendChild(renderInboxOffer(worldId, state, offer));
  }
  return wrap;
}

function renderInboxOffer(worldId: string, state: DiplomacyState, offer: DiplomacyOffer): HTMLElement {
  const row = document.createElement('div');
  row.className = 'dp-offer';
  const sender = playerName(state, offer.senderPlayerId);
  row.innerHTML = `
    <div class="dp-offer-head">
      <strong>${escape(sender)}</strong>
      <span class="dp-kind">${kindLabel(offer.kind)}</span>
      <span class="dp-expiry">expires tick ${offer.expiresAtTick}</span>
    </div>
    <div class="dp-actions">
      <button data-act="accept">Accept</button>
      <button data-act="reject">Reject</button>
      <span class="dp-status"></span>
    </div>`;
  const status = row.querySelector<HTMLSpanElement>('.dp-status')!;
  row.querySelector<HTMLButtonElement>('[data-act="accept"]')!.onclick = async () => {
    await wrapAction(status, () => acceptOffer(worldId, offer.offerId));
  };
  row.querySelector<HTMLButtonElement>('[data-act="reject"]')!.onclick = async () => {
    await wrapAction(status, () => rejectOffer(worldId, offer.offerId));
  };
  return row;
}

function renderOutbox(worldId: string, state: DiplomacyState): HTMLElement {
  const wrap = document.createElement('section');
  wrap.className = 'dp-section';
  const h = document.createElement('h3');
  h.textContent = `Sent (${state.outbox.length})`;
  wrap.appendChild(h);
  if (state.outbox.length === 0) {
    const p = document.createElement('p');
    p.className = 'hint';
    p.textContent = 'No outstanding proposals.';
    wrap.appendChild(p);
    return wrap;
  }
  for (const offer of state.outbox) {
    const row = document.createElement('div');
    row.className = 'dp-offer';
    const receiver = playerName(state, offer.receiverPlayerId);
    row.innerHTML = `
      <div class="dp-offer-head">
        <span>To <strong>${escape(receiver)}</strong>: ${kindLabel(offer.kind)}</span>
        <span class="dp-expiry">expires tick ${offer.expiresAtTick}</span>
      </div>
      <div class="dp-actions">
        <button data-act="revoke">Revoke</button>
        <span class="dp-status"></span>
      </div>`;
    const status = row.querySelector<HTMLSpanElement>('.dp-status')!;
    row.querySelector<HTMLButtonElement>('[data-act="revoke"]')!.onclick = async () => {
      await wrapAction(status, () => revokeOffer(worldId, offer.offerId));
    };
    wrap.appendChild(row);
  }
  return wrap;
}

function renderRoster(worldId: string, state: DiplomacyState): HTMLElement {
  const wrap = document.createElement('section');
  wrap.className = 'dp-section';
  const h = document.createElement('h3');
  h.textContent = 'Other powers';
  wrap.appendChild(h);
  const others = state.players.filter(p => p.playerId !== state.callerPlayerId);
  if (others.length === 0) {
    const p = document.createElement('p');
    p.className = 'hint';
    p.textContent = 'No other players in this world yet.';
    wrap.appendChild(p);
    return wrap;
  }
  for (const player of others) {
    wrap.appendChild(renderRosterRow(worldId, state, player));
  }
  return wrap;
}

function renderRosterRow(worldId: string, state: DiplomacyState, player: DiplomacyPlayer): HTMLElement {
  const row = document.createElement('div');
  row.className = 'dp-player';
  const status = findRelation(state, player.playerId);
  const sanctions = findSanctions(state, player.playerId);
  const aliveBadge = player.isAlive ? '' : ' <span class="dp-dead">eliminated</span>';
  const aiBadge = player.isAi ? ' <span class="dp-ai">AI</span>' : '';
  const youSanctionBadge = sanctions.iSanction
    ? ' <span class="dp-sanction-out" title="You are sanctioning this player">Sanctioning</span>' : '';
  const theySanctionBadge = sanctions.theySanction
    ? ' <span class="dp-sanction-in" title="This player is sanctioning you">Sanctioned by</span>' : '';
  row.innerHTML = `
    <div class="dp-player-head">
      <span class="owner-swatch" style="background:${player.flagPrimaryHex}"></span>
      <strong>${escape(player.nationName)}</strong>${aiBadge}${aliveBadge}
      <span class="dp-status-badge dp-${status.toLowerCase()}">${statusLabel(status)}</span>
      ${youSanctionBadge}${theySanctionBadge}
    </div>
    <div class="dp-actions"></div>
    <span class="dp-status"></span>`;
  const actions = row.querySelector<HTMLDivElement>('.dp-actions')!;
  const statusEl = row.querySelector<HTMLSpanElement>('.dp-status')!;
  if (player.isAlive) {
    appendActionButtons(actions, statusEl, worldId, status, sanctions.iSanction, player.playerId);
  }
  return row;
}

function appendActionButtons(
  host: HTMLElement,
  status: HTMLSpanElement,
  worldId: string,
  current: DiplomaticStatus,
  iSanction: boolean,
  targetPlayerId: string,
) {
  // What's available depends on the current relation. War is always loud:
  // peace -> declare war + propose alliance/NAP. War -> propose peace.
  // Allied -> nothing useful in MVP (alliance break-up arrives in 2D).
  if (current !== 'War') {
    addButton(host, 'Declare War', async () => {
      if (!confirm('Declare war? This is instant and breaks any pending offers.')) return;
      await wrapAction(status, () => declareWar(worldId, targetPlayerId));
    });
  }
  if (current === 'War') {
    addButton(host, 'Propose Peace', async () => {
      await wrapAction(status, () => proposeTreaty(worldId, targetPlayerId, 'Peace'));
    });
  }
  if (current === 'Peace') {
    addButton(host, 'Propose NAP', async () => {
      await wrapAction(status, () => proposeTreaty(worldId, targetPlayerId, 'NonAggression'));
    });
    addButton(host, 'Propose Alliance', async () => {
      await wrapAction(status, () => proposeTreaty(worldId, targetPlayerId, 'Alliance'));
    });
  }
  if (current === 'NonAggression') {
    addButton(host, 'Propose Alliance', async () => {
      await wrapAction(status, () => proposeTreaty(worldId, targetPlayerId, 'Alliance'));
    });
  }
  // Phase 4e: sanction toggle. Always available against any other living player —
  // free, instant, asymmetric. Stacking (multiple players sanctioning the same target)
  // multiplies the target's money penalty.
  if (iSanction) {
    addButton(host, 'Lift Sanction', async () => {
      await wrapAction(status, () => liftSanction(worldId, targetPlayerId));
    });
  } else {
    addButton(host, 'Sanction', async () => {
      await wrapAction(status, () => sanctionPlayer(worldId, targetPlayerId));
    });
  }
}

function addButton(host: HTMLElement, label: string, onClick: () => Promise<void>) {
  const b = document.createElement('button');
  b.textContent = label;
  b.onclick = async () => {
    b.disabled = true;
    try {
      await onClick();
    } finally {
      b.disabled = false;
    }
  };
  host.appendChild(b);
}

async function wrapAction(status: HTMLElement, fn: () => Promise<unknown>) {
  status.textContent = '';
  status.className = 'dp-status';
  try {
    await fn();
    status.textContent = 'OK';
    status.classList.add('dp-ok');
  } catch (err) {
    status.textContent = formatErr(err);
    status.classList.add('dp-err');
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

function playerName(state: DiplomacyState, playerId: string): string {
  return state.players.find(p => p.playerId === playerId)?.nationName ?? '?';
}

function kindLabel(kind: TreatyOfferKind): string {
  switch (kind) {
    case 'Peace': return 'Peace';
    case 'NonAggression': return 'Non-Aggression Pact';
    case 'Alliance': return 'Alliance';
  }
}

function statusLabel(s: DiplomaticStatus): string {
  switch (s) {
    case 'Peace': return 'Peace';
    case 'War': return 'WAR';
    case 'Allied': return 'Allied';
    case 'NonAggression': return 'NAP';
    case 'TradeAgreement': return 'Trade';
  }
}

function escape(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[c]!));
}
