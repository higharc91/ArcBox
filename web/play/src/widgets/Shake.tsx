import { useEffect, useMemo, useRef, useState } from "preact/hooks";
import { useFeedback } from "../feedback/feedback";
import type { ShakeView } from "../protocol";
import { useSkin } from "../games/module";
import { Bottle, Gauge, mix, safeColor, type CorkState, type Spray } from "./Bottle";
import { ChargeModel, G } from "./chargeModel";
import { Prompt, type WidgetProps } from "./common";
import { browserMotion, useMotionCharge, type MotionSample, type MotionSource } from "./motion";

/** Charge updates go out at most this often (podium spec §3: ≤ 4/s)… */
export const SEND_INTERVAL_MS = 250;
/** …and only when the charge moved by at least this much since the last one. */
export const SEND_STEP = 0.05;
/** The server ignores fires closer together than this; the button does too, so it never lies. */
export const FIRE_COOLDOWN_MS = 400;
/** The clock: decay, trailing sends and the redraw. */
const TICK_MS = 50;
/** After a pop, "Shake again to reload" until the bottle has this much again. */
const RELOADED = 0.25;
/** REDLINE red, for a view without a colour. */
export const DEFAULT_COLOR = "#e10600";
const DEFAULT_HOLD = "Hold on — not yet.";
export const RELOAD_PROMPT = "Shake again to reload";

const round2 = (v: number) => Math.round(v * 100) / 100;

/**
 * The bottle's wobble: a damped spring on the tilt, pushed by taps, by sideways motion and by the pop's recoil, and
 * leaning against the phone's own tilt. Writes the transform straight to the element (no re-render per frame) and
 * sleeps when it settles.
 */
function useWobble() {
  const el = useRef<HTMLDivElement>(null);
  const s = useRef({ angle: 0, vel: 0, lean: 0, gravityX: 0, frame: 0, last: 0 });

  const step = (t: number) => {
    const w = s.current;
    const dt = Math.min(0.05, w.last ? (t - w.last) / 1000 : 0.016);
    w.last = t;
    w.vel += (-130 * (w.angle - w.lean) - 7 * w.vel) * dt;
    w.angle = Math.max(-28, Math.min(28, w.angle + w.vel * dt));
    if (el.current) el.current.style.transform = `rotate(${w.angle.toFixed(2)}deg)`;
    const settled = Math.abs(w.angle - w.lean) < 0.05 && Math.abs(w.vel) < 0.05;
    w.frame = settled ? 0 : requestAnimationFrame(step);
    if (settled) w.last = 0;
  };
  const wake = () => {
    if (typeof requestAnimationFrame !== "function" || s.current.frame) return;
    if (globalThis.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;
    s.current.frame = requestAnimationFrame(step);
  };
  useEffect(() => () => {
    if (s.current.frame) cancelAnimationFrame(s.current.frame);
  }, []);

  return {
    ref: el,
    /** A knock: `deg/s` of spin, sign included. */
    kick(spin: number) {
      s.current.vel += spin;
      wake();
    },
    /** A motion sample: sideways jerks shake the bottle; the slow part (gravity: the phone's tilt) leans it upright. */
    motion(lateral: number) {
      const w = s.current;
      w.gravityX += (lateral - w.gravityX) * 0.08;
      w.lean = Math.max(-14, Math.min(14, (w.gravityX / G) * 30));
      w.vel -= (lateral - w.gravityX) * 9;
      wake();
    },
  };
}

/**
 * "Shake to charge, tap to fire" (podium spec §3–4). The charge lives here: shaking the phone (or tapping the bottle,
 * without motion) fills it, stillness lets it leak slowly. Updates go to the server at most 4×/s and on a 0.05 change;
 * a fire always carries the charge, then the bottle is empty. `motion` is injectable for tests.
 */
export function Shake({ view, send, motion }: WidgetProps<ShakeView> & { motion?: MotionSource }) {
  const skin = useSkin();
  const feedback = useFeedback();
  const source = useMemo(() => motion ?? browserMotion(), [motion]);
  const model = useMemo(() => new ChargeModel(view.initialCharge), []);
  const io = useRef({ viewId: "", lastSent: 0, lastSentAt: -Infinity, lastFireAt: -Infinity, tenths: 0, shown: -1, popped: false, sprays: 0, knock: 1 });
  const [, redraw] = useState(0);
  const [spray, setSpray] = useState<Spray | null>(null);
  const wobble = useWobble();

  // A new view seeds the charge it carries (the server already has it, so nothing to send).
  if (io.current.viewId !== view.viewId) {
    io.current.viewId = view.viewId;
    model.set(view.initialCharge);
    io.current.lastSent = model.value;
    io.current.tenths = Math.floor(model.value * 10);
    io.current.popped = false;
  }

  // Handlers run outside render (timers, motion events): they read the latest props through this ref.
  const latest = useRef({ view, send, feedback });
  latest.current = { view, send, feedback };

  /** After any change to the charge: send if due, cue the fizz, reload the cork, redraw if it moved. */
  const sync = (now = Date.now()) => {
    const st = io.current;
    const { send, feedback } = latest.current;
    const v = round2(model.value);
    if (Math.abs(v - st.lastSent) >= SEND_STEP - 1e-9 && now - st.lastSentAt >= SEND_INTERVAL_MS) {
      send({ charge: v, fire: false });
      st.lastSent = v;
      st.lastSentAt = now;
    }
    const tenths = Math.floor(model.value * 10 + 1e-9);
    if (tenths > st.tenths) feedback?.charging(model.value);
    st.tenths = tenths;
    const reloaded = st.popped && model.value >= RELOADED;
    if (reloaded) st.popped = false;
    if (reloaded || Math.abs(model.value - st.shown) > 0.001) redraw((n) => n + 1);
  };

  const fire = () => {
    const st = io.current;
    const { view, send, feedback } = latest.current;
    const now = Date.now();
    if (!view.canFire || now - st.lastFireAt < FIRE_COOLDOWN_MS) return;
    const charge = round2(model.value);
    send({ charge, fire: true }); // charge first: the server applies it, then records the spray
    st.lastFireAt = now;
    st.lastSent = 0;
    st.lastSentAt = now;
    st.tenths = 0;
    st.popped = true;
    model.reset();
    feedback?.pop();
    setSpray({ id: ++st.sprays, strength: Math.max(charge, 0.15) });
    wobble.kick(-160 - 220 * charge);
    redraw((n) => n + 1);
  };

  const onSample = (sample: MotionSample) => {
    wobble.motion(sample.lateral);
    if (latest.current.view.canFire && model.isFlick(sample.mag)) {
      fire();
      return;
    }
    model.addMotion(sample.mag, sample.dt);
    sync();
  };
  const { status, enable } = useMotionCharge(source, onSample);

  const tap = () => {
    model.addTap();
    io.current.knock = -io.current.knock;
    wobble.kick(io.current.knock * (90 + 60 * model.value));
    sync();
  };

  useEffect(() => {
    let last = Date.now();
    const timer = setInterval(() => {
      const now = Date.now();
      model.decay((now - last) / 1000);
      last = now;
      sync(now);
    }, TICK_MS);
    return () => clearInterval(timer);
  }, []);

  const charge = model.value;
  io.current.shown = charge;
  const color = safeColor(view.color) ?? DEFAULT_COLOR;
  const full = charge >= 0.9;
  // The cork flies on a pop and comes back once the bottle is reloaded (or a new view arrives).
  const cork: CorkState = io.current.popped ? "flying" : io.current.sprays > 0 ? "reseat" : "in";
  const text = !view.canFire
    ? `${skin?.shakeHold?.(view) ?? DEFAULT_HOLD} ${view.prompt}`
    : io.current.popped
      ? RELOAD_PROMPT
      : view.prompt;

  let help;
  if (status === "ask") {
    help = (
      <button type="button" class="enable-motion" onClick={enable}>
        <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <rect x="7" y="3" width="10" height="18" rx="2" />
          <path d="M3 8c-1 2.5-1 5.5 0 8M21 8c1 2.5 1 5.5 0 8" />
        </svg>
        Tap to enable shaking
      </button>
    );
  } else if (status === "off") {
    help = <p class="shake-hint">Tap the bottle fast to shake it</p>;
  } else if (status === "live" && view.canFire) {
    help = <p class="shake-hint">Shake it, then flick to pop!</p>;
  } else {
    help = <p class="shake-hint">Shake your phone!</p>;
  }

  return (
    <div class={`w-shake${full ? " full" : ""}${view.canFire ? " armed" : " holding"}`} style={{ "--accent": color, "--glow": mix(color, "#ffffff", 0.3), "--charge": charge.toFixed(3) }} data-motion={status}>
      <Prompt text={text} />
      <div class="shake-stage">
        <div class="bottle-wrap" ref={wobble.ref}>
          <button
            type="button"
            class="bottle-tap"
            aria-label="Shake the bottle"
            onPointerDown={(e) => {
              e.preventDefault();
              tap();
            }}
            onClick={(e) => {
              if (e.detail === 0) tap(); // keyboard: Enter or Space (a pointer tap already counted on pointerdown)
            }}
          >
            <Bottle color={color} charge={charge} cork={cork} spray={spray} />
          </button>
        </div>
        <Gauge charge={charge} />
        {spray ? <div class="pop-flash" key={spray.id} /> : null}
      </div>
      <div class="shake-help">{help}</div>
      <div class="actions">
        <button type="button" class="primary pop-btn" disabled={!view.canFire} onClick={fire}>
          POP!
        </button>
      </div>
    </div>
  );
}
