import { useEffect, useRef, useState } from "preact/hooks";
import { G } from "./chargeModel";

/** The statics of `DeviceMotionEvent` this needs: iOS 13+ adds `requestPermission`, which must run inside a tap. */
export interface MotionApi {
  requestPermission?: () => Promise<string>;
}

/** Where motion comes from: the browser's `DeviceMotionEvent` and `window`, or a fake in tests. */
export interface MotionSource {
  /** Undefined when the browser has no motion API at all. */
  api?: MotionApi;
  /** Dispatches `devicemotion` events. */
  target: Pick<EventTarget, "addEventListener" | "removeEventListener">;
}

export function browserMotion(): MotionSource {
  const g = globalThis as { DeviceMotionEvent?: MotionApi };
  return { api: g.DeviceMotionEvent, target: typeof window === "undefined" ? new EventTarget() : window };
}

/**
 * - `ask`: iOS: motion needs a tap on "Tap to enable shaking" first.
 * - `waiting`: listening, no sample yet.
 * - `live`: samples are arriving.
 * - `off`: no motion (no API, permission denied, or no sample within NO_MOTION_MS, e.g. a desktop or plain HTTP):
 *   the bottle charges by tapping.
 */
export type MotionStatus = "ask" | "waiting" | "live" | "off";

/** A desktop browser has the API but never sends a sample: after this long, fall back to tapping. */
export const NO_MOTION_MS = 1500;

export interface MotionSample {
  /** Total acceleration in g, gravity included (1 at rest). */
  mag: number;
  /** Seconds this sample covers. */
  dt: number;
  /** Sideways acceleration (m/s², device x axis), for the bottle's wobble and tilt. */
  lateral: number;
}

interface Vec {
  x: number;
  y: number;
  z: number;
}

const vec = (v: unknown): Vec | null => {
  const o = v as Partial<Vec> | null | undefined;
  return o && [o.x, o.y, o.z].every((n) => typeof n === "number" && Number.isFinite(n)) ? (o as Vec) : null;
};
const length = (v: Vec) => Math.hypot(v.x, v.y, v.z);

/**
 * One `devicemotion` event as a sample, or null when it carries no data (Chrome on a desktop sends one empty event).
 * `acceleration` (gravity removed) is preferred where the phone has a gyroscope; it is put back on the same 1 g
 * footing as `accelerationIncludingGravity`. `interval` is in ms (older iOS: seconds).
 */
export function readSample(e: Event): MotionSample | null {
  const m = e as Event & { acceleration?: unknown; accelerationIncludingGravity?: unknown; interval?: unknown };
  const pure = vec(m.acceleration);
  const withGravity = vec(m.accelerationIncludingGravity);
  if (!pure && !withGravity) return null;
  const interval = typeof m.interval === "number" && m.interval > 0 ? m.interval : 16;
  const dt = Math.min(0.1, interval > 1 ? interval / 1000 : interval);
  return pure
    ? { mag: length(pure) / G + 1, dt, lateral: pure.x }
    : { mag: length(withGravity!) / G, dt, lateral: withGravity!.x };
}

/** Sources whose iOS permission was granted in this page: no need to ask again when the widget comes back. */
const granted = new WeakSet<MotionApi>();

const needsAsk = (api?: MotionApi) => typeof api?.requestPermission === "function" && !granted.has(api);

/**
 * Feeds `devicemotion` samples to `onSample` and reports whether motion works. On iOS it waits for `enable()`, which
 * must be called from a tap handler: it calls `requestPermission` synchronously, inside the gesture.
 */
export function useMotionCharge(source: MotionSource, onSample: (sample: MotionSample) => void): { status: MotionStatus; enable: () => void } {
  const [status, setStatus] = useState<MotionStatus>(() => (!source.api ? "off" : needsAsk(source.api) ? "ask" : "waiting"));
  const [listening, setListening] = useState(() => !!source.api && !needsAsk(source.api));
  const latest = useRef(onSample);
  latest.current = onSample;

  useEffect(() => {
    if (!listening) return;
    let live = false;
    const handler = (e: Event) => {
      const sample = readSample(e);
      if (!sample) return;
      if (!live) {
        live = true;
        setStatus("live");
      }
      latest.current(sample);
    };
    source.target.addEventListener("devicemotion", handler);
    const timer = setTimeout(() => live || setStatus("off"), NO_MOTION_MS);
    return () => {
      clearTimeout(timer);
      source.target.removeEventListener("devicemotion", handler);
    };
  }, [listening, source]);

  const enable = () => {
    const api = source.api;
    if (!api?.requestPermission) return;
    let request: Promise<string>;
    try {
      request = api.requestPermission(); // synchronously, inside the tap: iOS refuses it anywhere else
    } catch {
      setStatus("off");
      return;
    }
    Promise.resolve(request)
      .then((answer) => {
        if (answer === "granted") {
          granted.add(api);
          setStatus("waiting");
          setListening(true);
        } else {
          setStatus("off");
        }
      })
      .catch(() => setStatus("off"));
  };

  return { status, enable };
}
