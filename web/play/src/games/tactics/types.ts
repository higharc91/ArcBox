/**
 * The data of the tactics game's phone screens, as games/Tactics/Tactics.Game sends it (adapter spec §8). Tiles are
 * `[x, y]`. Pinned by the fixtures in test/fixtures/tactics, which the C# side regenerates.
 */

export type Tile = [number, number];

export interface UnitView {
  id: string;
  name: string;
  side: "Party" | "Enemy" | "Neutral" | string;
  sprite: string | null;
  at: Tile;
  hp: number;
  maxHp: number;
  tempHp: number;
  life: "Up" | "Downed" | "Out" | "Dead" | string;
  countdown: number;
  conditions: string[];
  player: string | null;
}

export interface Economy {
  moveLeft: number;
  action: boolean;
  bonus: boolean;
  reaction: boolean;
  extraActions: number;
}

export interface MoveChoice {
  to: Tile;
  cost: number;
  path: Tile[];
}

export interface Target {
  unit: string | null;
  tile: Tile | null;
}

export interface UnitPreview {
  unit: string;
  damage: number;
  heal: number;
}

export interface TargetChoice {
  target: Target;
  aim: Tile;
  affected: UnitPreview[];
}

export interface Cost {
  action?: number;
  bonus?: number;
  reaction?: number;
  move?: number;
  resource?: Record<string, number>;
  uses?: number;
  concentration?: boolean;
}

export interface AbilityChoice {
  ability: string;
  item: string | null;
  name: string;
  tooltip: string;
  cost: Cost;
  /** Targets that affect someone, with their previews. */
  targets: TargetChoice[];
  /** The other legal aim tiles, bare (a tile-aimed area spell can have hundreds): pickable on the map. */
  tiles: Tile[];
}

export interface TurnChoices {
  unit: string;
  economy: Economy;
  moves: MoveChoice[];
  abilities: AbilityChoice[];
  canDelay: boolean;
}

export interface TurnData {
  me: UnitView;
  /** The map id, for its terrain from the content pack. */
  map?: string;
  units: UnitView[];
  choices: TurnChoices;
}

export interface ReactOption {
  id: string;
  label: string;
  target: string | null;
}

export interface ReactData {
  unit: string;
  trigger: string;
  cause: { kind: string; actor: string | null; target: string | null; ability: string | null } | null;
  options: ReactOption[];
}

export interface StatusData {
  me: UnitView;
  active: string | null;
  upIn: number | null;
  aiPlaying: boolean;
}

export interface ClassOption {
  id: string;
  name: string;
  role: string;
}

export interface EncounterOption {
  id: string;
  name: string;
  level: number | null;
  min: number;
  max: number;
  fits: boolean;
}

export interface ResultsData {
  outcome: "Won" | "Lost" | "Fled" | string | null;
  encounter: string | null;
  lines: { name: string; xp: number; injuries: string[] }[];
  vip: boolean;
  level: number;
}
