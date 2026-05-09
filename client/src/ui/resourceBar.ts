// Top resource bar. Renders the calling player's current resources; updates
// whenever the world store changes (ResourcesUpdated diff or full re-snapshot).

import { $world } from '../store/store';

export function mountResourceBar(container: HTMLElement) {
  container.innerHTML = '';
  container.classList.add('resource-bar');

  const labels: Array<[keyof typeof FIELDS, string]> = [
    ['money', '💰'],
    ['oil', '🛢️'],
    ['steel', '🔩'],
    ['electronics', '💾'],
    ['food', '🌾'],
    ['manpower', '👥'],
  ];
  const cells: Record<string, HTMLElement> = {};
  for (const [key, icon] of labels) {
    const cell = document.createElement('span');
    cell.className = 'resource-cell';
    cell.innerHTML = `<span class="rc-icon">${icon}</span><span class="rc-val">0</span>`;
    cell.title = key;
    container.appendChild(cell);
    cells[key] = cell.querySelector('.rc-val')!;
  }

  const tickCell = document.createElement('span');
  tickCell.className = 'tick-cell';
  tickCell.textContent = 'tick 0';
  container.appendChild(tickCell);

  $world.subscribe(world => {
    if (!world) return;
    const r = world.me.resources;
    cells.money.textContent = formatNum(r.money);
    cells.oil.textContent = formatNum(r.oil);
    cells.steel.textContent = formatNum(r.steel);
    cells.electronics.textContent = formatNum(r.electronics);
    cells.food.textContent = formatNum(r.food);
    cells.manpower.textContent = formatNum(r.manpower);
    tickCell.textContent = `tick ${world.currentTick}`;
  });
}

const FIELDS = {
  money: 0, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0,
};

function formatNum(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 10_000) return (n / 1_000).toFixed(1) + 'k';
  return n.toLocaleString();
}
