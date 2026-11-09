import { useState } from "preact/hooks";
import type { BattleTerrain, Tile, UnitView } from "./types";
import { tileStyle, type MapData } from "./map";
import { Sheet } from "./details";
import { HpText, LineIcon } from "./ui";

const same = (a: Tile, b: Tile) => a[0] === b[0] && a[1] === b[1];
const inside = (map: MapData, t: Tile) => t[0] >= 0 && t[1] >= 0 && t[0] < map.w && t[1] < map.h;
const height = (map: MapData, t: Tile) => map.heights[t[1] * map.w + t[0]] ?? 0;
const side = (unit: UnitView, me: UnitView) => unit.id === me.id ? "You" : unit.side === me.side ? "Ally" : unit.side === "Neutral" ? "Neutral" : "Enemy";
const directions = [{ name: "North", dx: 0, dy: -1, edge: "top" }, { name: "East", dx: 1, dy: 0, edge: "right" },
  { name: "South", dx: 0, dy: 1, edge: "bottom" }, { name: "West", dx: -1, dy: 0, edge: "left" }];

/** A visible description of the current cursor, independent of whether a move or ability can be committed there. */
function TileDetails({ map, cursor, units, me, zones, warning }: {
  map: MapData | null; cursor: Tile; units: UnitView[]; me: UnitView; zones: BattleTerrain["zones"]; warning?: string;
}) {
  const i = map && inside(map, cursor) ? cursor[1] * map.w + cursor[0] : -1;
  const flags = i >= 0 ? map!.flags[i] ?? 0 : 0;
  const active = zones.filter(z => z.tiles.some(t => same(t, cursor)));
  const occupants = units.map((unit, i) => ({ unit, number: i + 1 })).filter(({ unit }) => same(unit.at, cursor));
  const material = i >= 0 ? (map!.materials[map!.tileMaterial[i] ?? 0] ?? "Ground").replaceAll("_", " ").replaceAll("/", " · ") : null;
  const drops = map && i >= 0 ? directions.flatMap(d => {
    const next: Tile = [cursor[0] + d.dx, cursor[1] + d.dy];
    const drop = inside(map, next) ? height(map, cursor) - height(map, next) : 0;
    return drop > 3 ? [`${d.name}: drop ${drop} levels · fall damage`] : [];
  }) : [];
  return <section class="tx-tile-details" aria-label="Selected tile" aria-live="polite">
    <b>Tile {cursor[0]}, {cursor[1]}{i >= 0 ? ` · height ${map!.heights[i]}` : ""}</b>
    {material ? <span>{material}</span> : <span>Terrain unavailable</span>}
    {occupants.map(({ unit, number }) => <div class="tx-tile-unit" key={unit.id}>
      <b>#{number} {unit.name} · {side(unit, me)}</b><span><HpText unit={unit} /> HP{unit.life !== "Up" ? ` · ${unit.life}` : ""}</span>
    </div>)}
    {flags & 16 ? <span>Deep ground · cannot enter on foot · falling downs heroes</span>
      : flags & 4 ? <span>Blocked · cannot enter</span> : null}
    {flags & 1 ? <span>Water</span> : null}
    {flags & 2 ? <span>Difficult ground · costs 2 Move to enter</span> : null}
    {flags & 8 ? <span>Hazardous terrain</span> : null}
    {drops.map(drop => <span class="tx-tile-warning" key={drop}>{drop}</span>)}
    {active.map((zone, index) => <div key={index}><b>{zone.name}</b>{zone.detail ? <span>{zone.detail}</span> : null}</div>)}
    {warning ? <span class="tx-tile-warning">Movement: {warning}</span> : null}
  </section>;
}

interface Props {
  me: UnitView; units: UnitView[]; cursor: Tile; onPick(t: Tile): void; onUnit(unit: UnitView): void; map: MapData | null;
  reach: Tile[]; risks: Tile[]; spread: Tile[]; aims: Tile[]; zones: BattleTerrain["zones"]; warning?: string;
}

/** Local map browsing and direct unit selection. No action is sent until the turn's separate commit button is pressed. */
export function BattleMap(props: Props) {
  const { me, units, cursor, onPick, onUnit, map } = props;
  const [open, setOpen] = useState<"map" | "units" | null>(null);
  const dpad = <DPad cursor={cursor} onMove={onPick} map={map} />;
  return <section class="tx-battle-map" aria-label="Battle map">
    <div class="tx-map-tools"><b>Battlefield</b>
      <button type="button" class="tx-btn" onClick={() => setOpen("units")}>Units ({units.length})</button>
      <button type="button" class="tx-btn" onClick={() => setOpen("map")}>Enlarge map</button>
    </div>
    <MiniMap {...props} />
    <p class="tx-map-key"># = unit · numbers = height · ! = hazard · ↓ = deep · × = blocked · Z = zone</p>
    <p class="tx-map-key">Round = ally · square = enemy · outline = you. Dotted edge = reachable · double edge = aim · stripes = risky move.</p>
    <p class="tx-map-key">Dashed cliff edge = drop of 4+ levels. Tap a tile to inspect.</p>
    <div class="tx-map-controls">{dpad}<TileDetails {...props} /></div>
    {props.spread.length ? <span class="tx-spread-key">Dashed box: reaction spread · includes allies</span> : null}
    {open === "units" ? <Sheet title="Battlefield units" onClose={() => setOpen(null)}>
      <p class="tx-note">Tap a unit to inspect it or aim a legal ability at it.</p>
      <ul class="tx-map-units">{units.map((unit, i) => <li key={unit.id}>
        <button type="button" class="tx-btn" onClick={() => { onUnit(unit); setOpen(null); }}>
          <b>#{i + 1} {unit.name}</b><span>{side(unit, me)} · <HpText unit={unit} /> HP{unit.life !== "Up" ? ` · ${unit.life}` : ""}</span>
        </button>
      </li>)}</ul>
    </Sheet> : null}
    {open === "map" ? <Sheet title="Enlarged battlefield" wide onClose={() => setOpen(null)}
      foot={<button type="button" class="tx-btn gold" onClick={() => setOpen(null)}>Done inspecting</button>}>
      <p class="tx-note">Scroll the map to reach distant tiles. Each cell is at least 44 px.</p>
      <div class="tx-map-pan"><MiniMap {...props} enlarged /></div>
      <TileDetails {...props} />{dpad}
    </Sheet> : null}
  </section>;
}

function MiniMap({ me, units, cursor, reach, risks, spread, aims, zones, onPick, map, enlarged = false }: Props & { enlarged?: boolean }) {
  const tiles = [me.at, cursor, ...units.map(u => u.at), ...reach, ...aims, ...spread];
  const [x0, x1] = map ? [0, map.w - 1] : [Math.max(0, Math.min(...tiles.map(t => t[0])) - 1), Math.max(...tiles.map(t => t[0])) + 1];
  const [y0, y1] = map ? [0, map.h - 1] : [Math.max(0, Math.min(...tiles.map(t => t[1])) - 1), Math.max(...tiles.map(t => t[1])) + 1];
  const rows = [];
  for (let y = y0; y <= y1; y++) {
    const cells = [];
    for (let x = x0; x <= x1; x++) {
      const t: Tile = [x, y];
      const occupants = units.map((u, i) => ({ unit: u, number: i + 1 })).filter(u => same(u.unit.at, t));
      const first = occupants[0];
      const index = map ? y * map.w + x : -1;
      const flags = map?.flags[index] ?? 0;
      const active = zones.filter(z => z.tiles.some(at => same(at, t)));
      const hazard = !!(flags & 8) || active.some(z => z.hazard);
      const cliffs = map ? directions.filter(d => {
        const next: Tile = [x + d.dx, y + d.dy];
        return inside(map, next) && Math.abs(height(map, t) - height(map, next)) > 3;
      }).map(d => `cliff-${d.edge}`) : [];
      const cls = ["tx-cell", first ? first.unit.id === me.id ? "me" : first.unit.side === me.side ? "ally" : first.unit.side === "Neutral" ? "neutral" : "foe" : "",
        reach.some(r => same(r, t)) ? "reach" : "", aims.some(a => same(a, t)) ? "aim" : "",
        risks.some(r => same(r, t)) ? "risk" : "", spread.some(r => same(r, t)) ? "spread" : "",
        hazard ? "hazard" : "", active.length ? "zone" : "", flags & 16 ? "deep" : "", ...cliffs,
        same(cursor, t) ? "cursor" : ""].filter(Boolean).join(" ");
      const label = occupants.length ? occupants.map(o => `#${o.number} ${o.unit.name}, ${side(o.unit, me)}`).join("; ") : `${x}, ${y}`;
      const description = [reach.some(r => same(r, t)) ? "Reachable" : "", aims.some(a => same(a, t)) ? "Legal aim" : "", map ? `Height ${height(map, t)}` : "", hazard ? "Hazard" : "", flags & 16 ? "Deep ground" : "",
        active.map(z => z.name).join(", "), spread.some(r => same(r, t)) ? "Within reaction spread" : "",
        risks.some(r => same(r, t)) ? "Hazardous movement" : ""].filter(Boolean).join(". ");
      cells.push(<button type="button" key={x} class={cls} aria-label={label} aria-description={description || undefined}
        aria-pressed={same(cursor, t)} onClick={() => onPick(t)} style={map && !first ? tileStyle(map, index) : undefined}>
        <span class="tx-cell-label">{first ? `#${first.number}${occupants.length > 1 ? "+" : ""}` : flags & 16 ? "↓" : flags & 4 ? "×" : hazard ? "!" : active.length ? "Z" : map ? height(map, t) : ""}</span>
      </button>);
    }
    rows.push(<div class="tx-maprow" key={y}>{cells}</div>);
  }
  return <div class={`tx-map${map ? " terrain" : ""}${enlarged ? " enlarged" : ""}`} role="grid" aria-label="Battlefield">{rows}</div>;
}

function DPad({ cursor, onMove, map }: { cursor: Tile; onMove(t: Tile): void; map: MapData | null }) {
  const go = (dx: number, dy: number) => onMove([Math.max(0, Math.min(map ? map.w - 1 : Infinity, cursor[0] + dx)),
    Math.max(0, Math.min(map ? map.h - 1 : Infinity, cursor[1] + dy))]);
  return <div class="tx-dpad">
    <button type="button" class="up" aria-label="Up" onClick={() => go(0, -1)}><LineIcon name="up" /></button>
    <button type="button" class="left" aria-label="Left" onClick={() => go(-1, 0)}><LineIcon name="up" rotate={-90} /></button>
    <span class="hub" aria-hidden="true">Cursor</span>
    <button type="button" class="right" aria-label="Right" onClick={() => go(1, 0)}><LineIcon name="up" rotate={90} /></button>
    <button type="button" class="down" aria-label="Down" onClick={() => go(0, 1)}><LineIcon name="up" rotate={180} /></button>
  </div>;
}
