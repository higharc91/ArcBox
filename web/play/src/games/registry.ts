import { useEffect, useState } from "preact/hooks";
import type { GameModule } from "./module";

type Loader = () => Promise<{ module: GameModule }>;

/**
 * Game id → how to load its module. A dynamic import() per game, so Vite emits one chunk per game and a phone only
 * downloads the game its room selects. Add a game with one line here.
 */
const loaders: Record<string, Loader> = {
  redline: () => import("./redline"),
};

let loaded = new Map<string, GameModule>();
let pending = new Map<string, Promise<GameModule | undefined>>();
let failed = new Set<string>();
const listeners = new Set<() => void>();

function notify(): void {
  for (const fn of listeners) fn();
}

/** The game's module; undefined until it has loaded (and for a game that has none). */
export function moduleFor(gameId?: string | null): GameModule | undefined {
  return gameId ? loaded.get(gameId) : undefined;
}

/**
 * Loads the game's module. Idempotent: every call for an id returns the same promise. Resolves undefined, and never
 * rejects, for an id with no module or whose chunk failed to download (a later call tries again).
 */
export function loadModule(gameId?: string | null): Promise<GameModule | undefined> {
  if (!gameId) return Promise.resolve(undefined);
  const have = loaded.get(gameId);
  if (have) return Promise.resolve(have);
  const inFlight = pending.get(gameId);
  if (inFlight) return inFlight;
  const loader = Object.hasOwn(loaders, gameId) ? loaders[gameId] : undefined;
  if (!loader) return Promise.resolve(undefined);
  failed.delete(gameId);
  const promise = loader().then(
    ({ module }) => {
      loaded.set(gameId, module);
      pending.delete(gameId);
      notify();
      return module;
    },
    (err) => {
      console.warn(`Couldn't load the "${gameId}" module`, err);
      pending.delete(gameId);
      failed.add(gameId);
      notify();
      return undefined;
    },
  );
  pending.set(gameId, promise);
  return promise;
}

/** "loading": a module is on its way; "missing": it has loaded without what you want, or can't: there is none, or its chunk failed. */
export type ModuleState = "loaded" | "loading" | "missing";

export function moduleState(gameId?: string | null): ModuleState {
  if (!gameId) return "missing";
  if (loaded.has(gameId)) return "loaded";
  if (pending.has(gameId)) return "loading";
  // Not started yet but loadable: it is about to be.
  return Object.hasOwn(loaders, gameId) && !failed.has(gameId) ? "loading" : "missing";
}

/** Calls `fn` whenever a module finishes loading (or fails to). Returns the remover. */
export function subscribeModules(fn: () => void): () => void {
  listeners.add(fn);
  return () => void listeners.delete(fn);
}

/** A game's module and its state, re-rendering the component when the module arrives. Starts the load if nobody has. */
export function useGameModule(gameId?: string | null): { module: GameModule | undefined; state: ModuleState } {
  const [, setTick] = useState(0);
  const rerender = () => setTick((n) => n + 1);
  useEffect(() => {
    const off = subscribeModules(rerender);
    void loadModule(gameId); // a no-op when the session already started it
    rerender(); // it may have finished between the render and this effect
    return off;
  }, [gameId]);
  return { module: moduleFor(gameId), state: moduleState(gameId) };
}

/** For tests only: makes `module` the loaded module for `gameId`. */
export function registerModuleForTests(gameId: string, module: GameModule): void {
  loaded.set(gameId, module);
  pending.delete(gameId);
  failed.delete(gameId);
  notify();
}

/** For tests only: forgets every loaded module so a test starts from the app's initial state. */
export function resetModulesForTests(): void {
  loaded = new Map();
  pending = new Map();
  failed = new Set();
}
