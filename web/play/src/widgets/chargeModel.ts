/** Standard gravity, m/s². Motion magnitudes in this model are in g. */
export const G = 9.80665;

/** Total acceleration (gravity included) a shake must exceed, in g: resting reads 1 g, a wave of the hand ~1.3 g. */
export const SHAKE_THRESHOLD = 1.5;
/** Charge per second for each g above the threshold: a hard shake (~2.5 g on its peaks) fills the bottle in a few seconds. */
export const MOTION_RATE = 0.6;
/** One sample counts at most this many g above the threshold, so a single knock cannot fill the bottle. */
export const MAX_EXCESS = 2.5;
/** The longest gap between samples that counts (s); a stalled sensor does not turn into a burst of charge. */
export const MAX_DT = 0.1;
/** Charge per tap on the bottle (the fallback without motion): about 20 taps from empty to full. */
export const TAP_STEP = 0.05;
/** Seconds of stillness before the pressure starts to leak. */
export const DECAY_GRACE = 0.6;
/** Charge lost per second of stillness after the grace: a full bottle empties in ~20 s, longer than a podium phase. */
export const DECAY_RATE = 0.05;
/** A flick: a spike at least this sharp (g)… */
export const FLICK_G = 3.2;
/** …with at least this much charge to spray… */
export const FLICK_MIN_CHARGE = 0.25;
/** …after this long without shaking (s), so the peaks of a shake never fire by accident. */
export const FLICK_CALM = 0.35;

const clamp01 = (v: number) => (Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0);

/**
 * The bottle's pressure, 0..1: pure, with time passed in, so it is the same on every phone and in tests.
 * Shaking (`addMotion`) or tapping (`addTap`) adds charge; `decay` is the clock: it lets a still bottle leak slowly.
 */
export class ChargeModel {
  private charge: number;
  /** Seconds since the last shake or tap, counted by `decay`. */
  private still = 0;

  constructor(initial = 0) {
    this.charge = clamp01(initial);
  }

  get value(): number {
    return this.charge;
  }

  /** Seeds the charge (e.g. a new view's initialCharge); clamped, NaN is empty. */
  set(value: number): void {
    this.charge = clamp01(value);
    this.still = 0;
  }

  /** One motion sample: total acceleration `mag` in g over `dt` seconds. Above the threshold it charges, by intensity. */
  addMotion(mag: number, dt: number): void {
    if (!Number.isFinite(mag) || !Number.isFinite(dt) || dt <= 0) return;
    const excess = mag - SHAKE_THRESHOLD;
    if (excess <= 0) return;
    this.charge = clamp01(this.charge + MOTION_RATE * Math.min(excess, MAX_EXCESS) * Math.min(dt, MAX_DT));
    this.still = 0;
  }

  addTap(): void {
    this.charge = clamp01(this.charge + TAP_STEP);
    this.still = 0;
  }

  /** `dt` seconds passed: after `DECAY_GRACE` of stillness the charge leaks at `DECAY_RATE`. */
  decay(dt: number): void {
    if (!Number.isFinite(dt) || dt <= 0) return;
    const before = this.still;
    this.still += dt;
    const leaking = this.still - Math.max(before, DECAY_GRACE);
    if (leaking > 0) this.charge = clamp01(this.charge - DECAY_RATE * leaking);
  }

  /** Would a sample of `mag` g be a deliberate flick: sharp, with charge in the bottle, after a calm moment? */
  isFlick(mag: number): boolean {
    return Number.isFinite(mag) && mag >= FLICK_G && this.charge >= FLICK_MIN_CHARGE && this.still >= FLICK_CALM;
  }

  /** The cork is out: empty. */
  reset(): void {
    this.charge = 0;
    this.still = 0;
  }
}
