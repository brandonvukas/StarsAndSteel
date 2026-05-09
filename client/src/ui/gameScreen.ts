// Game screen: boots Phaser, mounts the resource bar + province panel, wires
// the SignalR hub to the diff layer. Owns the live HubConnection lifetime.

import Phaser from 'phaser';
import { BootScene } from '../scenes/BootScene';
import { MapScene } from '../scenes/MapScene';
import { GameHubClient } from '../api/hub';
import { getSnapshot } from '../api/rest';
import {
  $auth, setWorld, patchWorld, bumpTick,
} from '../store/store';
import {
  applyResourcesUpdated, applyUnitMoved, applyUnitDestroyed,
  applyProvinceCaptured, applyBuildingCompleted, applyUnitBuilt,
} from '../diff/applyDiffs';
import { mountResourceBar } from './resourceBar';
import { mountProvincePanel } from './provincePanel';

export async function mountGameScreen(host: HTMLElement, worldId: string) {
  host.innerHTML = `
    <div id="resource-bar"></div>
    <div id="game-body">
      <div id="phaser-host"></div>
      <aside id="side-panel"></aside>
    </div>`;

  // 1. Hydrate from REST snapshot first so the map has data to paint.
  const snapshot = await getSnapshot(worldId);
  setWorld(snapshot);

  // 2. Boot Phaser into the dedicated host.
  new Phaser.Game({
    type: Phaser.AUTO,
    parent: 'phaser-host',
    width: 800,
    height: 600,
    backgroundColor: '#0a0a14',
    scene: [BootScene, MapScene],
  });

  // 3. Mount HUD overlays. Both subscribe to the store and update on diffs.
  mountResourceBar(host.querySelector('#resource-bar')!);
  mountProvincePanel(host.querySelector('#side-panel')!);

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
      onTickAdvanced:      e => {
        bumpTick(e.tick);
        // Reflect tick into world snapshot for the resource bar's "tick N" cell.
        patchWorld(w => ({ ...w, currentTick: e.tick }));
      },
      onReconnected: async () => {
        const fresh = await getSnapshot(worldId);
        setWorld(fresh);
      },
    },
  );

  await hub.connect();
  await hub.joinWorld(worldId);
}
