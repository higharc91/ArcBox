import { useState } from "preact/hooks";
import { useFeedback } from "../feedback/feedback";
import type { RoomPeek, SeatCard, SeatOffer, SeatRef } from "../protocol";
import type { StoredProfile } from "../session/storage";
import { NAME_MAX, isValidName } from "./Join";

interface Props {
  peek: RoomPeek;
  /** The profile keys this phone holds: "Continue as" shows the person's name, which the offer's card doesn't carry. */
  profiles?: StoredProfile[];
  /** "Not you?" was tapped: show every offer even though one is suggested. */
  showAll?: boolean;
  error?: string;
  busy?: boolean;
  onTake: (seat: SeatRef) => void;
  onNew: (name: string) => void;
  onShowAll: () => void;
}

const offerKey = (o: SeatOffer) => o.ref.slot ?? o.ref.member ?? "";
/** The card's accent as a CSS variable. Only a plain hex colour gets through: it comes from a game's module. */
export const cardStyle = (card: SeatCard) => (card.color && /^#[0-9a-f]{3,8}$/i.test(card.color) ? `--card: ${card.color}` : undefined);
const detail = (o: SeatOffer) => [o.card.title, o.card.subtitle].filter(Boolean).join(" · ");

/**
 * "Who are you?": what the room says this phone may take (RoomPeek). One of three shapes: a remembered phone gets a
 * one-tap "Continue as", a campaign lobby gets the roster to pick from, and a game in progress gets the offline seats.
 */
export function Pick({ peek, profiles = [], showAll, error, busy, onTake, onNew, onShowAll }: Props) {
  const feedback = useFeedback();
  const take = (seat: SeatRef) => {
    feedback?.unlock(); // a tap is the user gesture iOS needs before any sound
    if (!busy) onTake(seat);
  };
  const room = peek.inGame ? `ROOM ${peek.code} · IN PROGRESS` : `ROOM ${peek.code}`;
  const alert = error ? <p class="form-error" role="alert">{error}</p> : null;

  if (peek.suggested && !showAll) {
    const offer = peek.suggested;
    const name = profiles.find((p) => p.profileId === offer.ref.member)?.name ?? offer.card.title;
    const sub = name === offer.card.title ? offer.card.subtitle : detail(offer);
    return (
      <div class="screen pick">
        <PickHead room={room} title={peek.campaignName ?? "Welcome back"} />
        <section class="pick-hero" style={cardStyle(offer.card)}>
          <small>Welcome back</small>
          <b class="hero-name">{name}</b>
          {sub ? <span class="hero-sub">{sub}</span> : null}
        </section>
        <div class="pick-foot">
          {alert}
          <button type="button" class="primary big" disabled={busy} onClick={() => take(offer.ref)}>
            Continue as {name}
          </button>
          <button type="button" class="link" onClick={onShowAll}>Not you? Show everyone</button>
        </div>
      </div>
    );
  }

  return peek.inGame
    ? <SeatList peek={peek} room={room} alert={alert} busy={busy} onTake={take} />
    : <Roster peek={peek} room={room} alert={alert} busy={busy} onTake={take} onNew={onNew} />;
}

function PickHead({ room, title, sub }: { room: string; title: string; sub?: string | null }) {
  return (
    <header class="pick-head">
      <small>{room}</small>
      <h1>{title}</h1>
      {sub ? <p>{sub}</p> : null}
    </header>
  );
}

interface PartProps {
  peek: RoomPeek;
  room: string;
  alert: preact.ComponentChildren;
  busy?: boolean;
  onTake: (seat: SeatRef) => void;
}

/** The lobby grid: tap who you are, or "I'm new here" for a name box. */
function Roster({ peek, room, alert, busy, onTake, onNew }: PartProps & { onNew: (name: string) => void }) {
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState("");
  const feedback = useFeedback();

  if (adding) {
    const submit = (e: Event) => {
      e.preventDefault();
      feedback?.unlock();
      if (isValidName(name) && !busy) onNew(name.trim());
    };
    return (
      <div class="screen pick">
        <PickHead room={room} title="I'm new here" sub={peek.campaignName} />
        <form class="join-form" onSubmit={submit}>
          <label class="field">
            <span>Your name</span>
            <input
              class="text-input"
              name="newName"
              value={name}
              autoComplete="nickname"
              maxLength={NAME_MAX}
              placeholder={`Up to ${NAME_MAX} characters`}
              onInput={(e) => setName((e.currentTarget as HTMLInputElement).value)}
            />
          </label>
          <button type="submit" class="primary big" disabled={!isValidName(name) || busy}>
            {busy ? "Joining…" : "Join"}
          </button>
          <button type="button" class="link" onClick={() => setAdding(false)}>Back to the list</button>
          {alert}
        </form>
      </div>
    );
  }

  return (
    <div class="screen pick">
      <PickHead room={room} title="Who are you?" sub={peek.campaignName} />
      <div class="pick-grid">
        {peek.offers.map((o) => (
          <button
            type="button"
            key={offerKey(o)}
            class="pick-tile"
            style={cardStyle(o.card)}
            disabled={busy}
            onClick={() => onTake(o.ref)}
          >
            <b>{o.card.title}</b>
            {o.card.subtitle ? <span>{o.card.subtitle}</span> : null}
          </button>
        ))}
        {peek.canJoinNew ? (
          <button type="button" class="pick-tile pick-new" disabled={busy} onClick={() => setAdding(true)}>+ I'm new here</button>
        ) : null}
      </div>
      <div class="pick-foot">
        {alert}
        <p class="hint">
          Characters already being played can't be picked.
          {peek.canJoinNew ? " New players can join between games." : ""}
        </p>
      </div>
    </div>
  );
}

/** Mid-game: the seats with no phone connected. Tap one, then confirm. */
function SeatList({ peek, room, alert, busy, onTake }: PartProps) {
  const [picked, setPicked] = useState(() => (peek.offers[0] ? offerKey(peek.offers[0]) : ""));
  const chosen = peek.offers.find((o) => offerKey(o) === picked) ?? peek.offers[0];

  return (
    <div class="screen pick">
      <PickHead room={room} title="Pick your seat" sub="These characters have no phone connected" />
      <ul class="pick-rows">
        {peek.offers.map((o) => {
          const on = o === chosen;
          return (
            <li key={offerKey(o)}>
              <button
                type="button"
                class="pick-row"
                style={cardStyle(o.card)}
                aria-pressed={on ? "true" : "false"}
                onClick={() => setPicked(offerKey(o))}
              >
                <span class="row-text">
                  <b>{o.card.title}</b>
                  {o.card.subtitle ? <span>{o.card.subtitle}</span> : null}
                  {o.absent ? <em>The AI is playing {o.card.title}</em> : null}
                </span>
                <span class={`tag ${o.absent ? "ai" : "offline"}`}>{o.absent ? "AI" : "OFFLINE"}</span>
              </button>
            </li>
          );
        })}
      </ul>
      <div class="pick-foot">
        {alert}
        <p class="hint">Characters with a phone connected aren't shown.</p>
        {chosen ? (
          <button type="button" class="primary big" disabled={busy} onClick={() => onTake(chosen.ref)}>
            I'm {chosen.card.title}
          </button>
        ) : null}
      </div>
    </div>
  );
}
