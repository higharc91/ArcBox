import { useEffect, useState } from "preact/hooks";

/**
 * The tactics content pack, as the server serves it: `/content/tactics/index.json` names the base pack's current hash,
 * and every file is at `/content/tactics/base/<hash>/<path>` (immutable, so the browser caches it for good). The index
 * is fetched once per page; each JSON file once. Everything resolves null when the pack can't be reached (tests, an
 * old server), and callers fall back to drawing without art.
 */

let base: Promise<string | null> | null = null;

/** The base pack's URL prefix ("/content/tactics/base/<hash>/"), or null. */
export function packBase(): Promise<string | null> {
  if (!base) {
    base = (async () => {
      try {
        const res = await fetch("/content/tactics/index.json");
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const index = (await res.json()) as { packId: string; hash: string }[];
        const pack = index.find((p) => p.packId === "base");
        return pack ? `/content/tactics/base/${pack.hash}/` : null;
      } catch (err) {
        console.warn("Couldn't load the tactics content index", err);
        base = null;   // try again next time
        return null;
      }
    })();
  }
  return base;
}

const json = new Map<string, Promise<unknown>>();

/** A JSON file from the pack, by its path in the pack ("icons/manifest.json"); null if it can't load. Cached. */
export function packJson<T>(path: string): Promise<T | null> {
  let pending = json.get(path);
  if (!pending) {
    pending = (async () => {
      const prefix = await packBase();
      if (!prefix) {
        json.delete(path);
        return null;
      }
      try {
        const res = await fetch(prefix + path);
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return (await res.json()) as T;
      } catch (err) {
        console.warn("Couldn't load", path, err);
        json.delete(path);
        return null;
      }
    })();
    json.set(path, pending);
  }
  return pending as Promise<T | null>;
}

/** The pack's URL prefix once known; null until then (and when there's no pack). */
export function usePackBase(): string | null {
  return usePromise(packBase, "");
}

/** A pack JSON file once loaded; null until then. */
export function usePackJson<T>(path: string | null): T | null {
  return usePromise(() => (path ? packJson<T>(path) : Promise.resolve(null)), path ?? "");
}

function usePromise<T>(load: () => Promise<T | null>, key: string): T | null {
  const [value, setValue] = useState<T | null>(null);
  useEffect(() => {
    let live = true;
    void load().then((v) => { if (live) setValue(v); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  return value;
}
