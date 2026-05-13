// World Events panel (Phase 4c). Filtered view of $news showing only the
// Disaster category — natural disasters, resource booms, breakthroughs,
// civil unrest, market crashes. Newest first, full body text inline (the
// bottom news ticker only shows headlines on hover).
//
// Subscribes to $news so newly-broadcast events from the SignalR hub appear
// without a manual refresh.

import { $news } from '../store/store';
import type { NewsItem } from '../types/api';

export function mountWorldEventsPanel(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('world-events-panel');

  const header = document.createElement('h3');
  header.textContent = 'World Events';
  container.appendChild(header);

  const sub = document.createElement('p');
  sub.className = 'panel-subtitle';
  sub.textContent = 'Disasters, market crashes, scientific breakthroughs and other unscripted events.';
  container.appendChild(sub);

  const list = document.createElement('ul');
  list.className = 'world-events-list';
  container.appendChild(list);

  const empty = document.createElement('div');
  empty.className = 'world-events-empty';
  empty.textContent = 'No world events yet — quiet skies.';
  container.appendChild(empty);

  $news.subscribe(items => {
    const events = items.filter(i => i.category === 'Disaster');
    if (events.length === 0) {
      list.style.display = 'none';
      empty.style.display = '';
      return;
    }
    empty.style.display = 'none';
    list.style.display = '';
    list.innerHTML = '';
    for (const ev of events) {
      list.appendChild(renderEvent(ev));
    }
  });
}

function renderEvent(item: NewsItem): HTMLLIElement {
  const li = document.createElement('li');
  li.className = `world-event world-event-${item.severity.toLowerCase()}`;

  const tickEl = document.createElement('span');
  tickEl.className = 'world-event-tick';
  tickEl.textContent = `T${item.tick}`;

  const headEl = document.createElement('div');
  headEl.className = 'world-event-headline';
  headEl.textContent = item.headline;

  const bodyEl = document.createElement('div');
  bodyEl.className = 'world-event-body';
  bodyEl.textContent = item.body;

  li.appendChild(tickEl);
  li.appendChild(headEl);
  li.appendChild(bodyEl);
  return li;
}
