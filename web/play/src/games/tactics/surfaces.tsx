/** Ambient art follows the server's current-tile snapshot, never the player's aim cursor. */
const terrain = {
  burning: "Burning ground", ice_sheet: "Ice sheet", water: "Water", oil: "Oil", web: "Webs", scorched: "Scorched ground",
} as const;
type Kind = keyof typeof terrain;
const priority: Kind[] = ["burning", "ice_sheet", "oil", "web", "water", "scorched"];

export function SurfaceScene({ kinds }: { kinds?: string[] }) {
  const known = priority.filter(k => kinds?.includes(k));
  if (known.length === 0) return null;
  // Two visual layers, at most 24 particles, even where several spell zones overlap.
  return <>
    <div class="tx-surface-scene" aria-hidden="true">
      {known.slice(0, 2).filter(kind => kind !== "web").map(kind => <div key={kind} class={`tx-surface tx-surface-${kind}`}>
        {Array.from({ length: 12 }, (_, i) => <i key={i} style={{ "--i": i, "--x": `${4 + (i * 37) % 92}%` }} />)}
      </div>)}
    </div>
    <div class={`tx-surface-glare tx-glare-${known[0]}`} aria-hidden="true">
      {known.slice(0, 2).includes("web") ? <WebStrands /> : null}
    </div>
    <div class="tx-current-surfaces" role="status" aria-label={`Current terrain: ${known.map(k => terrain[k]).join(", ")}`}>
      <span class="tx-terrain-dot" aria-hidden="true" />
      {known.map(k => terrain[k]).join(" · ")}
    </div>
  </>;
}

/** Radial silk and sagging cross-strands form an open web anchored outside the screen. */
function WebStrands() {
  const angles = Array.from({ length: 7 }, (_, i) => i * Math.PI / 12);
  const point = (r: number, angle: number) => `${(r * Math.cos(angle)).toFixed(2)} ${(r * Math.sin(angle)).toFixed(2)}`;
  const spokes = angles.map(angle => `M0 0 L${point(230, angle)}`).join(" ");
  const rings = [23, 47, 75, 108, 146, 188].map(radius =>
    `M${point(radius, 0)} ` + angles.slice(1).map((angle, i) =>
      `Q${point(radius * 0.82, (angles[i]! + angle) / 2)} ${point(radius, angle)}`).join(" "));
  return <>{["top-left", "top-right", "bottom-left"].map(corner =>
    <span key={corner} class={`tx-web-corner ${corner}`}>
      <svg viewBox="0 0 180 180" fill="none" focusable="false">
        <path d={spokes} class="tx-web-spokes" />
        {rings.map((d, i) => <path key={i} d={d} class="tx-web-cross" />)}
      </svg>
    </span>)}</>;
}
