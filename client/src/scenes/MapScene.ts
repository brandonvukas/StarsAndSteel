// Phaser scene rendering the world map: one filled polygon per province,
// colored by ownership from the live store. Click a polygon to select it.
//
// Geometry comes from @shared/map-data.json (polygons aren't in the snapshot
// to keep the wire payload small). We correlate each map-data row to a
// snapshot province by its (centerX, centerY) pair, which is unique and
// stable on both sides.

import Phaser from 'phaser';
import mapData from '@shared/map-data.json';
import { $world, $selectedProvinceId, selectProvince } from '../store/store';
import type { WorldSnapshot } from '../types/api';

const NEUTRAL_FILL = 0x2a2a3a;
const FOG_FILL = 0x12121a;
const SELECT_STROKE = 0xffd166;
const DEFAULT_STROKE = 0x10101a;

interface ProvincePolygon {
  serverId: string;            // the Guid the snapshot uses
  shape: Phaser.GameObjects.Polygon;
  border: Phaser.GameObjects.Polygon;
}

export class MapScene extends Phaser.Scene {
  private polys = new Map<string, ProvincePolygon>(); // serverId -> bits
  private worldUnsub: (() => void) | null = null;
  private selectionUnsub: (() => void) | null = null;

  constructor() {
    super({ key: 'MapScene' });
  }

  create() {
    this.cameras.main.setBackgroundColor('#0a0a14');

    // Subscribe AFTER we've placed polygons so the first redraw paints them.
    this.placePolygons();

    this.worldUnsub = $world.subscribe(world => {
      if (world) this.repaint(world);
    });
    this.selectionUnsub = $selectedProvinceId.subscribe(id => this.highlight(id));

    this.events.once(Phaser.Scenes.Events.SHUTDOWN, () => {
      this.worldUnsub?.();
      this.selectionUnsub?.();
    });
  }

  private placePolygons() {
    const world = $world.get();
    if (!world) return;

    for (const provDef of mapData.provinces) {
      const snap = matchByCenter(world, provDef.centerX, provDef.centerY);
      if (!snap) continue;

      // Phaser.Polygon expects a flat array of x,y pairs OR an array of {x,y}.
      // The shape is positioned at (0,0) and its points are absolute coords.
      const flat = provDef.polygon.flat();
      const fill = NEUTRAL_FILL;

      const shape = this.add.polygon(0, 0, flat, fill).setOrigin(0, 0);
      shape.setInteractive(
        new Phaser.Geom.Polygon(flat),
        Phaser.Geom.Polygon.Contains,
      );
      shape.on('pointerdown', () => selectProvince(snap.id));

      const border = this.add.polygon(0, 0, flat).setOrigin(0, 0);
      border.setStrokeStyle(2, DEFAULT_STROKE);
      border.setFillStyle();

      // Province name label centered.
      this.add.text(provDef.centerX, provDef.centerY, provDef.name, {
        fontSize: '14px', color: '#e6e6f0', fontFamily: 'sans-serif',
      }).setOrigin(0.5);

      this.polys.set(snap.id, { serverId: snap.id, shape, border });
    }

    this.repaint(world);
  }

  private repaint(world: WorldSnapshot) {
    for (const [id, p] of this.polys) {
      const snap = world.provinces.find(s => s.id === id);
      if (!snap) continue;
      let fill = FOG_FILL;
      if (snap.visible) fill = NEUTRAL_FILL;
      if (snap.ownerColorHex) fill = parseHex(snap.ownerColorHex);
      p.shape.setFillStyle(fill);
    }
  }

  private highlight(selectedId: string | null) {
    for (const [id, p] of this.polys) {
      p.border.setStrokeStyle(id === selectedId ? 3 : 2,
        id === selectedId ? SELECT_STROKE : DEFAULT_STROKE);
    }
  }
}

function matchByCenter(world: WorldSnapshot, cx: number, cy: number) {
  // Tolerate float jitter from JSON round-trip.
  return world.provinces.find(p =>
    Math.abs(p.centerX - cx) < 0.5 && Math.abs(p.centerY - cy) < 0.5);
}

function parseHex(hex: string): number {
  // Accept #rrggbb or rrggbb
  return parseInt(hex.replace('#', ''), 16);
}
