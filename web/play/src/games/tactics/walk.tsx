import { Give } from "./give";
import { WalkJournal } from "./quests";
import type { JSX } from "preact";
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "preact/hooks";
import { useFeedback } from "../../feedback/feedback";
import type { CustomScreenProps } from "../module";
import { Portrait } from "./art";
import { useAlertOnMount, useSfx } from "./sfx";
import type { WalkData, WalkMember } from "./types";
import { LineIcon } from "./ui";
import { ChoicesCard } from "./choices";
import { DEAD_ZONE, dirAt, SECTORS, stickyDirAt, WalkSender, type Dir, type Sector } from "./walkModel";

/** How long "Choose a battle" and "End session" wait for their confirm before they stand down. */
const CONFIRM_MS = 4000;
/** How far the knob travels from the centre, as a share of the pad's radius. */
const KNOB_REACH = 0.6;
/** How long a tapped Interact stays asleep when no new view comes (the server found the spot out of reach by then). */
const INTERACT_REARM_MS = 2000;

const ARROWS: Record<string, [number, number]> = { ArrowUp: [0, -1], ArrowDown: [0, 1], ArrowLeft: [-1, 0], ArrowRight: [1, 0] };

/** A point on the pad's drawing, whose radius is 100, `deg` clockwise from up. */
const polar = (r: number, deg: number) => {
  const a = (deg * Math.PI) / 180;
  return `${(r * Math.sin(a)).toFixed(2)} ${(-r * Math.cos(a)).toFixed(2)}`;
};

/** One sector of the pad, from the hub's edge to the rim, a degree short of each edge so the sectors read apart. */
function wedge({ from: start, to: end }: Sector): string {
  const [from, to, inner, outer] = [start + 1, end - 1, DEAD_ZONE * 100 + 2, 98];
  return `M${polar(inner, from)}L${polar(outer, from)}A${outer} ${outer} 0 0 1 ${polar(outer, to)}L${polar(inner, to)}A${inner} ${inner} 0 0 0 ${polar(inner, from)}Z`;
}

const WEDGES = SECTORS.map(wedge);

/**
 * The pad's face: eight sectors as `SECTORS` lays them out, a chevron in each, and a dashed hub the size of the dead
 * zone. Each chevron points where the party goes, so the road directions' sit on a road's bearing, not at 45°; the thin
 * e and w sectors get the small ones. It's drawn once and painted by CSS from `data-dir`.
 */
function PadFace() {
  return (
    <svg viewBox="-100 -100 200 200" aria-hidden="true">
      {SECTORS.map((s, i) => <path key={s.dir} class={`tx-wedge ${s.dir}`} d={WEDGES[i]} />)}
      {SECTORS.map(({ dir, aim }) => {
        // The thin sectors' chevrons sit smaller and further out, where their wedge is wide enough to clear the roads'.
        const thin = dir === "e" || dir === "w";
        return (
          <path key={dir} class={`tx-chevron ${dir}${thin ? " small" : ""}`}
            transform={`rotate(${aim.toFixed(2)}) translate(0 ${thin ? -80 : -66})${thin ? " scale(0.75)" : ""}`} d="M-11 5L0 -6L11 5" />
        );
      })}
      <circle class="tx-hub" r={DEAD_ZONE * 100} />
    </svg>
  );
}

/**
 * The walk pad. Everything a thumb does is written straight to the DOM from these listeners (`useWobble`'s way): it must
 * not re-render while held, and all it says goes through `sender`. The whole zone around the pad takes presses, so a
 * thumb that lands a little wide while the eyes are on the TV still counts, and the pad's place is read at every move,
 * so a page that shifts under a held thumb doesn't throw it off.
 */
function Pad({ sender }: { sender: WalkSender }) {
  const zone = useRef<HTMLDivElement>(null);
  const face = useRef<HTMLDivElement>(null);
  const knob = useRef<HTMLSpanElement>(null);
  const feedback = useFeedback();
  const buzz = useRef(feedback);
  buzz.current = feedback;

  useEffect(() => {
    const area = zone.current!;
    const dot = knob.current!;
    const pad = face.current!;
    const root = area.closest(".tx-walk");
    let pointer: number | null = null;
    let dir: Dir | null = null;
    const keys = new Set<string>();

    const paint = (down: boolean, x = 0, y = 0) => {
      pad.dataset.dir = dir ?? "";
      pad.classList.toggle("down", down);
      root?.classList.toggle("held", down);
      dot.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px)`;
    };

    /** Goes to `next`, says so, and if it's a change under a thumb already down, ticks. */
    const steer = (next: Dir | null, tick: boolean) => {
      if (next === dir) return;
      dir = next;
      if (next) sender.press(next);
      else sender.release();
      if (tick) buzz.current?.buzz("walkTurn");
    };

    const follow = (e: PointerEvent, tick: boolean) => {
      const box = pad.getBoundingClientRect();
      const radius = box.width / 2 || 1;
      const [px, py] = [e.clientX - box.left - radius, e.clientY - box.top - radius];
      steer(stickyDirAt(px / radius, py / radius, DEAD_ZONE, dir), tick);
      const shrink = Math.min(1, (KNOB_REACH * radius) / (Math.hypot(px, py) || 1));
      paint(true, px * shrink, py * shrink);
    };

    /** Lets go of everything, whichever way the hold ended. A thumb still down has to come up and press again. */
    const letGo = () => {
      pointer = null;
      keys.clear();
      steer(null, false);
      paint(false);
    };

    const down = (e: PointerEvent) => {
      if (pointer !== null || keys.size > 0 || e.button > 0) return;
      e.preventDefault();
      pointer = e.pointerId;
      try {
        area.setPointerCapture(e.pointerId);
      } catch {
        // No capture, no steering past the zone's edge: the release still arrives while the thumb is on it.
      }
      buzz.current?.buzz("walkPress");
      follow(e, false);
    };
    const move = (e: PointerEvent) => {
      if (e.pointerId === pointer) follow(e, true);
    };
    const end = (e: PointerEvent) => {
      if (e.pointerId === pointer) letGo();
    };

    const key = (e: KeyboardEvent, on: boolean) => {
      if (!ARROWS[e.key]) return;
      e.preventDefault();
      if (e.repeat || pointer !== null) return;
      if (on) keys.add(e.key);
      else keys.delete(e.key);
      const [x, y] = [...keys].reduce(([a, b], k) => [a + ARROWS[k]![0], b + ARROWS[k]![1]], [0, 0]);
      steer(dirAt(x, y, 0), false);
      paint(keys.size > 0);
    };
    const keyDown = (e: KeyboardEvent) => key(e, true);
    const keyUp = (e: KeyboardEvent) => key(e, false);
    const prevent = (e: Event) => e.preventDefault();
    const hidden = () => {
      if (document.visibilityState === "hidden") letGo();
    };

    const listen: [EventTarget, string, EventListener][] = [
      [area, "pointerdown", down as EventListener],
      [area, "pointermove", move as EventListener],
      [area, "pointerup", end as EventListener],
      [area, "pointercancel", end as EventListener],
      [area, "lostpointercapture", end as EventListener],
      [area, "contextmenu", prevent],
      [area, "selectstart", prevent],
      [area, "dragstart", prevent],
      [window, "keydown", keyDown as EventListener],
      [window, "keyup", keyUp as EventListener],
      [window, "blur", letGo],
      [window, "pagehide", letGo],
      [document, "visibilitychange", hidden],
    ];
    for (const [target, type, fn] of listen) target.addEventListener(type, fn);
    return () => {
      for (const [target, type, fn] of listen) target.removeEventListener(type, fn);
      letGo();
    };
  }, [sender]);

  return (
    <div class="tx-padzone" ref={zone}>
      {/* It takes focus so a keyboard finds it, though the arrow keys walk from anywhere on the page. */}
      <div class="tx-pad" ref={face} role="application" tabIndex={0} aria-keyshortcuts="ArrowUp ArrowDown ArrowLeft ArrowRight"
        aria-label="Walking pad: hold a direction to walk, and let go to stop. The arrow keys walk too.">
        <PadFace />
        <span class="tx-pad-knob" ref={knob} />
      </div>
    </div>
  );
}

const DIFFICULTIES = ["Story", "Normal", "Hard"] as const;

/** The campaign's difficulty, set by the VIP any time outside a battle; the server refuses it in one. */
function DifficultyPicker({ current, send }: { current: string; send: CustomScreenProps["send"] }) {
  const sfx = useSfx();
  return (
    <div class="tx-difficulty" role="group" aria-label="Difficulty">
      <span class="tx-difficulty-label">Difficulty</span>
      {DIFFICULTIES.map((level) => (
        <button type="button" class={`tx-btn ghost${level === current ? " on" : ""}`} aria-pressed={level === current} key={level}
          onClick={() => { if (level !== current) { sfx("select"); send("difficulty", { level }); } }}>{level}</button>
      ))}
    </div>
  );
}

/** The VIP's way out of the world. It's at the far end of the screen from the pad, and it sleeps while a thumb is on it. */
/**
 * The VIP's menu: Choose a battle (with the battle list) and End session, each behind a confirm. The story's screens show it
 * too, with no battle and "Keep playing" (`keep`), as the server takes End session in a scene.
 */
export function HostMenu({ canBattle, send, keep = "Keep walking", difficulty }: { canBattle: boolean; send: CustomScreenProps["send"]; keep?: string; difficulty?: string }) {
  // Both take the whole table out of the walk, and the leader's pad with it, so both ask first.
  const [asked, setSure] = useState<"battles" | "endSession" | null>(null);
  // A battle confirm outlives no battles: it goes at once, and stays gone if they come back.
  const sure = asked === "battles" && !canBattle ? null : asked;
  const sfx = useSfx();
  useEffect(() => {
    if (!canBattle) setSure((s) => (s === "battles" ? null : s));
  }, [canBattle]);
  useEffect(() => {
    if (!sure) return;
    const id = setTimeout(() => setSure(null), CONFIRM_MS);
    return () => clearTimeout(id);
  }, [sure]);

  // The confirm isn't under the finger that asked for it: "Keep walking" takes that finger's place, so a double tap
  // lands on it.
  if (sure) {
    const no = <button type="button" class="tx-btn ghost" onClick={() => { sfx("cancel"); setSure(null); }}>{keep}</button>;
    const yes = (
      <button type="button" class="tx-btn gold" onClick={() => { sfx("confirm"); send(sure); }}>
        {sure === "battles" ? "Yes, choose one" : "Yes, end it"}
      </button>
    );
    return (
      <div class="tx-walk-host sure" role="group" aria-label={sure === "battles" ? "Stop walking to choose a battle?" : "End the session?"}>
        {sure === "battles" ? <>{no}{yes}</> : <>{yes}{no}</>}
      </div>
    );
  }
  return (
    <div class="tx-walk-host">
      {canBattle ? <button type="button" class="tx-btn ghost" onClick={() => { sfx("select"); setSure("battles"); }}>Choose a battle</button> : <span />}
      <button type="button" class="tx-btn ghost" onClick={() => { sfx("select"); setSure("endSession"); }}>End session</button>
      {difficulty ? <DifficultyPicker current={difficulty} send={send} /> : null}   {/* below the two buttons, so they stay where the confirm expects them */}
    </div>
  );
}

/** How long the list of what's in reach stays open untouched before it closes by itself. */
const PICK_MS = 12000;

/** One thing the leader can use: a beat's spot (no target, so the server does the beat) or a thing to interact with. */
export interface InteractChoice {
  label: string;
  target: string | null;
}

/**
 * What the leader can use, the button's first: the beat in reach, when the server says the button is the beat's (`beat`), then each thing
 * in reach, nearest first (interactables design §2.5). A server that sends no list gives the button alone, as the beat's.
 */
export function interactChoices(interact: string | null | undefined, targets: WalkData["targets"], beat?: boolean): InteractChoice[] {
  const things = (targets ?? []).map((t) => ({ label: t.label, target: t.id }));
  const isBeat = targets === undefined ? !!interact : beat === true;
  return [...(isBeat && interact ? [{ label: interact, target: null }] : []), ...things];
}

/**
 * The leader's Interact button, while a beat's spot or a thing is in reach: "Talk", "Open the chest"… It sits at the foot of the info
 * column, never between the pad and the thumb, so its coming and going doesn't move the pad. With more than one in reach, a second button
 * beside it opens the list of all of them, and a pick uses that one. One use per view: a second tap of a double tap is dropped, and it
 * wakes again if the server didn't take it (out of reach by then).
 */
function Interact({ choices, send, viewId }: { choices: InteractChoice[]; send: CustomScreenProps["send"]; viewId: string }) {
  const sfx = useSfx();
  const [sent, setSent] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const sentFor = useRef<string | null>(null);
  // Each touch in the list starts its wait again, so it never closes under a hand that's still choosing.
  const [touched, setTouched] = useState(0);
  const more = useRef<HTMLButtonElement>(null);
  const main = useRef<HTMLButtonElement>(null);
  const list = useRef<HTMLDivElement>(null);
  const listId = useId();
  const shown = open && choices.length > 1;
  /** Back to the Interact button, if the keyboard's focus was in the list (or on what closed it): it's never left on nothing. */
  const refocus = () => {
    const at = document.activeElement;
    if (!at || at === document.body || list.current?.contains(at) || at === more.current) main.current?.focus();
  };
  useEffect(() => {
    if (!sent) return;
    const id = setTimeout(() => { sentFor.current = null; setSent(null); }, INTERACT_REARM_MS);
    return () => clearTimeout(id);
  }, [sent]);
  useEffect(() => {
    if (shown) list.current?.querySelector<HTMLButtonElement>("button")?.focus();
  }, [shown]);
  useEffect(() => {
    if (!shown) return;
    const id = setTimeout(() => { refocus(); setOpen(false); }, PICK_MS);
    return () => clearTimeout(id);
  }, [shown, touched]);
  useEffect(() => {
    if (open && choices.length < 2) {
      setOpen(false);
      main.current?.focus();      // the list went from under it: the focus goes back to the button, not to the page
    }
  }, [open, choices.length]);

  // Asleep, it's only aria-disabled, not disabled: a disabled button would drop the keyboard's focus to the page.
  const busy = sent === viewId;
  const use = (choice: InteractChoice) => {
    if (sentFor.current === viewId) return;
    sentFor.current = viewId;
    setSent(viewId);
    sfx("confirm");
    if (choice.target === null) send("interact");
    else send("interact", { target: choice.target });
  };
  const close = () => {
    sfx("cancel");
    refocus();
    setOpen(false);
  };
  const touch = () => setTouched((n) => n + 1);
  const first = choices[0]!;
  return (
    <div class="tx-interact-dock">
      {shown ? (
        <div class="tx-interact-pick" id={listId} role="group" aria-label="What to use" ref={list}
          onPointerDown={touch} onFocusIn={touch} onKeyDown={(e) => { touch(); if (e.key === "Escape") close(); }}>
          {choices.map((c) => (
            <button key={c.target ?? ""} type="button" class="tx-btn gold" aria-disabled={busy}
              onClick={() => { use(c); setOpen(false); main.current?.focus(); }}>
              {c.label}
            </button>
          ))}
          <button type="button" class="tx-btn ghost" onClick={close}>Back</button>
        </div>
      ) : null}
      <div class="tx-interact-row">
        <button type="button" class="tx-btn gold tx-interact" aria-disabled={busy} ref={main} onClick={() => use(first)}>
          <LineIcon name="speech" />
          {first.label}
        </button>
        {choices.length > 1 ? (
          <button type="button" class="tx-btn ghost tx-interact-more" ref={more} aria-expanded={shown} aria-controls={listId}
            aria-disabled={busy} aria-label={`${choices.length} things in reach: choose one`}
            onClick={() => { if (shown) close(); else if (!busy) { sfx("select"); setOpen(true); } }}>
            <span aria-hidden="true">{choices.length}</span>
            <span class="tx-interact-caret" aria-hidden="true" />
          </button>
        ) : null}
      </div>
    </div>
  );
}

/**
 * The leader's Search (interactables design §7.4), in a region with things: the party's Investigation check for whatever is hidden
 * nearby. It's there whether or not anything is, so it gives nothing away, and it never comes and goes under a thumb. One send per
 * view, as Interact's: a double tap sends once, and it wakes again if the server didn't take it.
 */
function Search({ send, viewId }: { send: CustomScreenProps["send"]; viewId: string }) {
  const sfx = useSfx();
  const [sent, setSent] = useState<string | null>(null);
  const sentFor = useRef<string | null>(null);      // a ref, so the second tap of a double tap sees the first before a render
  useEffect(() => {
    if (!sent) return;
    const id = setTimeout(() => { sentFor.current = null; setSent(null); }, INTERACT_REARM_MS);
    return () => clearTimeout(id);
  }, [sent]);
  const busy = sent === viewId;
  const search = () => {
    if (sentFor.current === viewId) return;
    sentFor.current = viewId;
    setSent(viewId);
    sfx("confirm");
    send("search");
  };
  return (
    <button type="button" class="tx-btn ghost tx-search" aria-disabled={busy} onClick={search}>
      <LineIcon name="search" />
      Search
    </button>
  );
}

function Party({ party }: { party: WalkMember[] }) {
  return (
    <ul class="tx-walk-party" aria-label="The party">
      {party.map((m) => (
        <li key={m.name} class={`${m.leads ? "leads" : ""}${m.here ? "" : " away"}`}>
          {m.leads ? <span class="tx-crown" title="Leads the party"><LineIcon name="crown" /></span> : null}
          {m.color ? <i class="tx-dot" style={{ "--c": m.color } as JSX.CSSProperties} aria-hidden="true" /> : null}
          <b>{m.name}</b>
          <span class="tx-walk-cls">{m.here ? m.cls : "away"}</span>
        </li>
      ))}
    </ul>
  );
}

/** A class's name as the art knows it. */
const classId = (name: string) => name.toLowerCase().replaceAll(" ", "_");

/**
 * Out in the world. The leader has the pad; everyone else sees who leads, what the party is out to do, their character and
 * the party. Every phone here keeps pinging, not only the leader's, because that's how a phone the server stopped hearing
 * wins its place back. Nothing on this screen changes as the party walks, so nothing re-renders under a held thumb.
 */
export function Walk({ data, send, stream, viewId }: CustomScreenProps) {
  const d = data as WalkData;
  const sfx = useSfx();
  const choices = useMemo(() => interactChoices(d.interact, d.targets, d.beat), [d.interact, d.targets, d.beat]);

  // The screen's `stream` closes over its view's id, so timers must reach the newest one, or all they send is dropped.
  const out = useRef(stream);
  out.current = stream;
  const sender = useMemo(() => new WalkSender((dir) => out.current("walk", { dir })), []);

  const view = useRef(viewId);
  useLayoutEffect(() => {
    if (view.current === viewId) return;
    view.current = viewId;
    sender.newView();
  }, [viewId]);

  useEffect(() => {
    const look = () => {
      if (document.visibilityState === "hidden") sender.sleep();
      else sender.wake();
    };
    const sleep = () => sender.sleep();
    const wake = () => sender.wake();
    look();
    document.addEventListener("visibilitychange", look);
    window.addEventListener("pagehide", sleep);
    window.addEventListener("pageshow", wake);
    return () => {
      document.removeEventListener("visibilitychange", look);
      window.removeEventListener("pagehide", sleep);
      window.removeEventListener("pageshow", wake);
      sender.dispose();
    };
  }, []);

  // A class, not state with a timer: CSS plays the arrival once, and nothing re-renders under the thumb that follows.
  // Opening the screen as leader is an arrival too. Its sound and buzz come from `useAlertOnMount`, which stays quiet
  // when a reconnect re-sends the view; taking the lead later plays them here.
  // A reconnect's remount inside the alert's quiet window doesn't pulse either.
  const alerted = useAlertOnMount("yourTurn", "walk", d.leader);
  const led = useRef(d.leader);
  const arrived = useRef(alerted);
  if (d.leader && !led.current) arrived.current = true;
  if (!d.leader) arrived.current = false;
  led.current = d.leader;
  const mounted = useRef(false);
  useEffect(() => {
    if (mounted.current && arrived.current) sfx("yourTurn");
    mounted.current = true;
  }, [d.leader]);

  if (d.ambush) return <div class="tx tx-mob-ambush" role="alert"><span class="tx-mob-warning" aria-hidden="true"><LineIcon name="sword" /></span><span class="tx-eyebrow">Danger on the road</span><h1>Ambush!</h1><p>{d.ambush} attack!</p><span class="tx-note">Watch the TV—the party is taking position.</span></div>;

  return (
    <div class={`tx tx-walk ${d.leader ? "lead" : "follow"}${arrived.current ? " arrived" : ""}`}>
      <div class="tx-walk-sidebar">
        {/* A status, so a screen reader hears who leads, and that it's this phone when the lead comes here. */}
        <div class="tx-walk-leadrow">
        <div class="tx-walk-who" role="status">
          <span class="tx-crown"><LineIcon name="crown" /></span>
          <b>{d.leader ? "You lead the party" : d.leaderName ? `${d.leaderName} leads` : "The party waits for someone to lead it"}</b>
        </div>
        {d.vip ? <details class="tx-walk-session" onKeyDown={e => { if (e.key === "Escape") e.currentTarget.open = false; }}><summary aria-label="Party menu">Party menu</summary><HostMenu canBattle={d.canBattle} difficulty={d.difficulty} send={send} /></details> : null}
        </div>
        <div class="tx-walk-info">
        {d.leader || d.leaderName
          ? <p class="tx-walk-hint">{d.leader ? "Hold a direction to walk. Follow the party on the TV map." : `${d.leaderName} steers. Follow the party on the TV map.`}</p>
          : null}
        {d.journal ? <WalkJournal journal={d.journal} pinned={d.pinned} notice={d.questNotice} send={send} stop={() => sender.release()} /> : null}
        {(d.canGive || d.give) && <Give data={d.give} gold={d.gold ?? 0} send={send} viewId={viewId} stop={() => sender.release()} />}
        <div class="tx-walk-goal">
          <div class="tx-walk-meta">
          {d.region ? <span class="tx-eyebrow">{d.region}</span> : null}
          {d.restReady && <span class="tx-rest-ready">Ready to rest</span>}
          {d.gold !== undefined && <span class="tx-shop-gold">◆ {d.gold} gold</span>}
          </div>
          <p>{d.objective}</p>
          {d.leader && d.danger ? <p class="tx-mob-danger" role="status">Danger nearby: {d.danger}</p> : null}
        </div>
        {d.leader ? null : (
          <div class="tx-me" style={d.me.color ? ({ "--c": d.me.color } as JSX.CSSProperties) : undefined} data-color={d.me.color ? "" : undefined}>
            {/* Player characters have no sprite: the class's portrait from the pack stands in, as on the lobby's cards. */}
            <Portrait unit={{ name: d.me.name, sprite: null, hp: 1, maxHp: 1, side: "Party" }} cls={classId(d.me.cls)} size={40} tone="you" />
            <div class="tx-me-name">
              <b>{d.me.name}</b>
              <span>{d.me.cls} · level {d.me.level}</span>
            </div>
          </div>
        )}
        <Party party={d.party} />
        {d.choices && d.choices.waiting > 0 ? <ChoicesCard choices={d.choices} send={send} /> : null}
        {d.portal && !d.leader ? <p><a class="tx-link" href={d.portal} target="_blank" rel="noopener">Your character</a></p> : null}
      </div>
        {d.leader && d.search ? (
          <div class="tx-walk-dock">
            {choices.length > 0 ? <Interact choices={choices} send={send} viewId={viewId} /> : null}
            <Search send={send} viewId={viewId} />
          </div>
        ) : d.leader && choices.length > 0 ? <div class="tx-walk-dock"><Interact choices={choices} send={send} viewId={viewId} /></div> : null}
      </div>
      {d.leader ? <Pad sender={sender} /> : null}
    </div>
  );
}
