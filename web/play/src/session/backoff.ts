const SCHEDULE_SECONDS = [0, 1, 2, 5, 10];

/** Reconnect delay in ms before retry number `attempt` (0-based): 0, 1, 2, 5, then 10 s forever. Never gives up. */
export function backoffDelays(attempt: number): number {
  return SCHEDULE_SECONDS[Math.min(Math.max(0, attempt), SCHEDULE_SECONDS.length - 1)]! * 1000;
}
