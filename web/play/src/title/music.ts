/**
 * The title theme (`music.title`, looped), faded in and out so starting it on the first key press doesn't jolt.
 * `play()` resolves false when the browser still refuses (no gesture yet).
 */
export class TitleMusic {
  private audio: HTMLAudioElement;
  private fade = 0;
  started = false;

  constructor(src: string, private volume = 0.7) {
    this.audio = new Audio(src);
    this.audio.loop = true;
    this.audio.preload = "auto";
    this.audio.volume = 0;
  }

  async play(): Promise<boolean> {
    this.started = true;
    try {
      await this.audio.play();
    } catch {
      this.started = false;
      return false;
    }
    this.ramp(this.volume, 2000);
    return true;
  }

  stop(): void {
    this.ramp(0, 600, () => this.audio.pause());
  }

  private ramp(to: number, ms: number, done?: () => void): void {
    cancelAnimationFrame(this.fade);
    const from = this.audio.volume, t0 = performance.now();
    const step = () => {
      const k = Math.min(1, (performance.now() - t0) / ms);
      this.audio.volume = from + (to - from) * k;
      if (k < 1) this.fade = requestAnimationFrame(step);
      else done?.();
    };
    step();
  }
}
