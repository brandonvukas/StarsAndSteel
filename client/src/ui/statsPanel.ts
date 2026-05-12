// Stats panel (Phase 2L). Lightweight Chart.js bar charts over the current
// world snapshot. No time series — server doesn't yet record per-tick history,
// so we visualize the live state. Re-renders whenever $world ticks.
//
// Two charts:
//   1. Provinces owned per player (already in WorldSnapshot.players).
//   2. My visible strength per unit type (caller's myUnits aggregated).
// Both update when the underlying snapshot changes.

import { Chart, BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend } from 'chart.js';
import { $world } from '../store/store';
import type { WorldSnapshot } from '../types/api';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend);

let provinceChart: Chart | null = null;
let strengthChart: Chart | null = null;

export function mountStatsPanel(container: HTMLElement) {
  container.innerHTML = `
    <section class="stats-section">
      <h3>Provinces owned</h3>
      <div class="stats-canvas-wrap"><canvas id="stats-provinces"></canvas></div>
    </section>
    <section class="stats-section">
      <h3>My forces by type</h3>
      <div class="stats-canvas-wrap"><canvas id="stats-strength"></canvas></div>
    </section>`;
  container.classList.add('stats-panel');

  const provincesEl = container.querySelector<HTMLCanvasElement>('#stats-provinces')!;
  const strengthEl  = container.querySelector<HTMLCanvasElement>('#stats-strength')!;

  function rerender() {
    const world = $world.get();
    if (!world) return;
    renderProvincesChart(provincesEl, world);
    renderStrengthChart(strengthEl, world);
  }

  $world.subscribe(rerender);
  rerender();
}

function renderProvincesChart(canvas: HTMLCanvasElement, world: WorldSnapshot) {
  const players = world.players.slice().sort((a, b) => b.ownedProvinceCount - a.ownedProvinceCount);
  const labels = players.map(p => p.nationName);
  const data   = players.map(p => p.ownedProvinceCount);
  const colors = players.map(p => p.flagPrimaryHex);

  if (provinceChart) {
    provinceChart.data.labels = labels;
    provinceChart.data.datasets[0].data = data;
    provinceChart.data.datasets[0].backgroundColor = colors;
    provinceChart.update('none');
    return;
  }

  provinceChart = new Chart(canvas, {
    type: 'bar',
    data: {
      labels,
      datasets: [{
        label: 'Provinces',
        data,
        backgroundColor: colors,
        borderColor: '#0a0a14',
        borderWidth: 1,
      }],
    },
    options: chartOptions(),
  });
}

function renderStrengthChart(canvas: HTMLCanvasElement, world: WorldSnapshot) {
  // Aggregate caller's strength per unit type.
  const totals = new Map<string, number>();
  for (const u of world.myUnits) {
    totals.set(u.type, (totals.get(u.type) ?? 0) + u.strength);
  }
  const entries = Array.from(totals.entries()).sort((a, b) => b[1] - a[1]);
  const labels = entries.map(([t]) => t);
  const data   = entries.map(([, v]) => v);

  if (strengthChart) {
    strengthChart.data.labels = labels;
    strengthChart.data.datasets[0].data = data;
    strengthChart.update('none');
    return;
  }

  strengthChart = new Chart(canvas, {
    type: 'bar',
    data: {
      labels,
      datasets: [{
        label: 'Strength',
        data,
        backgroundColor: '#88a4ff',
        borderColor: '#0a0a14',
        borderWidth: 1,
      }],
    },
    options: chartOptions(),
  });
}

function chartOptions() {
  return {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: { backgroundColor: '#15151c', titleColor: '#ffd166', bodyColor: '#eee' },
    },
    scales: {
      x: { ticks: { color: '#ccc' }, grid: { color: '#2a2a3a' } },
      y: { beginAtZero: true, ticks: { color: '#ccc' }, grid: { color: '#2a2a3a' } },
    },
  } as const;
}
