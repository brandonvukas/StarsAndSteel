// Stars & Steel — build-map.mjs
//
// Generates shared/map-data.json from Natural Earth admin_1 (states/provinces).
// Output is committed to the repo so the JSON is reproducible without anyone
// having to install Node tooling. Run from `scripts/`:
//
//   npm install
//   npm run build:map
//
// What it does, in order:
//   1. Fetch Natural Earth admin_1 1:50m GeoJSON (cached to .cache/).
//   2. Filter to US / Canada / Mexico features.
//   3. For Canada and Mexico, merge admin_1 features into ~5 and ~3 named blocs
//      so the map doesn't drown in 13 provinces + 32 estados the player has
//      no story reason to care about. US gets 48 contiguous + AK + HI = 50.
//   4. Project all geometry through d3-geo Albers (NA-centered) → 1600x1000
//      pixel viewport with ~40 px padding.
//   5. Simplify each polygon with turf at a tolerance tuned for ~80-150 vertices
//      per province (enough to look like the real shape; small enough that the
//      JSON stays under ~250KB).
//   6. Compute centroids → centerX/centerY for the snapshot index match.
//   7. Compute land adjacencies via shared-segment detection on the simplified
//      polygons (snap-to-grid + segment hash). Plus a small hand-listed set of
//      sea crossings (HI↔CA, AK↔WA blocs etc.) so islands aren't unreachable
//      once we ship naval units.
//   8. Assign ProvinceType (USER REQ: every province is Capital) and a base
//      resource output appropriate to the real economy (CA tech, TX oil,
//      midwest agriculture, etc.). Fallback to a neutral profile.
//   9. Emit shared/map-data.json with stable kebab-case ids.

import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import * as d3geo from 'd3-geo';
import * as turf from '@turf/turf';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..');
const cacheDir = path.join(here, '.cache');
const outPath = path.join(repoRoot, 'shared', 'map-data.json');

// Natural Earth admin_1 states/provinces, 1:10m. Public domain.
// Mirrored on the nvkelso/natural-earth-vector GitHub repo.
//
// We use 10m (not 50m) because 50m only ships admin_1 polygons for nine large
// countries — Mexico is missing. 10m has full global coverage of all
// states/provinces. The file is ~40 MB but is cached locally to .cache/, so
// network cost is one-time. Output JSON size is unaffected (we project +
// simplify to ~1.5px tolerance regardless of source detail).
const NE_URL = 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_1_states_provinces.geojson';

// Output viewport.
const WIDTH = 1600;
const HEIGHT = 1000;
const PADDING = 40;

// Simplification tolerance — units = whatever the input projection produces.
// We project FIRST, then simplify in pixel space, so this is a pixel value.
const SIMPLIFY_PIXELS = 1.5;

// Adjacency segment-hash quantization. After projection (but BEFORE
// simplification) two provinces share a border iff they emit the same
// quantized segment. We snap to a 0.5px grid: tight enough to avoid
// false positives between non-touching states, loose enough to absorb the
// floating-point noise that d3-geo's projection introduces along shared
// borders that are mathematically identical in the source geometry.
//
// CRITICAL: adjacency runs on the unsimplified rings. Independent
// simplification of two polygons along a shared border produces vertex
// sets that rarely match, which would silently drop almost every
// adjacency. Detection happens first, simplification is for rendering only.
const SEGMENT_QUANT = 0.5;

// Sea crossings (only land-adjacency detection runs above; these patch the
// island-isolation cases). Kept as id-pairs in the final id space (kebab-case).
const SEA_CROSSINGS = [
  // Hawaii hangs off the California coast — naval/air-only path until ports ship.
  ['hawaii', 'california', 6.0],
  ['hawaii', 'oregon', 6.0],
  // Alaska connects via the inside passage to BC bloc; admin_1 alone misses this
  // because the polygons don't touch in the projected pixel space.
  ['alaska', 'canada-west', 4.0],
  // Florida tip → Cuba would go here once we add the Caribbean.
];

// Bloc definitions for Canada + Mexico. Keys are the new merged-province ids
// (kebab-case). Values are admin_1 names (Natural Earth `name` field).
const CANADA_BLOCS = {
  'canada-west':     ['British Columbia', 'Yukon', 'Northwest Territories'],
  'canada-prairies': ['Alberta', 'Saskatchewan', 'Manitoba'],
  'canada-ontario':  ['Ontario', 'Nunavut'],
  'canada-quebec':   ['Quebec'],
  'canada-atlantic': ['New Brunswick', 'Nova Scotia', 'Prince Edward Island', 'Newfoundland and Labrador'],
};

const MEXICO_BLOCS = {
  'mexico-north':   [
    'Baja California', 'Baja California Sur', 'Sonora', 'Chihuahua', 'Coahuila',
    'Nuevo León', 'Tamaulipas', 'Sinaloa', 'Durango',
  ],
  'mexico-central': [
    'Nayarit', 'Jalisco', 'Aguascalientes', 'Zacatecas', 'San Luis Potosí',
    'Guanajuato', 'Querétaro', 'Hidalgo', 'México', 'Distrito Federal',
    'Morelos', 'Tlaxcala', 'Puebla', 'Veracruz', 'Michoacán',
    'Colima',
  ],
  'mexico-south':   [
    'Guerrero', 'Oaxaca', 'Chiapas', 'Tabasco', 'Campeche', 'Yucatán',
    'Quintana Roo',
  ],
};

// Resource profile by archetype. All provinces are ProvinceType.Capital per
// user requirement, but resource output still varies by real-world economy so
// gameplay has texture (Texas oil, California tech, Iowa food, etc.).
const PROFILES = {
  'tech':         { moneyPerTick: 90, oilPerTick: 10, steelPerTick: 15, electronicsPerTick: 60, foodPerTick: 20, manpowerPerTick: 35 },
  'finance':      { moneyPerTick: 120, oilPerTick: 5, steelPerTick: 15, electronicsPerTick: 35, foodPerTick: 15, manpowerPerTick: 40 },
  'industrial':   { moneyPerTick: 70, oilPerTick: 15, steelPerTick: 55, electronicsPerTick: 25, foodPerTick: 20, manpowerPerTick: 45 },
  'oil':          { moneyPerTick: 80, oilPerTick: 60, steelPerTick: 25, electronicsPerTick: 15, foodPerTick: 20, manpowerPerTick: 30 },
  'agricultural': { moneyPerTick: 50, oilPerTick: 10, steelPerTick: 15, electronicsPerTick: 10, foodPerTick: 60, manpowerPerTick: 25 },
  'resource':     { moneyPerTick: 55, oilPerTick: 35, steelPerTick: 40, electronicsPerTick: 10, foodPerTick: 25, manpowerPerTick: 25 },
  'urban':        { moneyPerTick: 95, oilPerTick: 8, steelPerTick: 25, electronicsPerTick: 35, foodPerTick: 18, manpowerPerTick: 50 },
  'mixed':        { moneyPerTick: 65, oilPerTick: 20, steelPerTick: 25, electronicsPerTick: 20, foodPerTick: 25, manpowerPerTick: 30 },
};

// Profile assignment per state/bloc. Anything missing falls back to 'mixed'.
const STATE_PROFILES = {
  // USA — flavor by real-world economy
  'california': 'tech', 'washington': 'tech', 'massachusetts': 'tech',
  'new-york': 'finance', 'connecticut': 'finance', 'illinois': 'finance',
  'texas': 'oil', 'oklahoma': 'oil', 'louisiana': 'oil', 'alaska': 'oil', 'north-dakota': 'oil',
  'pennsylvania': 'industrial', 'ohio': 'industrial', 'michigan': 'industrial', 'indiana': 'industrial',
  'iowa': 'agricultural', 'nebraska': 'agricultural', 'kansas': 'agricultural', 'south-dakota': 'agricultural',
  'minnesota': 'agricultural', 'wisconsin': 'agricultural', 'arkansas': 'agricultural', 'mississippi': 'agricultural',
  'missouri': 'agricultural', 'kentucky': 'agricultural',
  'florida': 'urban', 'georgia': 'urban', 'north-carolina': 'urban', 'virginia': 'urban',
  'new-jersey': 'urban', 'maryland': 'urban',
  'colorado': 'mixed', 'arizona': 'mixed', 'utah': 'mixed', 'nevada': 'resource',
  'wyoming': 'resource', 'montana': 'resource', 'idaho': 'resource', 'new-mexico': 'resource',
  'oregon': 'mixed', 'tennessee': 'mixed', 'alabama': 'industrial', 'south-carolina': 'mixed',
  'west-virginia': 'resource', 'maine': 'resource', 'vermont': 'agricultural',
  'new-hampshire': 'mixed', 'rhode-island': 'urban', 'delaware': 'finance', 'hawaii': 'mixed',
  // Canada blocs
  'canada-west': 'resource', 'canada-prairies': 'agricultural',
  'canada-ontario': 'industrial', 'canada-quebec': 'urban', 'canada-atlantic': 'resource',
  // Mexico blocs
  'mexico-north': 'oil', 'mexico-central': 'urban', 'mexico-south': 'agricultural',
};

// Coastal flag per id. Used by the Naval phase later. Hand-curated; misses
// here just mean a state can't host coastal-only buildings, which is benign.
const COASTAL = new Set([
  'california', 'oregon', 'washington', 'alaska', 'hawaii',
  'texas', 'louisiana', 'mississippi', 'alabama', 'florida',
  'georgia', 'south-carolina', 'north-carolina', 'virginia',
  'maryland', 'delaware', 'new-jersey', 'new-york', 'connecticut',
  'rhode-island', 'massachusetts', 'new-hampshire', 'maine',
  'canada-west', 'canada-ontario', 'canada-quebec', 'canada-atlantic',
  'mexico-north', 'mexico-central', 'mexico-south',
]);

// ---------------------------------------------------------------- helpers ---

function kebab(s) {
  return s
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')   // strip diacritics
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

function basePopulationFor(profile, areaPx) {
  // Rough population scaling: tied to projected area + profile base. The actual
  // value isn't surfaced to UI in MVP, but ResourceProductionStep + future
  // attrition use it, so plausibility matters.
  const base = {
    tech: 8e6, finance: 12e6, industrial: 7e6, oil: 5e6,
    agricultural: 3e6, resource: 1.5e6, urban: 9e6, mixed: 4e6,
  }[profile] ?? 4e6;
  // areaPx ~ 100-50000; normalize roughly so a small state still ~ 1M.
  const scale = Math.max(0.3, Math.log10(Math.max(areaPx, 100)) - 1.5);
  return Math.round(base * scale);
}

async function fetchCached(url, filename) {
  if (!existsSync(cacheDir)) await mkdir(cacheDir, { recursive: true });
  const p = path.join(cacheDir, filename);
  if (existsSync(p)) {
    return JSON.parse(await readFile(p, 'utf8'));
  }
  console.log(`  fetching ${url}`);
  const res = await fetch(url);
  if (!res.ok) throw new Error(`fetch ${url} → ${res.status}`);
  const text = await res.text();
  await writeFile(p, text, 'utf8');
  return JSON.parse(text);
}

// Project a GeoJSON geometry through d3-geo. The projection is configured
// once on the full geometry collection so fitExtent works against the union.
function makeProjection(features) {
  const fc = { type: 'FeatureCollection', features };
  const projection = d3geo.geoAlbers()
    .rotate([96, 0])         // center on ~96°W (mid-USA)
    .center([0, 38])
    .parallels([29.5, 45.5])
    .fitExtent([[PADDING, PADDING], [WIDTH - PADDING, HEIGHT - PADDING]], fc);
  return projection;
}

// Convert a GeoJSON geometry's coords (lon,lat) → projected (x,y) pixels
// in-place style (returns a new geometry). Handles Polygon and MultiPolygon.
function projectGeometry(geom, projection) {
  function projRing(ring) {
    return ring.map(([lon, lat]) => {
      const p = projection([lon, lat]);
      // d3 returns null for points outside the projection clip.
      return p ? [round2(p[0]), round2(p[1])] : null;
    }).filter(p => p !== null);
  }
  if (geom.type === 'Polygon') {
    return { type: 'Polygon', coordinates: geom.coordinates.map(projRing) };
  }
  if (geom.type === 'MultiPolygon') {
    return {
      type: 'MultiPolygon',
      coordinates: geom.coordinates.map(poly => poly.map(projRing)),
    };
  }
  throw new Error(`unsupported geometry type ${geom.type}`);
}

function round2(n) { return Math.round(n * 100) / 100; }

// Pick the largest ring of a (possibly Multi)Polygon as the representative
// outer ring for rendering. Phaser draws a single polygon, not a multipolygon,
// so for islands like Hawaii we just take the largest island. Good enough for MVP.
function largestRing(geom) {
  let best = null;
  let bestArea = -1;
  const polys = geom.type === 'MultiPolygon' ? geom.coordinates : [geom.coordinates];
  for (const poly of polys) {
    const outer = poly[0]; // first ring is the outer ring per GeoJSON spec
    const area = Math.abs(turf.area(turf.polygon([outer])));
    if (area > bestArea) { bestArea = area; best = outer; }
  }
  return best;
}

function pixelArea(ring) {
  // Shoelace.
  let s = 0;
  for (let i = 0, n = ring.length - 1; i < n; i++) {
    s += ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1];
  }
  return Math.abs(s) / 2;
}

// Ensure a ring is counter-clockwise (positive shoelace). Phaser doesn't care
// for fills but it's a good sanity invariant and matches GeoJSON exterior order.
function ensureCcw(ring) {
  let s = 0;
  for (let i = 0, n = ring.length - 1; i < n; i++) {
    s += (ring[i + 1][0] - ring[i][0]) * (ring[i + 1][1] + ring[i][1]);
  }
  // s > 0 means clockwise in screen-y-down coordinates; we want CCW (s < 0).
  return s > 0 ? ring.slice().reverse() : ring;
}

// Centroid of a flat ring (closed). Average of vertices is fine for MVP labels.
function centroid(ring) {
  let cx = 0, cy = 0, n = ring.length - 1; // exclude duplicate close-vertex
  for (let i = 0; i < n; i++) { cx += ring[i][0]; cy += ring[i][1]; }
  return [round2(cx / n), round2(cy / n)];
}

// Quantized segment hash: a segment between two snapped points, direction-
// agnostic. Two provinces share a segment iff they both emit the same key.
function segmentKey(a, b, q = SEGMENT_QUANT) {
  const ax = Math.round(a[0] / q) * q;
  const ay = Math.round(a[1] / q) * q;
  const bx = Math.round(b[0] / q) * q;
  const by = Math.round(b[1] / q) * q;
  // Sort endpoints so direction doesn't matter.
  const lo = ax < bx || (ax === bx && ay < by) ? [ax, ay] : [bx, by];
  const hi = lo[0] === ax && lo[1] === ay ? [bx, by] : [ax, ay];
  return `${lo[0]},${lo[1]}|${hi[0]},${hi[1]}`;
}

// Walk every edge of a polygon ring and emit its segment keys.
function* segmentsOf(ring) {
  for (let i = 0, n = ring.length - 1; i < n; i++) {
    yield segmentKey(ring[i], ring[i + 1]);
  }
}

// Walk every segment of every ring (outer + holes) of every polygon in a
// (Multi)Polygon geometry. Used for adjacency detection on the full
// pre-simplification projected geometry.
function* allSegmentsOfGeometry(geom) {
  const polys = geom.type === 'MultiPolygon' ? geom.coordinates : [geom.coordinates];
  for (const poly of polys) {
    for (const ring of poly) {
      // Ring may or may not be closed; segment loop tolerates both since we
      // iterate i < ring.length - 1. If unclosed we'd miss the closing
      // segment, but neighbors would also miss it, so adjacency is symmetric.
      for (let i = 0, n = ring.length - 1; i < n; i++) {
        yield segmentKey(ring[i], ring[i + 1]);
      }
    }
  }
}

// Simplify a single ring with turf.simplify, pixel-tolerance.
function simplifyRing(ring, tol = SIMPLIFY_PIXELS) {
  if (ring.length < 6) return ring; // already minimal
  // turf wants a Polygon Feature.
  const poly = turf.polygon([ring]);
  const simple = turf.simplify(poly, { tolerance: tol, highQuality: true });
  // simplify can occasionally collapse a tiny ring to nothing; in that case
  // fall back to the original.
  const out = simple.geometry.coordinates[0];
  return out.length >= 4 ? out : ring;
}

// Merge multiple admin_1 features (same country) into one MultiPolygon. We
// keep them as a MultiPolygon so the largestRing / area logic stays uniform.
function mergeFeatures(features) {
  const polys = [];
  for (const f of features) {
    const g = f.geometry;
    if (g.type === 'Polygon') polys.push(g.coordinates);
    else if (g.type === 'MultiPolygon') polys.push(...g.coordinates);
  }
  return { type: 'MultiPolygon', coordinates: polys };
}

// ---------------------------------------------------------------------- main

async function main() {
  console.log('Stars & Steel map builder');

  // 1. Fetch + parse Natural Earth.
  console.log('1. fetching Natural Earth admin_1 …');
  const ne = await fetchCached(NE_URL, 'ne_10m_admin_1_states_provinces.geojson');
  console.log(`   loaded ${ne.features.length} admin_1 features`);

  // 2. Build the working feature list.
  console.log('2. filtering + merging features …');
  const features = []; // { id, name, geom (lon/lat), profile }

  // 2a. USA — keep each state as its own feature.
  const usFeatures = ne.features.filter(f => f.properties.iso_a2 === 'US');
  for (const f of usFeatures) {
    const name = f.properties.name;
    const id = kebab(name);
    if (!STATE_PROFILES[id] && !['district-of-columbia'].includes(id)) {
      // Unmapped US territory (Puerto Rico, Guam, etc) — drop. We're MVP-scoped
      // to states + AK + HI per the user's "USA + neighbors" answer.
      continue;
    }
    if (id === 'district-of-columbia') continue; // too small to bother
    features.push({ id, name, geom: f.geometry, profile: STATE_PROFILES[id] ?? 'mixed' });
  }

  // 2b. Canada — merge into the 5 blocs.
  // Match by kebab(name) so diacritics ('Québec') and case differences are
  // handled uniformly. Failing to match silently was the primary failure mode
  // of the original strict-string approach.
  for (const [id, members] of Object.entries(CANADA_BLOCS)) {
    const wanted = new Set(members.map(kebab));
    const matched = ne.features.filter(f =>
      f.properties.admin === 'Canada' && wanted.has(kebab(f.properties.name ?? '')));
    if (matched.length !== members.length) {
      const got = matched.map(f => f.properties.name);
      console.warn(`   WARN: bloc ${id} expected ${members.length} matched ${matched.length} (got: ${got.join(', ') || '<none>'})`);
    }
    if (matched.length === 0) continue;
    features.push({
      id,
      name: blocDisplayName(id),
      geom: mergeFeatures(matched),
      profile: STATE_PROFILES[id] ?? 'mixed',
    });
  }

  // 2c. Mexico — merge into the 3 blocs. Same kebab-normalized matching.
  for (const [id, members] of Object.entries(MEXICO_BLOCS)) {
    const wanted = new Set(members.map(kebab));
    const matched = ne.features.filter(f =>
      f.properties.admin === 'Mexico' && wanted.has(kebab(f.properties.name ?? '')));
    if (matched.length !== members.length) {
      const got = matched.map(f => f.properties.name);
      console.warn(`   WARN: bloc ${id} expected ${members.length} matched ${matched.length} (got: ${got.join(', ') || '<none>'})`);
    }
    if (matched.length === 0) continue;
    features.push({
      id,
      name: blocDisplayName(id),
      geom: mergeFeatures(matched),
      profile: STATE_PROFILES[id] ?? 'mixed',
    });
  }

  console.log(`   total features after merge: ${features.length}`);

  // 3. Project everything through one Albers (NA-centered).
  console.log('3. projecting through Albers …');
  const proj = makeProjection(features.map(f => ({ type: 'Feature', properties: {}, geometry: f.geom })));
  for (const f of features) {
    f.projGeom = projectGeometry(f.geom, proj);
  }

  // 4. For each feature: pick the largest ring (representative outline for
  //    Phaser rendering), simplify it, ensure CCW, compute centroid + area
  //    + base population. The full unsimplified projected geometry is kept
  //    on f.projGeom for the adjacency pass.
  console.log('4. simplifying outer rings …');
  for (const f of features) {
    let ring = largestRing(f.projGeom);
    // turf wants closed rings (last == first). NE polygons usually are.
    if (ring[0][0] !== ring[ring.length - 1][0] || ring[0][1] !== ring[ring.length - 1][1]) {
      ring = [...ring, ring[0]];
    }
    ring = simplifyRing(ring);
    ring = ensureCcw(ring);
    f.ring = ring;
    f.area = pixelArea(ring);
    [f.cx, f.cy] = centroid(ring);
    f.basePopulation = basePopulationFor(f.profile, f.area);
  }

  // Sort smallest-first so the click hit-test in Phaser favors smaller states
  // (added later, they sit on top of larger states they're adjacent to).
  features.sort((a, b) => b.area - a.area);

  // 5. Compute land adjacencies via shared segments. Walk EVERY ring of the
  //    full projected MultiPolygon (not just the simplified largest ring) so
  //    border vertices match exactly between neighbors. Independent
  //    simplification per-province would destroy these matches.
  console.log('5. computing land adjacencies …');
  const segIndex = new Map(); // segmentKey -> Set<provinceId>
  for (const f of features) {
    const seen = new Set();
    for (const k of allSegmentsOfGeometry(f.projGeom)) {
      if (seen.has(k)) continue; // a province may double up on a segment; ignore
      seen.add(k);
      let arr = segIndex.get(k);
      if (!arr) { arr = []; segIndex.set(k, arr); }
      if (!arr.includes(f.id)) arr.push(f.id);
    }
  }
  const edgeSet = new Set();
  for (const ids of segIndex.values()) {
    if (ids.length < 2) continue;
    for (let i = 0; i < ids.length; i++) {
      for (let j = i + 1; j < ids.length; j++) {
        edgeSet.add(canonEdge(ids[i], ids[j]));
      }
    }
  }
  console.log(`   found ${edgeSet.size} land adjacency edges`);

  // 6. Add sea crossings.
  console.log('6. patching sea crossings …');
  const adjacencies = [];
  for (const e of edgeSet) {
    const [a, b] = e.split('|');
    adjacencies.push({ provinceAId: a, provinceBId: b, terrainCost: 1.0, isSeaCrossing: false });
  }
  for (const [a, b, cost] of SEA_CROSSINGS) {
    if (!features.find(f => f.id === a) || !features.find(f => f.id === b)) {
      console.warn(`   WARN: sea crossing ${a}↔${b} references missing province`);
      continue;
    }
    const e = canonEdge(a, b);
    if (edgeSet.has(e)) continue; // already adjacent on land
    const [lo, hi] = e.split('|');
    adjacencies.push({ provinceAId: lo, provinceBId: hi, terrainCost: cost, isSeaCrossing: true });
  }
  adjacencies.sort((x, y) => (x.provinceAId + x.provinceBId).localeCompare(y.provinceAId + y.provinceBId));

  // 7. Verify graph connectivity. The previous BFS-from-ids[0] reported false
  //    positives whenever the graph had multiple components — it claimed every
  //    vertex outside the first component was "unreachable" without telling
  //    us how many components there actually were. We use union-find here
  //    so we can report each component separately and the operator can decide
  //    which need a SEA_CROSSINGS patch.
  console.log('7. verifying graph connectivity …');
  const components = findComponents(features.map(f => f.id), adjacencies);
  if (components.length === 1) {
    console.log(`   graph is fully connected ✓ (${components[0].length} provinces)`);
  } else {
    console.warn(`   WARN: graph has ${components.length} disconnected components:`);
    components
      .slice()
      .sort((a, b) => b.length - a.length)
      .forEach((c, i) => {
        const preview = c.length > 6 ? `${c.slice(0, 6).join(', ')}, …` : c.join(', ');
        console.warn(`     [${i + 1}] ${c.length} provinces: ${preview}`);
      });
  }

  // 8. Build final province records. Per user req: every province is Capital.
  console.log('8. emitting map-data.json …');
  const provinces = features.map(f => ({
    id: f.id,
    name: f.name,
    type: 'Capital',                            // user requirement
    isCoastal: COASTAL.has(f.id),
    centerX: f.cx,
    centerY: f.cy,
    basePopulation: f.basePopulation,
    baseResourceOutput: PROFILES[f.profile] ?? PROFILES.mixed,
    polygon: f.ring.slice(0, -1),               // map-data spec stores OPEN rings
  }));

  const out = {
    $schema: './map-data.schema.json',
    version: 2,
    provinces,
    adjacencies,
  };

  await mkdir(path.dirname(outPath), { recursive: true });
  await writeFile(outPath, JSON.stringify(out, null, 2) + '\n', 'utf8');

  const bytes = (await readFile(outPath)).length;
  console.log(`   wrote ${outPath}`);
  console.log(`   ${provinces.length} provinces, ${adjacencies.length} adjacencies, ${(bytes / 1024).toFixed(1)} KB`);
}

function blocDisplayName(id) {
  // 'canada-west' → 'Canada West'
  return id.split('-').map(s => s[0].toUpperCase() + s.slice(1)).join(' ');
}

function canonEdge(a, b) {
  return a < b ? `${a}|${b}` : `${b}|${a}`;
}

function findComponents(ids, edges) {
  // Union-find over the id set. Returns one array of ids per connected
  // component. Cheaper to build a single forest than to re-BFS from every
  // vertex, and it gives us component membership directly.
  const parent = new Map(ids.map(id => [id, id]));
  function find(x) {
    while (parent.get(x) !== x) {
      parent.set(x, parent.get(parent.get(x))); // path halving
      x = parent.get(x);
    }
    return x;
  }
  function union(a, b) {
    const ra = find(a), rb = find(b);
    if (ra !== rb) parent.set(ra, rb);
  }
  for (const e of edges) union(e.provinceAId, e.provinceBId);
  const groups = new Map();
  for (const id of ids) {
    const r = find(id);
    let g = groups.get(r);
    if (!g) { g = []; groups.set(r, g); }
    g.push(id);
  }
  return [...groups.values()];
}

main().catch(err => { console.error(err); process.exit(1); });
