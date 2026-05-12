// Game screen: boots Phaser, mounts the resource bar + province panel, wires
// the SignalR hub to the diff layer. Owns the live HubConnection lifetime.

import Phaser from 'phaser';
import { BootScene } from '../scenes/BootScene';
import { MapScene } from '../scenes/MapScene';
import { GameHubClient } from '../api/hub';
import { getSnapshot, getNews, getDiplomacy, getResearch, getChatHistory } from '../api/rest';
import {
  $auth, setWorld, patchWorld, bumpTick, pushNews, setNews,
  setDiplomacy, $diplomacy, applyRelationChanged, applyOfferReceived, applyOfferResolved,
  setResearch, $research, applyResearchStarted, applyTechUnlocked, tickResearchProgress,
  setChat, pushChat,
} from '../store/store';
import {
  applyResourcesUpdated, applyUnitMoved, applyUnitDestroyed,
  applyProvinceCaptured, applyBuildingCompleted, applyUnitBuilt,
} from '../diff/applyDiffs';
import { mountResourceBar } from './resourceBar';
import { mountProvincePanel } from './provincePanel';
import { mountNewsTicker } from './newsTicker';
import { mountDiplomacyPanel } from './diplomacyPanel';
import { mountResearchPanel } from './researchPanel';
import { mountChatPanel } from './chatPanel';
import { mountStatsPanel } from './statsPanel';
import { mountSettingsPanel } from './settingsPanel';

export async function mountGameScreen(host: HTMLElement, worldId: string) {
  host.innerHTML = `
    <div id="resource-bar"></div>
    <div id="game-body">
      <div id="phaser-host"></div>
      <aside id="side-panel">
        <nav class="side-tabs">
          <button data-tab="province" class="active">Province</button>
          <button data-tab="diplomacy">Diplomacy</button>
          <button data-tab="research">Research</button>
          <button data-tab="chat">Chat</button>
          <button data-tab="stats">Stats</button>
          <button data-tab="settings">Settings</button>
        </nav>
        <div id="side-tab-province" class="side-tab-pane"></div>
        <div id="side-tab-diplomacy" class="side-tab-pane" hidden></div>
        <div id="side-tab-research" class="side-tab-pane" hidden></div>
        <div id="side-tab-chat" class="side-tab-pane" hidden></div>
        <div id="side-tab-stats" class="side-tab-pane" hidden></div>
        <div id="side-tab-settings" class="side-tab-pane" hidden></div>
      </aside>
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

  // 1c. Initial diplomacy state. Best-effort like news; the panel will show a
  //    "Loading..." hint if it fails and recover on the next hub event.
  try {
    setDiplomacy(await getDiplomacy(worldId));
  } catch {
    // ignored
  }

  // 1d. Initial research state — catalog + caller's per-tech progress.
  try {
    setResearch(await getResearch(worldId));
  } catch {
    // ignored
  }

  // 1e. Initial chat backfill (most recent visible-to-caller messages).
  try {
    setChat(await getChatHistory(worldId));
  } catch {
    // ignored
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
  mountProvincePanel(host.querySelector('#side-tab-province')!);
  mountDiplomacyPanel(host.querySelector('#side-tab-diplomacy')!);
  mountResearchPanel(host.querySelector('#side-tab-research')!);
  mountChatPanel(host.querySelector('#side-tab-chat')!);
  mountStatsPanel(host.querySelector('#side-tab-stats')!);
  mountSettingsPanel(host.querySelector('#side-tab-settings')!);
  mountNewsTicker(host.querySelector('#news-ticker')!);
  wireSideTabs(host);

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
      onRelationChanged:   e => {
        const cur = $diplomacy.get();
        if (cur) setDiplomacy(applyRelationChanged(cur, e));
      },
      onOfferReceived:     e => {
        const cur = $diplomacy.get();
        if (cur) setDiplomacy(applyOfferReceived(cur, e));
      },
      onOfferResolved:     e => {
        const cur = $diplomacy.get();
        if (cur) setDiplomacy(applyOfferResolved(cur, e));
      },
      onResearchStarted:   e => {
        const cur = $research.get();
        if (cur) setResearch(applyResearchStarted(cur, e));
      },
      onTechUnlocked:      e => {
        const cur = $research.get();
        if (cur) setResearch(applyTechUnlocked(cur, e));
      },
      onChatMessageReceived: e => pushChat(e),
      onTickAdvanced:      e => {
        bumpTick(e.tick);
        // Reflect tick into world snapshot for the resource bar's "tick N" cell.
        patchWorld(w => ({ ...w, currentTick: e.tick }));
        // Bump local research progress by 1 per tick so the bars animate without
        // a per-tick fetch. Server is authoritative — TechUnlocked corrects any drift.
        const r = $research.get();
        if (r) setResearch(tickResearchProgress(r));
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
        // Backfill diplomacy too — relation/offer changes during the disconnect
        // would otherwise leave the panel stale.
        try {
          setDiplomacy(await getDiplomacy(worldId));
        } catch {
          // ignored
        }
        try {
          setResearch(await getResearch(worldId));
        } catch {
          // ignored
        }
        try {
          setChat(await getChatHistory(worldId));
        } catch {
          // ignored
        }
      },
    },
  );

  await hub.connect();
  await hub.joinWorld(worldId);
}

function wireSideTabs(host: HTMLElement) {
  const tabs = host.querySelectorAll<HTMLButtonElement>('.side-tabs button[data-tab]');
  const panes = {
    province: host.querySelector<HTMLElement>('#side-tab-province')!,
    diplomacy: host.querySelector<HTMLElement>('#side-tab-diplomacy')!,
    research: host.querySelector<HTMLElement>('#side-tab-research')!,
    chat: host.querySelector<HTMLElement>('#side-tab-chat')!,
    stats: host.querySelector<HTMLElement>('#side-tab-stats')!,
    settings: host.querySelector<HTMLElement>('#side-tab-settings')!,
  };
  tabs.forEach(btn => {
    btn.onclick = () => {
      tabs.forEach(b => b.classList.toggle('active', b === btn));
      const which = btn.dataset.tab as keyof typeof panes;
      Object.entries(panes).forEach(([key, el]) => {
        el.hidden = key !== which;
      });
    };
  });
}
