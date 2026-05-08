// Type declarations for shared/map-data.json.
// Imported by the client as `import mapData from '@shared/map-data.json'`.
// Consumed by the server at migration time via MapSeeder (System.Text.Json).
// Both sides MUST agree on this shape.

declare module '@shared/map-data.json' {
  const data: MapData;
  export default data;
}

export type ProvinceType =
  | 'Urban'
  | 'Industrial'
  | 'Tech'
  | 'Agricultural'
  | 'Resource'
  | 'Capital';

export interface ResourceOutput {
  moneyPerTick: number;
  oilPerTick: number;
  steelPerTick: number;
  electronicsPerTick: number;
  foodPerTick: number;
  manpowerPerTick: number;
}

export interface ProvinceData {
  /** Stable string id used in the source map file. Server maps this to a Guid at seed time. */
  id: string;
  name: string;
  type: ProvinceType;
  isCoastal: boolean;
  centerX: number;
  centerY: number;
  basePopulation: number;
  baseResourceOutput: ResourceOutput;
  /** Closed polygon as [x, y] pairs in map-space. */
  polygon: [number, number][];
}

export interface AdjacencyData {
  /** Invariant: provinceAId < provinceBId (lexical). Stored once per edge. */
  provinceAId: string;
  provinceBId: string;
  /** Movement cost multiplier; 1.0 = normal terrain. */
  terrainCost: number;
  /** True when the edge is open water; only naval/air units may cross. */
  isSeaCrossing: boolean;
}

export interface MapData {
  version: number;
  provinces: ProvinceData[];
  adjacencies: AdjacencyData[];
}
