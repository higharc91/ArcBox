import { useEffect, useRef, useState } from "preact/hooks";

/** Upper bound for the cosmetic countdown, so a phone with a badly skewed clock still shows something sane. */
export const MAX_COUNTDOWN_SECONDS = 60;

/**
 * Whole seconds left until a server-time deadline, clamped to [0, 60]. Purely cosmetic: the server owns the timer
 * and input is never blocked on the client because of it.
 */
export function remainingSeconds(deadlineUtc: string, now: number): number {
  const deadline = Date.parse(deadlineUtc);
  if (Number.isNaN(deadline)) return 0;
  return Math.min(MAX_COUNTDOWN_SECONDS, Math.max(0, Math.ceil((deadline - now) / 1000)));
}

const format = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;

export function Countdown({ deadlineUtc }: { deadlineUtc: string }) {
  const [now, setNow] = useState(() => Date.now());
  const total = useRef<{ deadline: string; seconds: number } | null>(null);

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 250);
    return () => clearInterval(id);
  }, []);

  const left = remainingSeconds(deadlineUtc, now);
  // The bar's full length is the time left when this deadline was first seen.
  if (!total.current || total.current.deadline !== deadlineUtc) total.current = { deadline: deadlineUtc, seconds: Math.max(1, left) };
  const exact = Math.min(MAX_COUNTDOWN_SECONDS, Math.max(0, (Date.parse(deadlineUtc) - now) / 1000)) || 0;
  const fraction = Math.min(1, exact / total.current.seconds);

  return (
    <div class={`ptimer${left <= 3 ? " urgent" : ""}`} role="timer" aria-live="off">
      <b>{format(left)}</b>
      <div class="bar"><i style={{ width: `${(fraction * 100).toFixed(1)}%` }} /></div>
    </div>
  );
}
