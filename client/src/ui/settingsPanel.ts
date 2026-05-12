// Settings panel (Phase 2L). Currently exposes only the QuietHours window.
// Advisory only — the client suppresses non-critical hub notifications during
// the configured window in a future iteration; for now we just round-trip the
// values through the server.

import { getMe, updateQuietHours, HttpError } from '../api/rest';
import type { MeResponse } from '../types/api';

interface State {
  loaded: boolean;
  start: string;       // "HH:MM" or ""
  end: string;         // "HH:MM" or ""
  status: string | null;
  error: string | null;
}

const state: State = { loaded: false, start: '', end: '', status: null, error: null };

export function mountSettingsPanel(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('settings-panel');

  function rerender() {
    container.innerHTML = '';
    if (!state.loaded) {
      container.innerHTML = '<p class="hint">Loading settings...</p>';
      return;
    }
    container.appendChild(renderForm(rerender));
  }

  // Lazy-load on first mount; subsequent mounts reuse the cached state.
  void (async () => {
    if (state.loaded) {
      rerender();
      return;
    }
    try {
      const me = await getMe();
      hydrate(me);
      state.loaded = true;
    } catch (err) {
      state.error = formatErr(err);
      state.loaded = true; // unblock the UI even on error
    }
    rerender();
  })();

  rerender();
}

function hydrate(me: MeResponse) {
  state.start = me.quietHoursStartUtc ? me.quietHoursStartUtc.slice(0, 5) : '';
  state.end = me.quietHoursEndUtc ? me.quietHoursEndUtc.slice(0, 5) : '';
}

function renderForm(rerender: () => void): HTMLElement {
  const form = document.createElement('form');
  form.className = 'settings-form';
  form.innerHTML = `
    <h3>Quiet Hours (UTC)</h3>
    <p class="hint">
      Suppress non-critical notifications during this window. Wraps midnight
      when start &gt; end. Both fields blank = disabled.
    </p>
    <label>Start <input type="time" name="start" value="${state.start}"></label>
    <label>End   <input type="time" name="end"   value="${state.end}"></label>
    <div class="settings-actions">
      <button type="submit">Save</button>
      <button type="button" data-act="clear">Clear</button>
      <span class="settings-status">${state.status ? escape(state.status) : ''}</span>
      <span class="settings-err">${state.error ? escape(state.error) : ''}</span>
    </div>`;

  const startEl = form.querySelector<HTMLInputElement>('input[name="start"]')!;
  const endEl   = form.querySelector<HTMLInputElement>('input[name="end"]')!;
  startEl.oninput = () => { state.start = startEl.value; };
  endEl.oninput   = () => { state.end   = endEl.value; };

  const submitBtn = form.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  const clearBtn  = form.querySelector<HTMLButtonElement>('button[data-act="clear"]')!;

  form.onsubmit = async e => {
    e.preventDefault();
    state.status = null;
    state.error = null;
    if ((state.start === '') !== (state.end === '')) {
      state.error = 'Set both start and end together, or clear both.';
      rerender();
      return;
    }
    submitBtn.disabled = true;
    try {
      const me = await updateQuietHours({
        quietHoursStartUtc: state.start ? `${state.start}:00` : null,
        quietHoursEndUtc:   state.end   ? `${state.end}:00`   : null,
      });
      hydrate(me);
      state.status = 'Saved.';
    } catch (err) {
      state.error = formatErr(err);
    } finally {
      submitBtn.disabled = false;
      rerender();
    }
  };

  clearBtn.onclick = async () => {
    state.status = null;
    state.error = null;
    clearBtn.disabled = true;
    try {
      const me = await updateQuietHours({
        quietHoursStartUtc: null,
        quietHoursEndUtc: null,
      });
      hydrate(me);
      state.status = 'Cleared.';
    } catch (err) {
      state.error = formatErr(err);
    } finally {
      clearBtn.disabled = false;
      rerender();
    }
  };

  return form;
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
