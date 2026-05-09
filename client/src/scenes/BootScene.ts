// Boot scene: minimal — Phaser 4's pipeline is async-warm enough that we
// don't have any explicit assets yet (polygons are vector-drawn). This scene
// exists as the canonical entry point so future asset preloading slots in
// without re-wiring main.ts.

import Phaser from 'phaser';

export class BootScene extends Phaser.Scene {
  constructor() {
    super({ key: 'BootScene' });
  }

  create() {
    this.scene.start('MapScene');
  }
}
