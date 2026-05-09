// Game screen: boots Phaser, mounts the resource bar + province panel, wires
// the SignalR hub to the diff layer. Owns the live HubConnection lifetime.

import Phaser from 'phaser';
import { BootScene } from '../scenes/BootScene';
import { MapScene } from '../scenes/MapScene';
import { GameHubClient } from '../api/hub';
import { getSnapshot, getNews } from '../api/rest';
import {
  $auth, setWorld, patchWorld, bumpTick, pushNews, setNews,
} from '../store/store';
import {
  applyResourcesUpdated, applyUnitMoved, applyUnitDestroyed,
  applyProvinceCaptured, applyBuildingCompleted, applyUnitBuilt,
} from '../diff/applyDiffs';
import { mountResourceBar } from './resourceBar';
import { mountProvincePanel } from './provincePanel';
import { mountNewsTicker } from './newsTicker';

export async function mountGameScreen(host: HTMLElement, worldId: string) {
  host.innerHTML = `
    <div id="resource-bar"></div>
    <div id="game-body">
      <div id="phaser-host"></div>
      <aside id="side-panel"></aside>
    </div>
    <div id="news-ticker"></div>`;

  // 1. Hydrate from REST snapshot first so the map has data to paint.
  const snapshot = await getSnapshot(worldId);
  setWorld(snapshot);

  // 1b. Backfill any prior headlines so the ticker isn't empty on join.
  try {
    const news = await getNews(worldId, 0);
    setNews(news);
  } catch {
    // News history is best-effort; failure here mustn't block the screen.
  }

  // 2. Boot Phaser into the dedicated host. Canvas is sized to match the
  //    map-data viewport (1600x1000 from scripts/build-map.mjs). Phaser scales
  //    the canvas to fit the parent via Scale.FIT so the full map is visible
  //    no matter the window size; the internal coordinate system stays
  //    constant which keeps polygon hit-tests aligned with the data.
  new Phaser.Game({
    type: Phaser.AUTO,
    parent: 'phaser-host',
    width: 1600,
    height: 1000,
    backgroundColor: '#0a0a14',
    scale: {
      mode: Phaser.Scale.FIT,
      autoCenter: Phaser.Scale.CENTER_BOTH,
    },
    scene: [BootScene, MapScene],
  });

  // 3. Mount HUD overlays. All three subscribe to the store and update on diffs.
  mountResourceBar(host.querySelector('#resource-bar')!);
  mountProvincePanel(host.querySelector('#side-panel')!);
  mountNewsTicker(host.querySelector('#news-ticker')!);

  // 4. Connect SignalR. Diff handlers patch the store; on reconnect we
  //    re-snapshot per docs/06 because we may have missed events.
  const hub = new GameHubClient(
    () => $auth.get()?.accessToken ?? null,
    {
      onResourcesUpdated:  e => patchWorld(w => applyResourcesUpdated(w, e)),
      onUnitMoved:         e => patchWorld(w => applyUnitMoved(w, e)),
      onUnitDestroyed:     e => patchWorld(w => applyUnitDestroyed(w, e)),
      onProvinceCaptured:  e => patchWorld(w => applyProvinceCaptured(w, e)),
      onBuildingCompleted: e => patchWorld(w => applyBuildingCompleted(w, e)),
      onUnitBuilt:         e => patchWorld(w => applyUnitBuilt(w, e)),
      onNewsPublished:     e => pushNews({
        id: e.newsItemId,
        tick: e.tick,
        headline: e.headline,
        body: e.body,
        severity: e.severity,
        category: e.category,
        relatedPlayerId: e.relatedPlayerId,
      }),
      onTickAdvanced:      e => {
        bumpTick(e.tick);
        // Reflect tick into world snapshot for the resource bar's "tick N" cell.
        patchWorld(w => ({ ...w, currentTick: e.tick }));
      },
      onReconnected: async () => {
        const fresh = await getSnapshot(worldId);
        setWorld(fresh);
        // Backfill news we missed during the disconnect.
        try {
          const news = await getNews(worldId, 0);
          setNews(news);
        } catch {
          // Best-effort.
        }
      },
    },
  );

  await hub.connect();
  await hub.joinWorld(worldId);
}
