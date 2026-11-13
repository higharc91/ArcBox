import { useEffect, useRef } from "preact/hooks";
import { PLAYER_COLOURS } from "../games/meridian/DeckMap";
import type { CompartmentCard, CrewMarker, Deck, Presented } from "../games/meridian/types";

/**
 * A browser stand-in for the TV's Meridian screen until the Godot scene (stage M4): all three decks side by side, the
 * seats, the counters, the event being presented and the log. It plays the TV's part in the pacing (spec §3.2): once an
 * event has been on screen for most of its fallback time it signals `presented { seq }`, once per event.
 */

export interface MeridianHostView {
  phase: string; banner: string;
  seats: { id: string; name: string; crew: string | null; colour: number; connected: boolean; auto: boolean; active: boolean }[];
  compartments: CompartmentCard[]; crew: CrewMarker[]; tilesLeft: number; anomaliesDrawn: number; round: number;
  now: Presented | null; log: string[];
}

export const isMeridianHostView = (v: unknown): v is MeridianHostView =>
  typeof v === "object" && v !== null && Array.isArray((v as MeridianHostView).compartments) && "tilesLeft" in v;

const DECKS: { id: Deck; label: string }[] = [
  { id: "command", label: "Command" }, { id: "habitat", label: "Habitat" }, { id: "engineering", label: "Engineering" },
];

/** The TV view carries tile ids, not names (the TV has the content pack); the dev board makes do with the id. */
export const tileLabel = (tile: string) => tile.split("_").map((w) => w[0].toUpperCase() + w.slice(1)).join(" ");

/** How long before an event's fallback runs out the board signals, so the game moves on a little sooner. */
const EARLY_MS = 200;

export function MeridianBoard({ view, signal }: { view: MeridianHostView; signal(kind: string, payload: unknown): void }) {
  const signalled = useRef(0);
  const seq = view.now?.seq ?? 0;
  useEffect(() => {
    if (!view.now || seq <= signalled.current) return;
    const timer = setTimeout(() => {
      signalled.current = seq;
      signal("presented", { seq });
    }, Math.max(300, view.now.seconds * 1000 - EARLY_MS));
    return () => clearTimeout(timer);
  }, [seq]);

  return (
    <div class="mer-board">
      <header>
        <b>{view.banner}</b>
        <span>Round {view.round} · Anomalies {view.anomaliesDrawn} · Tiles left {view.tilesLeft}</span>
      </header>
      <ol class="mer-seats">
        {view.seats.map((s) => (
          <li key={s.id} class={s.active ? "active" : ""} style={{ borderColor: PLAYER_COLOURS[s.colour % 6] }}>
            {s.name}{s.crew ? ` · ${s.crew}` : ""}{!s.connected ? " · offline" : ""}{s.auto ? <span class="mer-auto"> auto</span> : null}
          </li>
        ))}
      </ol>
      {view.now ? (
        <div class={`mer-now ${view.now.kind}`} role="status">
          <b>{view.now.title}</b> {view.now.text}
          {view.now.faces.length ? ` [${view.now.faces.join(" ")}]` : ""}
        </div>
      ) : null}
      <div class="mer-board-decks">
        {DECKS.map((d) => <BoardDeck key={d.id} label={d.label} rooms={view.compartments.filter((c) => c.at[0] === d.id)}
          crew={view.crew} lit={view.now?.compartment ?? null} />)}
      </div>
      <ul class="mer-log">{view.log.map((line, i) => <li key={i}>{line}</li>)}</ul>
    </div>
  );
}

function BoardDeck({ label, rooms, crew, lit }: { label: string; rooms: CompartmentCard[]; crew: CrewMarker[]; lit: string | null }) {
  const xs = rooms.map((r) => r.at[1]), ys = rooms.map((r) => r.at[2]);
  const minX = Math.min(...xs, 0), maxX = Math.max(...xs, 0), minY = Math.min(...ys, 0);
  return (
    <section class="mer-board-deck" aria-label={label}>
      <h3>{label}</h3>
      <div class="mer-grid" style={{ gridTemplateColumns: `repeat(${maxX - minX + 1}, 48px)` }}>
        {rooms.map((r) => {
          const sealed = new Set(r.sealed ?? []);
          const cls = ["mer-room", r.id === lit ? "here" : "", ...r.hatches.map((h) => (sealed.has(h) ? `sealed-${h}` : `hatch-${h}`))];
          return (
            <div key={r.id} class={cls.filter(Boolean).join(" ")} style={{ gridColumn: r.at[1] - minX + 1, gridRow: r.at[2] - minY + 1 }}
              title={r.id}>
              <span class="mer-name">{tileLabel(r.tile)}</span>
              <span class="mer-crew">
                {crew.filter((c) => c.at === r.id).map((c) => (
                  <span key={c.id} class="mer-dot" title={c.name} style={{ background: PLAYER_COLOURS[c.colour % 6] }} />
                ))}
              </span>
            </div>
          );
        })}
      </div>
    </section>
  );
}
