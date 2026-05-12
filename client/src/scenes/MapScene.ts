// Phaser scene rendering the world map: one filled polygon per province,
// colored by ownership from the live store. Click a polygon to select it.
//
// Geometry comes from @shared/map-data.json (polygons aren't in the snapshot
// to keep the wire payload small). We correlate each map-data row to a
// snapshot province by its (centerX, centerY) pair, which is unique and
// stable on both sides.
//
// Phase 2M: camera pan (drag with right-mouse OR space-held + left-mouse) +
// mouse-wheel zoom around the cursor; smart labels (hide tiny provinces by
// default, font scales inversely with zoom so it stays readable but doesn't
// dominate the map). Click without drag still selects the province.

import Phaser from 'phaser';
import mapData from '@shared/map-data.json';
import { $world, $selectedProvinceId, selectProvince } from '../store/store';
import type { WorldSnapshot } from '../types/api';

const NEUTRAL_FILL = 0x2a2a3a;
const FOG_FILL = 0x12121a;
const SELECT_STROKE = 0xffd166;
const DEFAULT_STROKE = 0x10101a;

// Camera limits. Map data is authored at 1600x1000.
const MIN_ZOOM = 0.6;
const MAX_ZOOM = 6;
const ZOOM_STEP = 1.15;

// Drag detection threshold (pixels) — smaller than this counts as a click.
const DRAG_THRESHOLD = 4;

// Label visibility tuning.
//   - At zoom=1 (default), only labels for provinces with polygon area
//     above LABEL_AREA_AT_BASE are shown. Tiny states get culled.
//   - As you zoom in, the threshold drops linearly so more labels appear.
//   - The font itself scales inversely with zoom so the rendered text size
//     stays roughly constant rather than growing huge when zoomed in.
const LABEL_AREA_AT_BASE = 5500;     // px² at zoom=1
const LABEL_BASE_FONT_PX = 13;
const LABEL_MIN_FONT_PX = 9;
const LABEL_MAX_FONT_PX = 16;

interface ProvincePolygon {
  serverId: string;            // the Guid the snapshot uses
  shape: Phaser.GameObjects.Polygon;
  border: Phaser.GameObjects.Polygon;
  label: Phaser.GameObjects.Text;
  area: number;                // px², used to gate label visibility
}

export class MapScene extends Phaser.Scene {
  private polys = new Map<string, ProvincePolygon>(); // serverId -> bits
  private worldUnsub: (() => void) | null = null;
  private selectionUnsub: (() => void) | null = null;

  // Pointer-drag bookkeeping.
  private dragOriginX = 0;
  private dragOriginY = 0;
  private dragStartScrollX = 0;
  private dragStartScrollY = 0;
  private isDragging = false;
  private dragMovedFar = false;

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

    this.installCameraControls();

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

      const flat = provDef.polygon.flat();
      const fill = NEUTRAL_FILL;
      const area = polygonArea(provDef.polygon as [number, number][]);

      const shape = this.add.polygon(0, 0, flat, fill).setOrigin(0, 0);
      shape.setInteractive(
        new Phaser.Geom.Polygon(flat),
        Phaser.Geom.Polygon.Contains,
      );
      // Click → select, but only if the pointer didn't drag (camera pan).
      shape.on('pointerup', () => {
        if (!this.dragMovedFar) selectProvince(snap.id);
      });

      const border = this.add.polygon(0, 0, flat).setOrigin(0, 0);
      border.setStrokeStyle(2, DEFAULT_STROKE);
      border.setFillStyle();

      const label = this.add.text(provDef.centerX, provDef.centerY, provDef.name, {
        fontSize: `${LABEL_BASE_FONT_PX}px`,
        color: '#e6e6f0',
        fontFamily: 'Inter, system-ui, sans-serif',
        stroke: '#0a0a14',
        strokeThickness: 3,
      }).setOrigin(0.5);
      label.setDepth(10); // above polygons + borders

      this.polys.set(snap.id, { serverId: snap.id, shape, border, label, area });
    }

    this.repaint(world);
    this.applyLabelLod(this.cameras.main.zoom);
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

  // ------ Camera (pan + zoom) ----------------------------------------

  private installCameraControls() {
    const cam = this.cameras.main;
    cam.setZoom(1);

    // Wheel zoom around the cursor (zoom toward / away from cursor pixel).
    this.input.on('wheel', (
      _ptr: Phaser.Input.Pointer,
      _objs: unknown,
      _dx: number, dy: number,
    ) => {
      const oldZoom = cam.zoom;
      const factor = dy < 0 ? ZOOM_STEP : 1 / ZOOM_STEP;
      const newZoom = Phaser.Math.Clamp(oldZoom * factor, MIN_ZOOM, MAX_ZOOM);
      if (newZoom === oldZoom) return;

      // Keep the world-point under the cursor stationary while zooming.
      const pointer = this.input.activePointer;
      const before = cam.getWorldPoint(pointer.x, pointer.y);
      cam.setZoom(newZoom);
      const after = cam.getWorldPoint(pointer.x, pointer.y);
      cam.scrollX += before.x - after.x;
      cam.scrollY += before.y - after.y;

      this.applyLabelLod(newZoom);
    });

    // Drag-to-pan: any mouse button. Province selection happens on `pointerup`
    // and is suppressed if the pointer moved further than DRAG_THRESHOLD px.
    this.input.on('pointerdown', (ptr: Phaser.Input.Pointer) => {
      this.isDragging = true;
      this.dragMovedFar = false;
      this.dragOriginX = ptr.x;
      this.dragOriginY = ptr.y;
      this.dragStartScrollX = cam.scrollX;
      this.dragStartScrollY = cam.scrollY;
    });
    this.input.on('pointermove', (ptr: Phaser.Input.Pointer) => {
      if (!this.isDragging) return;
      const dx = ptr.x - this.dragOriginX;
      const dy = ptr.y - this.dragOriginY;
      if (!this.dragMovedFar &&
          (Math.abs(dx) > DRAG_THRESHOLD || Math.abs(dy) > DRAG_THRESHOLD)) {
        this.dragMovedFar = true;
      }
      if (this.dragMovedFar) {
        cam.scrollX = this.dragStartScrollX - dx / cam.zoom;
        cam.scrollY = this.dragStartScrollY - dy / cam.zoom;
      }
    });
    this.input.on('pointerup', () => {
      this.isDragging = false;
      // dragMovedFar stays true for the duration of the click handler so
      // polygon `pointerup` listeners can suppress selection. Reset on the
      // next pointerdown.
    });

    // Disable browser context menu on right-click so a future right-click
    // pan binding works without a menu popping up.
    this.input.mouse?.disableContextMenu();
  }

  private applyLabelLod(zoom: number) {
    const threshold = LABEL_AREA_AT_BASE / zoom;
    const inverseZoomFont = Phaser.Math.Clamp(
      LABEL_BASE_FONT_PX / zoom, LABEL_MIN_FONT_PX, LABEL_MAX_FONT_PX);
    for (const p of this.polys.values()) {
      const visible = p.area >= threshold;
      p.label.setVisible(visible);
      if (visible && p.label.style.fontSize !== `${inverseZoomFont}px`) {
        p.label.setFontSize(inverseZoomFont);
      }
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

/** Shoelace formula. Returns absolute area in px². */
function polygonArea(points: [number, number][]): number {
  let sum = 0;
  for (let i = 0, n = points.length; i < n; i++) {
    const [x1, y1] = points[i];
    const [x2, y2] = points[(i + 1) % n];
    sum += x1 * y2 - x2 * y1;
  }
  return Math.abs(sum) / 2;
}
