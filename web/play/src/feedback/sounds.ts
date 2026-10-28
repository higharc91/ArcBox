/**
 * Tiny WebAudio-synthesised sounds (audio spec §3.4): no asset files, each a short oscillator or noise envelope.
 * iOS only lets a page start audio from a user gesture, so the AudioContext is created and resumed in `unlock()`,
 * which the app calls from taps (the Join button, the mute toggle, any tap after a rejoin). Before that, and on a
 * browser without WebAudio, `play` does nothing. Nothing here throws.
 */

export type SoundName = "tap" | "tick" | "correct" | "wrong" | "spin" | "fizz" | "pop";

/** The loudest any envelope goes: quiet by default, the phone's volume does the rest. */
export const MAX_GAIN = 0.25;

/** Exponential ramps cannot reach 0; this is silence for our purposes (−80 dB). */
const FLOOR = 0.0001;

type ContextClass = new () => AudioContext;

function contextClass(): ContextClass | undefined {
  const g = globalThis as { AudioContext?: ContextClass; webkitAudioContext?: ContextClass };
  return g.AudioContext ?? g.webkitAudioContext;
}

/** A gain node shaped as a percussive envelope (fast attack to `peak`, exponential decay to silence), into the speakers. */
function envelope(ctx: AudioContext, at: number, peak: number, length: number, attack = 0.004): GainNode {
  const g = ctx.createGain();
  g.gain.setValueAtTime(FLOOR, at);
  g.gain.exponentialRampToValueAtTime(Math.min(peak, MAX_GAIN), at + attack);
  g.gain.exponentialRampToValueAtTime(FLOOR, at + length);
  g.connect(ctx.destination);
  return g;
}

interface Tone {
  type: OscillatorType;
  /** Start frequency in Hz; `to` glides to another over the tone's length. */
  from: number;
  to?: number;
  peak: number;
  /** Seconds, including the decay. */
  length: number;
  delay?: number;
}

function tone(ctx: AudioContext, at: number, t: Tone, into?: AudioNode): void {
  const start = at + (t.delay ?? 0);
  const osc = ctx.createOscillator();
  osc.type = t.type;
  osc.frequency.setValueAtTime(t.from, start);
  if (t.to) osc.frequency.exponentialRampToValueAtTime(t.to, start + t.length);
  osc.connect(into ?? envelope(ctx, start, t.peak, t.length));
  osc.start(start);
  osc.stop(start + t.length);
}

/**
 * Champagne noise: mostly a soft hiss with sparse loud grains, the crackle of bubbles bursting. `density` is the share
 * of samples that are grains.
 */
function fizzBuffer(ctx: AudioContext, length: number, density: number): AudioBuffer {
  const buffer = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * length), ctx.sampleRate);
  const data = buffer.getChannelData(0);
  for (let i = 0; i < data.length; i++) data[i] = (Math.random() * 2 - 1) * (Math.random() < density ? 1 : 0.12);
  return buffer;
}

/** Noise from `buffer` through a filter into an envelope, from `at` for `length` s. */
function filteredNoise(ctx: AudioContext, at: number, buffer: AudioBuffer, filter: { type: BiquadFilterType; from: number; to?: number; q?: number }, peak: number, length: number, attack: number): void {
  const src = ctx.createBufferSource();
  src.buffer = buffer;
  const f = ctx.createBiquadFilter();
  f.type = filter.type;
  f.Q.setValueAtTime(filter.q ?? 0.7, at);
  f.frequency.setValueAtTime(filter.from, at);
  if (filter.to) f.frequency.exponentialRampToValueAtTime(filter.to, at + length);
  src.connect(f);
  f.connect(envelope(ctx, at, peak, length, attack));
  src.start(at);
  src.stop(at + length);
}

const RECIPES: Record<SoundName, (ctx: AudioContext, at: number, level: number) => void> = {
  /** A soft "tok": a sine dropping an octave in 45 ms. */
  tap: (ctx, at) => tone(ctx, at, { type: "sine", from: 880, to: 440, peak: 0.18, length: 0.045 }),

  /** A dry wood-block tick for the last seconds of the clock. */
  tick: (ctx, at) => tone(ctx, at, { type: "triangle", from: 1480, peak: 0.12, length: 0.05 }),

  /** A rising C-major arpeggio (C6 E6 G6) of bell-like sines with a quiet octave partial; 0.56 s. */
  correct: (ctx, at) => {
    [1046.5, 1318.5, 1568].forEach((f, i) => {
      tone(ctx, at, { type: "sine", from: f, peak: 0.15, length: 0.42, delay: i * 0.07 });
      tone(ctx, at, { type: "sine", from: f * 2, peak: 0.04, length: 0.2, delay: i * 0.07 });
    });
  },

  /** A low, filtered buzz sagging in pitch: two slightly detuned saws beating against each other. */
  wrong: (ctx, at) => {
    const length = 0.26;
    const env = envelope(ctx, at, 0.1, length, 0.01);
    const lowpass = ctx.createBiquadFilter();
    lowpass.type = "lowpass";
    lowpass.frequency.setValueAtTime(900, at);
    lowpass.connect(env);
    for (const f of [146, 155]) tone(ctx, at, { type: "sawtooth", from: f, to: f * 0.75, peak: 0, length }, lowpass);
  },

  /** A tyre-squeal whoosh: noise through a band-pass sweeping down from 2.6 kHz. */
  spin: (ctx, at) => {
    const length = 0.28;
    const buffer = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * length), ctx.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
    const noise = ctx.createBufferSource();
    noise.buffer = buffer;
    const band = ctx.createBiquadFilter();
    band.type = "bandpass";
    band.Q.setValueAtTime(3, at);
    band.frequency.setValueAtTime(2600, at);
    band.frequency.exponentialRampToValueAtTime(380, at + length);
    noise.connect(band);
    band.connect(envelope(ctx, at, MAX_GAIN, length, 0.01));
    noise.start(at);
    noise.stop(at + length);
  },

  /** Charging: a short fizz that gets brighter and louder as the pressure (`level` 0..1) rises; 0.22 s. */
  fizz: (ctx, at, level) => {
    const l = Math.min(1, Math.max(0, level));
    filteredNoise(ctx, at, fizzBuffer(ctx, 0.22, 0.04 + 0.08 * l), { type: "highpass", from: 2400 + 3600 * l, q: 0.9 }, 0.03 + 0.08 * l, 0.22, 0.03);
  },

  /**
   * The cork: a hollow "plock" (a sine falling from 640 Hz, the bottle mouth), a low thump and a click of air, then
   * the champagne fizzing out, swelling and dying away; 0.9 s.
   */
  pop: (ctx, at) => {
    tone(ctx, at, { type: "sine", from: 640, to: 250, peak: MAX_GAIN, length: 0.07 });
    tone(ctx, at, { type: "sine", from: 160, to: 48, peak: 0.2, length: 0.14 });
    filteredNoise(ctx, at, fizzBuffer(ctx, 0.03, 1), { type: "bandpass", from: 2200, q: 0.8 }, 0.18, 0.03, 0.001);
    filteredNoise(ctx, at + 0.03, fizzBuffer(ctx, 0.85, 0.1), { type: "highpass", from: 5200, to: 2600, q: 0.8 }, 0.14, 0.85, 0.06);
  },
};

export class Sounds {
  private ctx: AudioContext | null = null;
  /** A resume() started inside a gesture has not settled yet; sounds scheduled now play once it does. */
  private resuming = false;

  /** True once a gesture has created the AudioContext. */
  get unlocked(): boolean {
    return this.ctx !== null;
  }

  /** Call from inside a user gesture: creates the AudioContext the first time, and resumes it whenever it is not running. */
  unlock(): void {
    if (!this.ctx) {
      try {
        const Context = contextClass();
        if (!Context) return;
        this.ctx = new Context();
      } catch {
        return; // e.g. too many contexts, or blocked: stay silent, try again on the next gesture
      }
      this.blip(this.ctx);
    }
    const ctx = this.ctx;
    if (ctx.state === "running") return;
    try {
      this.resuming = true;
      Promise.resolve(ctx.resume())
        .catch(() => {})
        .finally(() => (this.resuming = false));
    } catch {
      this.resuming = false;
    }
  }

  /** `level` (0..1) shapes sounds that vary, e.g. the fizz rising with the charge; the others ignore it. */
  play(name: SoundName, level = 1): void {
    const ctx = this.ctx;
    // A context that is suspended (or interrupted by a call on iOS) would queue sounds and play them all, late,
    // when it resumes: skip them instead, unless a resume from this very gesture is on its way.
    if (!ctx || (ctx.state !== "running" && !this.resuming)) return;
    try {
      RECIPES[name](ctx, ctx.currentTime + 0.005, level);
    } catch {
      // Audio is decoration: never let it break a tap.
    }
  }

  /** Older iOS only unlocks output once a sound has started inside the gesture: play one silent sample. */
  private blip(ctx: AudioContext): void {
    try {
      const src = ctx.createBufferSource();
      src.buffer = ctx.createBuffer(1, 1, ctx.sampleRate);
      src.connect(ctx.destination);
      src.start(0);
    } catch {
      // ignore
    }
  }
}
