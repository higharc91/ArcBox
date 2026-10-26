/** Vibration patterns in ms (audio spec §3.4): a number is one buzz, an array alternates buzz and pause. */
export const PATTERNS = {
  tap: 15,
  correct: [30, 40, 30],
  wrong: 120,
  spin: [60, 30, 60, 30, 120],
} as const satisfies Record<string, number | readonly number[]>;

export type HapticName = keyof typeof PATTERNS;

/**
 * Vibrates when the browser supports it; false otherwise. iOS Safari has no `navigator.vibrate`, and Chrome ignores
 * it before the first user gesture, so a missing or refused vibration is never an error.
 */
export function vibrate(pattern: number | readonly number[]): boolean {
  const nav = globalThis.navigator as (Navigator & { vibrate?: Navigator["vibrate"]; userActivation?: { hasBeenActive: boolean } }) | undefined;
  if (typeof nav?.vibrate !== "function") return false;
  // Before the first tap Chrome refuses and logs an error (e.g. a result view right after a reload): skip it quietly.
  if (nav.userActivation && !nav.userActivation.hasBeenActive) return false;
  try {
    return nav.vibrate(typeof pattern === "number" ? pattern : [...pattern]);
  } catch {
    return false;
  }
}
