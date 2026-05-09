// Bottom news ticker. Renders the most recent cable-news headlines from the
// $news store. Color-coded by Severity (Breaking = red, Notable = amber, Info = neutral)
// per docs/07 §"News card style".
//
// The ticker shows up to MAX_VISIBLE entries and auto-truncates older ones; the
// store itself is capped at NEWS_CAP (50). The full history is recoverable via
// GET /api/worlds/{id}/news?since=N.

import { $news } from '../store/store';
import type { NewsItem } from '../types/api';

const MAX_VISIBLE = 6;

export function mountNewsTicker(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('news-ticker');

  const list = document.createElement('ul');
  list.className = 'news-list';
  container.appendChild(list);

  const empty = document.createElement('div');
  empty.className = 'news-empty';
  empty.textContent = 'No news yet — the wire is quiet.';
  container.appendChild(empty);

  $news.subscribe(items => {
    if (items.length === 0) {
      list.style.display = 'none';
      empty.style.display = '';
      return;
    }
    empty.style.display = 'none';
    list.style.display = '';

    // Items are already newest-first. Render up to MAX_VISIBLE; rebuild on every
    // change — the list is short enough that diffing isn't worth the complexity.
    list.innerHTML = '';
    for (const item of items.slice(0, MAX_VISIBLE)) {
      list.appendChild(renderItem(item));
    }
  });
}

function renderItem(item: NewsItem): HTMLLIElement {
  const li = document.createElement('li');
  li.className = `news-item news-${item.severity.toLowerCase()}`;
  // Title attribute exposes the body so hover shows the full sentence without
  // bloating the ticker; the visual line stays single-row.
  li.title = item.body;

  const tickEl = document.createElement('span');
  tickEl.className = 'news-tick';
  tickEl.textContent = `T${item.tick}`;

  const sevEl = document.createElement('span');
  sevEl.className = 'news-sev';
  sevEl.textContent = item.severity.toUpperCase();

  const headEl = document.createElement('span');
  headEl.className = 'news-headline';
  headEl.textContent = item.headline;

  li.appendChild(tickEl);
  li.appendChild(sevEl);
  li.appendChild(headEl);
  return li;
}
