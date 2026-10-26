import { createContext } from "preact";
import { useContext } from "preact/hooks";
import type { ControllerView } from "../protocol";
import type { KeyValueStore } from "../session/storage";
import { skinFor, type ResultKind } from "../skins/skin";
import { PATTERNS, vibrate } from "./haptics";
import { Sounds } from "./sounds";

/** localStorage key for the speaker toggle: "1" muted, "0" not. Shared by every room. */
export const MUTE_KEY = "arcbox:mute";

/** The last seconds of an input view's clock that tick. */
const TICK_FROM = 3;

/**
 * Phone haptics and sounds (audio spec §3.4). Every game gets a tap on each input sent and countdown ticks in the
 * last 3 s of an input view; a game whose skin has `classifyResult` also gets a cue for its round results.
 * Mute silences both sounds and vibration and is remembered on this device.
 */
export class Feedback {
  private mutedValue: boolean;
  private listeners = new Set<(muted: boolean) => void>();
  private viewId: string | null = null;
  private input = false;
  private sent = false;
  private lastTick: number | null = null;
  private lastResult: string | null = null;

  constructor(
    private readonly store: KeyValueStore,
    private readonly sounds = new Sounds(),
  ) {
    this.mutedValue = readMuted(store);
  }

  get muted(): boolean {
    return this.mutedValue;
  }

  setMuted(muted: boolean): void {
    this.mutedValue = muted;
    try {
      this.store.setItem(MUTE_KEY, muted ? "1" : "0");
    } catch {
      // Storage blocked: the choice lasts until the page reloads.
    }
    for (const fn of this.listeners) fn(muted);
  }

  /** From the speaker toggle's tap: unmuting confirms with a tap, so the player hears (and feels) that it worked. */
  toggleMuted(): void {
    this.unlock();
    this.setMuted(!this.mutedValue);
    this.cue("tap");
  }

  subscribe(fn: (muted: boolean) => void): () => void {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  }

  /** Call inside a user gesture (iOS starts audio only there). Safe to call on every tap. */
  unlock(): void {
    this.sounds.unlock();
  }

  /**
   * Unlocks audio on any tap or key press on `target`, in the capture phase so a tap that also sends input already
   * has a running context. Covers a rejoin after a reload, which skips the Join tap. Returns the remover.
   */
  installGestureUnlock(target: EventTarget): () => void {
    const handler = () => this.unlock();
    const types = ["touchend", "click", "keydown"];
    for (const type of types) target.addEventListener(type, handler, { capture: true, passive: true });
    return () => {
      for (const type of types) target.removeEventListener(type, handler, { capture: true });
    };
  }

  /** An input was sent for the current view. */
  onSend(): void {
    this.sent = true;
    this.cue("tap");
  }

  /** A view arrived. Repeats of the current view (a rejoin re-sends it) are ignored. */
  onView(view: ControllerView, game?: string | null): void {
    if (view.viewId === this.viewId) return;
    this.viewId = view.viewId;
    this.input = view.kind !== "wait";
    this.sent = false;
    this.lastTick = null;
    const result = view.kind === "wait" ? skinFor(game)?.classifyResult?.(view) : null;
    const message = result && view.kind === "wait" ? view.message : null;
    // A result re-minted under a new id with the same wording (e.g. after a server restart) is the same result.
    const repeat = message !== null && message === this.lastResult;
    this.lastResult = message;
    if (result && !repeat) this.cue(result);
  }

  /** The countdown's whole seconds left; ticks once per second for the last 3 s of an input view not yet answered. */
  onCountdown(secondsLeft: number): void {
    if (!this.input || this.sent || secondsLeft < 1 || secondsLeft > TICK_FROM || secondsLeft === this.lastTick) return;
    this.lastTick = secondsLeft;
    if (!this.mutedValue) this.sounds.play("tick");
  }

  private cue(kind: "tap" | ResultKind): void {
    if (this.mutedValue) return;
    vibrate(PATTERNS[kind]);
    this.sounds.play(kind);
  }
}

function readMuted(store: KeyValueStore): boolean {
  try {
    return store.getItem(MUTE_KEY) === "1";
  } catch {
    return false;
  }
}

/** The app's Feedback; null in unit renders and anywhere else nothing should buzz. */
export const FeedbackContext = createContext<Feedback | null>(null);

export const useFeedback = () => useContext(FeedbackContext);
