import { useEffect, useState } from "preact/hooks";
import type { GameModule, LobbyText, LobbyTheme } from "./module";

type Loader = () => Promise<{ module: GameModule }>;

/**
 * Game id → how to load its module. A dynamic import() per game, so Vite emits one chunk per game and a phone only
 * downloads the game its room selects. Add a game with one line here.
 */
const loaders: Record<string, Loader> = {
  redline: () => import("./redline"),
  tactics: () => import("./tactics"),
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

/**
 * The selected game's lobby theme, re-rendering when its module arrives; undefined (the default look) for no game, a
 * game without one, and while the module is still loading. `load` starts the module's download too (the "Who are
 * you?" screen comes before the lobby, which is where the session starts it).
 */
export function useLobbyTheme(gameId?: string | null, load = false): LobbyTheme | undefined {
  const [, setTick] = useState(0);
  useEffect(() => {
    const off = subscribeModules(() => setTick((n) => n + 1));
    if (load) void loadModule(gameId);
    setTick((n) => n + 1);   // it may have arrived between the render and this effect
    return off;
  }, [gameId, load]);
  return moduleFor(gameId)?.lobbyTheme;
}

/** The shell's own lobby wording: what every game without a theme shows. */
export const DEFAULT_LOBBY_TEXT: LobbyText = {
  pickCampaign: "PICK A CAMPAIGN",
  newCampaign: "New campaign",
  campaignName: "Campaign name",
  create: "Create",
  campaignMeta: (c) => `${c.members} ${c.members === 1 ? "member" : "members"}`,
  campaignLabel: "Campaign",
};

/** The wording to use: the theme's, falling back to the default per label. */
export function lobbyText(theme?: LobbyTheme): LobbyText {
  return theme?.text ? { ...DEFAULT_LOBBY_TEXT, ...theme.text } : DEFAULT_LOBBY_TEXT;
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
