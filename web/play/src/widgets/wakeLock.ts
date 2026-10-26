// Keeps the phone screen on while an input widget is showing. Needs a secure context (HTTPS); every failure is ignored.

let sentinel: WakeLockSentinel | null = null;
let wanted = false;
let pending: Promise<void> | null = null;

export async function syncWakeLock(active: boolean): Promise<void> {
  wanted = active;
  if (pending) await pending;
  pending = apply().finally(() => (pending = null));
  return pending;
}

async function apply(): Promise<void> {
  try {
    if (wanted) {
      if (sentinel && !sentinel.released) return;
      const wakeLock = (navigator as Navigator & { wakeLock?: WakeLock }).wakeLock;
      if (!wakeLock) return;
      sentinel = await wakeLock.request("screen");
    } else if (sentinel) {
      const s = sentinel;
      sentinel = null;
      await s.release();
    }
  } catch {
    sentinel = null;
  }
}

/** The browser drops the lock when the page is hidden; take it again on return if it is still wanted. */
export function installWakeLockRefresh(): void {
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && wanted) void syncWakeLock(true);
  });
}
