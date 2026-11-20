import { useState } from "preact/hooks";
import type { CompartmentCard, Deck, MapData, Side } from "./types";

/** The platform's six player colours, by seat index (tools/tactics-assets/STYLE.md). */
export const PLAYER_COLOURS = ["#ff8a3d", "#3ec7ff", "#c08cff", "#5fe08a", "#ffd447", "#ff7fb6"];

const DECKS: { id: Deck; label: string }[] = [
  { id: "command", label: "Command" }, { id: "habitat", label: "Habitat" }, { id: "engineering", label: "Engineering" },
];
const STEP: Record<Side, [number, number]> = { n: [0, -1], e: [1, 0], s: [0, 1], w: [-1, 0] };
const SIDE_NAME: Record<Side, string> = { n: "north", e: "east", s: "south", w: "west" };
const SYMBOL: Record<string, string> = { incident: "!", salvage: "+", anomaly: "◆" };

interface Props {
  map: MapData;
  /** The compartment the player is in; its deck is shown first. */
  here?: string;
  /** Compartments the player can move to, with their cost. Only these are tappable. */
  reach?: { id: string; cost: number }[];
  /** Hatches the player can open. */
  opens?: { at: string; side: Side }[];
  onMove?(id: string): void;
  onOpen?(at: string, side: Side): void;
}

/**
 * One deck of the ship as a grid, with a switcher for the other two. The switcher is phone-side state, so browsing the
 * decks never sends anything (spec §3.4). Placeholder look: the art arrives with issues #332 and #335.
 */
export function DeckMap({ map, here, reach = [], opens = [], onMove, onOpen }: Props) {
  const start = map.compartments.find((c) => c.id === here)?.at[0] ?? "habitat";
  const [deck, setDeck] = useState<Deck>(start);
  const rooms = map.compartments.filter((c) => c.at[0] === deck);
  const costs = new Map(reach.map((r) => [r.id, r.cost]));
  const byId = new Map(map.compartments.map((c) => [c.id, c]));
  const doors = opens
    .map((o) => ({ ...o, from: byId.get(o.at)! }))
    .filter((o) => o.from && o.from.at[0] === deck)
    .map((o) => ({ ...o, x: o.from.at[1] + STEP[o.side][0], y: o.from.at[2] + STEP[o.side][1] }));

  const xs = [...rooms.map((r) => r.at[1]), ...doors.map((d) => d.x)];
  const ys = [...rooms.map((r) => r.at[2]), ...doors.map((d) => d.y)];
  const minX = Math.min(...xs, 0), maxX = Math.max(...xs, 0), minY = Math.min(...ys, 0);
  const place = (x: number, y: number) => ({ gridColumn: x - minX + 1, gridRow: y - minY + 1 });

  return (
    <div class="mer-map">
      <div class="mer-decks" role="tablist" aria-label="Deck">
        {DECKS.map((d) => {
          // A deck with somewhere to go says so, since the map shows one deck at a time and the lift reaches the others.
          const count = reach.filter((r) => byId.get(r.id)?.at[0] === d.id).length
            + opens.filter((o) => byId.get(o.at)?.at[0] === d.id).length;
          return (
            <button key={d.id} type="button" role="tab" aria-selected={d.id === deck} class={d.id === deck ? "on" : ""}
              onClick={() => setDeck(d.id)}>{d.label}{count ? <span class="mer-badge"> {count}</span> : null}</button>
          );
        })}
      </div>
      <div class={`mer-grid deck-${deck}`} style={{ gridTemplateColumns: `repeat(${maxX - minX + 1}, 1fr)` }}>
        {rooms.map((room) => (
          <Room key={room.id} room={room} style={place(room.at[1], room.at[2])} here={room.id === here}
            cost={costs.get(room.id)} crew={map.crew.filter((c) => c.at === room.id)} onMove={onMove} />
        ))}
        {doors.map((d) => (
          <button key={`${d.at}-${d.side}`} type="button" class="mer-open" style={place(d.x, d.y)}
            aria-label={`Open the ${d.from.name ?? d.from.tile}'s ${SIDE_NAME[d.side]} hatch`}
            onClick={() => onOpen?.(d.at, d.side)}>Open</button>
        ))}
      </div>
    </div>
  );
}

function Room({ room, style, here, cost, crew, onMove }: {
  room: CompartmentCard; style: Record<string, number>; here: boolean; cost: number | undefined;
  crew: { id: string; name: string; colour: number }[]; onMove?(id: string): void;
}) {
  const sealed = new Set(room.sealed ?? []);
  const cls = ["mer-room", here ? "here" : "", cost !== undefined ? "reach" : "",
    ...room.hatches.map((h) => (sealed.has(h) ? `sealed-${h}` : `hatch-${h}`))].filter(Boolean).join(" ");
  const body = (
    <>
      <span class="mer-name">{room.name ?? room.tile}</span>
      {room.symbol ? <span class={`mer-symbol ${room.symbol}`} aria-label={room.symbol}>{SYMBOL[room.symbol]}</span> : null}
      {cost !== undefined ? <span class="mer-cost">{cost}</span> : null}
      <span class="mer-crew">
        {crew.map((c) => <span key={c.id} class="mer-dot" title={c.name} style={{ background: PLAYER_COLOURS[c.colour % 6] }} />)}
      </span>
    </>
  );
  return cost !== undefined && onMove ? (
    <button type="button" class={cls} style={style} aria-label={`Move to ${room.name ?? room.tile} (${cost})`}
      onClick={() => onMove(room.id)}>{body}</button>
  ) : (
    <div class={cls} style={style}>{body}</div>
  );
}
