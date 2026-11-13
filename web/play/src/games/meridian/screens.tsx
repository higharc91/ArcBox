import { useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { DeckMap } from "./DeckMap";
import type {
  CardData, CrewCard, CrewPickData, ItemCard, OrientData, OverData, RollData, SheetData, Side, TradeData, TurnData, WatchData,
} from "./types";

const TRAIT: Record<string, string> = { brawn: "Brawn", agility: "Agility", tech: "Tech", nerve: "Nerve" };
const ARROW: Record<Side, string> = { n: "↑", e: "→", s: "↓", w: "←" };

/** A crew member's four tracks, with the marker on the current step. */
function Tracks({ crew }: { crew: CrewCard }) {
  return (
    <dl class="mer-tracks">
      {crew.tracks.map((t) => (
        <div key={t.trait} class="mer-track">
          <dt>{TRAIT[t.trait]} <b>{t.values[t.step - 1]}</b></dt>
          <dd>{t.values.map((v, i) => <span key={i} class={i === t.step - 1 ? "mark" : ""}>{v}</span>)}</dd>
        </div>
      ))}
    </dl>
  );
}

function Items({ items, onUse }: { items: ItemCard[]; onUse?(id: string): void }) {
  if (items.length === 0) return <p class="mer-muted">Nothing carried.</p>;
  return (
    <ul class="mer-items">
      {items.map((i) => (
        <li key={i.id} class={i.kind}>
          <b>{i.title}</b> <span class="mer-muted">{i.text}</span>
          {i.use && onUse && i.usable ? <button type="button" onClick={() => onUse(i.id)}>{i.use}</button> : null}
        </li>
      ))}
    </ul>
  );
}

export function CrewPick({ data, send }: CustomScreenProps) {
  const d = data as CrewPickData;
  return (
    <div class="mer mer-pick">
      <h2>Choose your crew</h2>
      <p class="mer-muted">{d.picked} of {d.seats} chosen</p>
      {d.crew.map((c) => (
        <section key={c.id} class={`mer-crew-card${c.mine ? " mine" : ""}${c.taken ? " taken" : ""}`}>
          <h3>{c.name} <span class="mer-muted">{c.role}</span></h3>
          <Tracks crew={c} />
          <button type="button" disabled={c.taken || c.mine} onClick={() => send("pick", { crew: c.id })}>
            {c.mine ? "Chosen" : c.taken ? "Taken" : `Choose ${c.name}`}
          </button>
        </section>
      ))}
    </div>
  );
}

export function Turn({ data, send }: CustomScreenProps) {
  const d = data as TurnData;
  return (
    <div class="mer mer-turn">
      <h2>Your turn <span class="mer-muted">{d.moves} {d.moves === 1 ? "move" : "moves"} left</span></h2>
      <DeckMap map={d.map} here={d.at} reach={d.reach} opens={d.opens}
        onMove={(id) => send("moveTo", { to: id })} onOpen={(at, side) => send("open", { at, side })} />
      {d.room ? <button type="button" onClick={() => send("useRoom")}>Use the {d.room}</button> : null}
      <Items items={d.items} onUse={(id) => send("useItem", { item: id })} />
      {d.neighbours.map((n) => <TradeOffer key={n.seat} to={n} mine={d.items} send={send} />)}
      <button type="button" class="mer-end" onClick={() => send("endTurn")}>End turn</button>
    </div>
  );
}

/** Offer one of your items, ask for one of theirs, or both. The choice is phone-side until Offer is pressed. */
function TradeOffer({ to, mine, send }: { to: TurnData["neighbours"][number]; mine: ItemCard[]; send: CustomScreenProps["send"] }) {
  const [give, setGive] = useState<string | null>(null);
  const [take, setTake] = useState<string | null>(null);
  if (mine.length === 0 && to.items.length === 0) return null;
  const pick = (items: ItemCard[], value: string | null, set: (v: string | null) => void, label: string) => (
    <label>{label}
      <select value={value ?? ""} onChange={(e) => set((e.currentTarget as HTMLSelectElement).value || null)}>
        <option value="">Nothing</option>
        {items.map((i) => <option key={i.id} value={i.id}>{i.title}</option>)}
      </select>
    </label>
  );
  return (
    <section class="mer-trade-offer">
      <h3>Trade with {to.name}</h3>
      {pick(mine, give, setGive, "Give")}
      {pick(to.items, take, setTake, "Ask for")}
      <button type="button" disabled={!give && !take}
        onClick={() => send("offerTrade", { to: to.seat, give: give ? [give] : [], take: take ? [take] : [] })}>Offer</button>
    </section>
  );
}

export function Orient({ data, send }: CustomScreenProps) {
  const d = data as OrientData;
  return (
    <div class="mer mer-orient">
      <h2>{d.name}</h2>
      <p class="mer-muted">Which way round does it go?</p>
      <div class="mer-options">
        {d.options.map((o) => (
          <button key={o.rotation} type="button" onClick={() => send("orient", { rotation: o.rotation })}
            aria-label={`Hatches ${o.hatches.join(" ")}`}>{o.hatches.map((h) => ARROW[h]).join(" ")}</button>
        ))}
      </div>
      <DeckMap map={d.map} />
    </div>
  );
}

export function Card({ data, send }: CustomScreenProps) {
  const d = data as CardData;
  return (
    <div class={`mer mer-card ${d.kind}`}>
      <h2>{d.title}</h2>
      <p>{d.text}</p>
      {d.options.map((o) => (
        <button key={o.id} type="button" onClick={() => send("choose", { option: o.id })}>
          {o.label}{o.check ? ` (${TRAIT[o.check]} check)` : ""}
        </button>
      ))}
    </div>
  );
}

export function Roll({ data, send }: CustomScreenProps) {
  const d = data as RollData;
  return (
    <div class={`mer mer-roll ${d.reason}`}>
      <h2>{d.title}</h2>
      <p>{d.text}</p>
      {d.needed !== null ? <p class="mer-need">{d.needed} or more and the Breach fires.</p> : (
        <ol class="mer-bands">
          {d.bands.map((b) => <li key={b.min}><b>{b.max === null ? `${b.min}+` : b.min === b.max ? b.min : `${b.min}–${b.max}`}</b> {b.text}</li>)}
        </ol>
      )}
      <button type="button" class="mer-roll-button" onClick={() => send("roll")}>
        Roll {d.dice} {d.dice === 1 ? "die" : "dice"}{d.trait ? ` for ${TRAIT[d.trait]}` : ""}
      </button>
    </div>
  );
}

export function Trade({ data, send }: CustomScreenProps) {
  const d = data as TradeData;
  const list = (items: ItemCard[]) => (items.length ? items.map((i) => i.title).join(", ") : "nothing");
  return (
    <div class="mer mer-trade">
      <h2>{d.from} offers a trade</h2>
      <p>They give you <b>{list(d.give)}</b> for <b>{list(d.take)}</b>.</p>
      <button type="button" onClick={() => send("answerTrade", { accept: true })}>Accept</button>
      <button type="button" onClick={() => send("answerTrade", { accept: false })}>Decline</button>
    </div>
  );
}

/** Everyone who isn't acting. The tabs are phone-side state, so browsing them never sends anything. */
export function Sheet({ data }: CustomScreenProps) {
  const d = data as SheetData;
  const [tab, setTab] = useState<"traits" | "gear" | "map">("traits");
  return (
    <div class="mer mer-sheet">
      <p class="mer-muted">{d.waiting ? `Waiting on ${d.waiting}` : `${d.active}'s turn`}</p>
      <h2>{d.me.name} <span class="mer-muted">{d.me.role}</span></h2>
      <div class="mer-tabs" role="tablist">
        {(["traits", "gear", "map"] as const).map((t) => (
          <button key={t} type="button" role="tab" aria-selected={t === tab} class={t === tab ? "on" : ""} onClick={() => setTab(t)}>
            {t === "traits" ? "Traits" : t === "gear" ? "Gear" : "Map"}
          </button>
        ))}
      </div>
      {tab === "traits" ? <Tracks crew={d.me} /> : tab === "gear" ? <Items items={d.items} /> : <DeckMap map={d.map} here={d.at} />}
    </div>
  );
}

export function Watch({ data }: CustomScreenProps) {
  const d = data as WatchData;
  return (
    <div class={`mer mer-watch ${d.now.kind}`}>
      <p class="mer-muted">On the main screen</p>
      <h2>{d.now.title}</h2>
      {d.now.text ? <p>{d.now.text}</p> : null}
      {d.now.faces.length ? <p class="mer-faces" aria-label="Dice">{d.now.faces.map((f, i) => <span key={i}>{f}</span>)}</p> : null}
    </div>
  );
}

export function Over({ data }: CustomScreenProps) {
  const d = data as OverData;
  return (
    <div class="mer mer-over">
      <h2>Game over</h2>
      <p>{d.message}</p>
    </div>
  );
}
