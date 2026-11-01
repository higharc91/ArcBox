import type { AbilityChoice, UnitView } from "./types";

/**
 * What the phone remembers from the views it has seen, for screens whose view doesn't carry it: the units on the
 * field (so the waiting screen can show who's acting), your abilities and items as of your last turn, and each hero's
 * class (worked out from their abilities, since a player's character has no sprite). Kept in sessionStorage, so a
 * reload mid-battle keeps it. Never sent anywhere; every screen works without it.
 */

const KEY = "arcbox:tactics:memory";

export interface Memory {
  /** Unit id → class id. */
  classes: Record<string, string>;
  /** The units as of the last turn view. */
  units: UnitView[];
  /** Your abilities as of your last turn, by unit id. */
  abilities: Record<string, AbilityChoice[]>;
}

let memory: Memory | null = null;

function load(): Memory {
  if (memory) return memory;
  try {
    const raw = sessionStorage.getItem(KEY);
    memory = raw ? (JSON.parse(raw) as Memory) : null;
  } catch {
    memory = null;
  }
  memory ??= { classes: {}, units: [], abilities: {} };
  return memory;
}

function save(): void {
  try {
    sessionStorage.setItem(KEY, JSON.stringify(memory));
  } catch {
    // Storage blocked or full: remember for this page only.
  }
}

export const remembered = (): Readonly<Memory> => load();

/** A turn view arrived: remember its units, and your abilities and class. */
export function rememberTurn(me: UnitView, units: UnitView[], abilities: AbilityChoice[], cls: string | undefined): void {
  const m = load();
  m.units = units;
  m.abilities[me.id] = abilities.map((a) => ({ ...a, targets: [], tiles: [] }));   // just what the list shows
  if (cls) m.classes[me.id] = cls;
  save();
}

/**
 * The class most of these abilities belong to, ignoring the common actions, racial traits and items. `classOf` looks an
 * ability's class up (the icon manifest has it); undefined when there's nothing to go on.
 */
export function classFrom(abilities: AbilityChoice[], classOf: (ability: string) => string | undefined): string | undefined {
  const votes = new Map<string, number>();
  for (const a of abilities) {
    const c = a.item ? undefined : classOf(a.ability);
    if (c && c !== "common" && c !== "race" && c !== "item") votes.set(c, (votes.get(c) ?? 0) + 1);
  }
  return [...votes].sort((x, y) => y[1] - x[1])[0]?.[0];
}
