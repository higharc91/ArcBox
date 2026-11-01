/** What the walk pad sends, and when (overworld 5b §1). No DOM here: walk.tsx turns thumbs into `press` and `release`. */

/** A screen direction. The server maps it to a tile. */
export type Dir = "n" | "ne" | "e" | "se" | "s" | "sw" | "w" | "nw";

/** Clockwise from up. */
export const DIRS: readonly Dir[] = ["n", "ne", "e", "se", "s", "sw", "w", "nw"];

/** The pad's dead zone, as a share of its radius. The hub drawn on the pad is this size. */
export const DEAD_ZONE = 0.18;
/** How far past a sector's edge a held direction holds, in degrees, so a thumb resting on the edge doesn't flicker. */
const STICK_DEGREES = 6;
/** How far into the dead zone a held direction holds, as a share of it. */
const STICK_DEAD_ZONE = 0.75;

/** Repeats while held: each one extends the server's lease. */
export const PING_MS = 250;
/** Repeats while not held, on every walk view: a phone the server has stopped hearing wins its place back with this. */
export const IDLE_PING_MS = 3000;
/** The second stop after a release. A stop is idempotent, so this is only a retry. */
export const STOP_RETRY_MS = 150;
/** Direction changes go out at most this often. A stop isn't held back. */
export const CHANGE_MS = 66;

/** Degrees clockwise from up. */
const bearing = (dx: number, dy: number) => (Math.atan2(dx, -dy) * 180) / Math.PI;

/**
 * The sector of a point relative to the pad's centre, in radii with y down; null in the dead zone. Only the angle counts
 * outside it, so a thumb that has slid off the pad steers just the same.
 */
export function dirAt(dx: number, dy: number, deadZone = DEAD_ZONE): Dir | null {
  if (Math.hypot(dx, dy) <= deadZone) return null;
  const sector = Math.round(bearing(dx, dy) / 45);
  return DIRS[(sector + 8) % 8]!;
}

/** `dirAt` for a thumb already holding `held`, which it keeps until the thumb has clearly left it. */
export function stickyDirAt(dx: number, dy: number, deadZone: number, held: Dir | null): Dir | null {
  if (held === null) return dirAt(dx, dy, deadZone);
  const next = dirAt(dx, dy, deadZone * STICK_DEAD_ZONE);
  if (next === null || next === held) return next;
  const off = Math.abs(((bearing(dx, dy) - DIRS.indexOf(held) * 45 + 540) % 360) - 180);
  return off <= 22.5 + STICK_DEGREES ? held : next;
}

export interface WalkClock {
  now(): number;
  /** Runs `fn` after `ms`. The result cancels it. */
  after(ms: number, fn: () => void): () => void;
}

const systemClock: WalkClock = {
  now: () => Date.now(),
  after(ms, fn) {
    const id = setTimeout(fn, ms);
    return () => clearTimeout(id);
  },
};

/**
 * Says what's held, level-triggered, so that any lost message heals itself: at once on press, change or release, again
 * every `PING_MS` while held, and every `IDLE_PING_MS` while not. One timer does both repeats, re-armed by each send.
 * Everything goes through `send`, which the screen keeps pointing at the newest view.
 */
export class WalkSender {
  private held: Dir | null = null;
  private lastChange = -Infinity;
  private asleep = false;
  private disposed = false;
  private cancelTick: (() => void) | null = null;
  private cancelChange: (() => void) | null = null;
  private cancelRetry: (() => void) | null = null;

  constructor(
    private readonly send: (dir: Dir | null) => void,
    private readonly clock: WalkClock = systemClock,
  ) {
    this.arm();
  }

  /** The thumb is on `dir`. Holding one direction and sliding to another is just another press. */
  press(dir: Dir): void {
    if (this.disposed || dir === this.held) return;
    this.asleep = false;
    const moving = this.held !== null;
    this.held = dir;
    this.clearRetry();
    const wait = moving ? this.lastChange + CHANGE_MS - this.clock.now() : 0;
    if (wait <= 0) this.change();
    else this.cancelChange ??= this.clock.after(wait, () => this.change());
  }

  /** The thumb has gone, or is in the dead zone. */
  release(): void {
    if (this.disposed || this.held === null) return;
    this.held = null;
    this.clearChange();
    this.emit();
    this.clearRetry();
    this.cancelRetry = this.clock.after(STOP_RETRY_MS, () => {
      this.cancelRetry = null;
      this.emit();
    });
  }

  /** A new view: whatever went out under the old one may have been dropped, so say it again. */
  newView(): void {
    if (this.disposed || this.asleep) return;
    this.clearChange();
    this.emit();
  }

  /** The page has gone: stop, and let the server notice this phone is gone too. */
  sleep(): void {
    if (this.disposed) return;
    this.asleep = true;
    this.cancelTick?.();
    this.cancelTick = null;
    this.release();
  }

  /** The page is back. Heard at once, so a phone that lost the lead by going quiet doesn't wait for a tick to get it back. */
  wake(): void {
    if (this.disposed || !this.asleep) return;
    this.asleep = false;
    this.emit();
  }

  dispose(): void {
    if (this.disposed) return;
    if (this.held !== null) this.send(null);
    this.disposed = true;
    this.held = null;
    this.cancelTick?.();
    this.clearChange();
    this.clearRetry();
  }

  private change(): void {
    this.clearChange();
    this.lastChange = this.clock.now();
    this.emit();
  }

  private emit(): void {
    this.send(this.held);
    this.arm();
  }

  private arm(): void {
    this.cancelTick?.();
    this.cancelTick = this.asleep ? null : this.clock.after(this.held !== null ? PING_MS : IDLE_PING_MS, () => this.emit());
  }

  private clearChange(): void {
    this.cancelChange?.();
    this.cancelChange = null;
  }

  private clearRetry(): void {
    this.cancelRetry?.();
    this.cancelRetry = null;
  }
}
