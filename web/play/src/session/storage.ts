import type { PlayerId, ProfileKey } from "../protocol";

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

/** A profile key this phone holds: who it is, the credential, and when it was last used to pick them. */
export interface StoredProfile {
  profileId: string;
  key: string;
  name: string;
  lastUsed: number;
}

/** `arcbox:profiles`, or `arcbox:profiles:<slot>` on the dev page. */
export function profilesKey(slot?: string): string {
  return slot ? `arcbox:profiles:${slot}` : "arcbox:profiles";
}

/** The profile keys on this phone, most recently used first. Corrupt or blocked storage reads as none. */
export function loadProfileKeys(store: KeyValueStore, slot?: string): StoredProfile[] {
  try {
    const raw = store.getItem(profilesKey(slot));
    if (!raw) return [];
    const list: unknown = JSON.parse(raw);
    if (!Array.isArray(list)) return [];
    return list
      .filter((p): p is StoredProfile =>
        typeof p === "object" && p !== null && typeof p.profileId === "string" && typeof p.key === "string" &&
        typeof p.name === "string" && typeof p.lastUsed === "number")
      .map((p) => ({ profileId: p.profileId, key: p.key, name: p.name, lastUsed: p.lastUsed }))
      .sort((a, b) => b.lastUsed - a.lastUsed);
  } catch {
    return [];
  }
}

/** Remembers a key the server just issued, replacing any entry for the same profile and marking it used `now`. */
export function saveProfileKey(store: KeyValueStore, slot: string | undefined, key: ProfileKey, now: number): void {
  try {
    const others = loadProfileKeys(store, slot).filter((p) => p.profileId !== key.id);
    const next: StoredProfile[] = [{ profileId: key.id, key: key.key, name: key.name, lastUsed: now }, ...others];
    store.setItem(profilesKey(slot), JSON.stringify(next));
  } catch {
    // Storage blocked: the phone just won't remember who it is next week.
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
