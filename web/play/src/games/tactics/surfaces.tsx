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
      {known.slice(0, 2).map(kind => <div key={kind} class={`tx-surface tx-surface-${kind}`}>
        {Array.from({ length: 12 }, (_, i) => <i key={i} style={{ "--i": i, "--x": `${4 + (i * 37) % 92}%` }} />)}
      </div>)}
    </div>
    <div class={`tx-surface-glare tx-glare-${known[0]}`} aria-hidden="true" />
    <div class="tx-current-surfaces" role="status" aria-label={`Current terrain: ${known.map(k => terrain[k]).join(", ")}`}>
      <span class="tx-terrain-dot" aria-hidden="true" />
      {known.map(k => terrain[k]).join(" · ")}
    </div>
  </>;
}
