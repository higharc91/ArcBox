import type { PlayerId } from "../protocol";

/** The subset of Web Storage the session needs (localStorage in the app, a Map in tests). */
export interface KeyValueStore {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export interface Seat {
  playerId: PlayerId;
  secret: string;
  name: string;
}

/** `arcbox:<CODE>`, or `arcbox:<CODE>:<slot>` on the dev page where iframes share localStorage. */
export function storageKey(code: string, slot?: string): string {
  const base = `arcbox:${code.toUpperCase()}`;
  return slot ? `${base}:${slot}` : base;
}

export function loadSeat(store: KeyValueStore, code: string, slot?: string): Seat | null {
  try {
    const raw = store.getItem(storageKey(code, slot));
    if (!raw) return null;
    const seat = JSON.parse(raw) as Partial<Seat>;
    if (typeof seat.playerId !== "string" || typeof seat.secret !== "string" || typeof seat.name !== "string") return null;
    return { playerId: seat.playerId, secret: seat.secret, name: seat.name };
  } catch {
    return null; // corrupt entry, or storage blocked (private mode)
  }
}

export function saveSeat(store: KeyValueStore, code: string, slot: string | undefined, seat: Seat): void {
  try {
    store.setItem(storageKey(code, slot), JSON.stringify(seat));
  } catch {
    // Storage blocked: the session still works, it just can't survive a reload.
  }
}

export function clearSeat(store: KeyValueStore, code: string, slot?: string): void {
  try {
    store.removeItem(storageKey(code, slot));
  } catch {
    // ignore
  }
}

/** localStorage when it is usable, else an in-memory stand-in. */
export function browserStore(): KeyValueStore {
  try {
    const probe = "arcbox:probe";
    localStorage.setItem(probe, "1");
    localStorage.removeItem(probe);
    return localStorage;
  } catch {
    const map = new Map<string, string>();
    return {
      getItem: (k) => map.get(k) ?? null,
      setItem: (k, v) => void map.set(k, v),
      removeItem: (k) => void map.delete(k),
    };
  }
}
