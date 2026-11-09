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
  risk?: number;
  warn?: string;
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
  spread?: Tile[];
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
  /** How it's aimed: at a unit, a tile, a direction (a line or cone from you), or just used ("self"). */
  aim?: "unit" | "tile" | "direction" | "self";
  /**
   * Set (above 1) for a weapon attack that makes several attacks: each may go at any of `targets` ("Attack 2 of 3"), sent as
   * `targets`, one unit id per attack. Without a list every attack goes at the one target. Previews are for all at one.
   */
  attacks?: number;
}

export interface TurnChoices {
  unit: string;
  economy: Economy;
  moves: MoveChoice[];
  abilities: AbilityChoice[];
  canDelay: boolean;
}

/** Falling back (#111): the button, the vote under way, or that it failed this round. Absent before round 3 or when the fight forbids it. */
export interface FallBackData {
  canPropose?: boolean;
  failed?: boolean;
  vote?: {
    proposer: string;
    yes: number;
    no: number;
    /** Players here (they vote), and the yes votes a strict majority takes. */
    present: number;
    needed: number;
    /** This player's own answer, once given. */
    mine: "yes" | "no" | null;
    /** When it times out, ISO UTC. */
    until?: string | null;
  };
}

/** A player's reaction preferences (#113): what the game answers for them without asking. */
export interface ReactionPrefsData {
  opportunity: "ask" | "always" | "never";
  defence: "ask" | "miss" | "never";
}

export interface ControlNoticeData {
  seq: number;
  reason: "turnTimeout" | "reactionTimeout" | "disconnected" | "reconnected" | "controlUnavailable";
  text: string;
  policy?: string | null;
  recap?: string | null;
}

export interface BattleTerrain {
  w: number;
  h: number;
  heights: number[];
  flags: number[];
  materials: string[];
  tileMaterial: number[];
  zones: { name: string; tiles: Tile[]; hazard: boolean; detail: string }[];
}

export interface TurnData {
  terrain?: BattleTerrain;
  controlNotice?: ControlNoticeData;
  surfaces?: string[];
  fallBack?: FallBackData;
  reactions?: ReactionPrefsData;
  /** Who is acting: the hero, or one of their summons (then `summoned` is set and the header names it). */
  me: UnitView;
  summoned?: boolean;
  /** The map id, for its terrain from the content pack. */
  map?: string;
  units: UnitView[];
  choices: TurnChoices;
}

export interface ReactOption {
  outcome?: string;
  cost?: string;
  id: string;
  label: string;
  target: string | null;
  /** What the reaction does. */
  tooltip?: string | null;
}

export interface ReactData {
  timeout?: string;
  incomingDamage?: number | null;
  controlNotice?: ControlNoticeData;
  surfaces?: string[];
  fallBack?: FallBackData;
  reactions?: ReactionPrefsData;
  unit: string;
  trigger: string;
  cause: {
    kind: string; actor: string | null; target: string | null; ability: string | null;
    roll?: { total: number; vs: number } | null;
  } | null;
  /** The acting unit's name, the target's ("you" when it's you) and the ability's, in words. */
  actor?: string | null;
  targetName?: string | null;
  ability?: string | null;
  options: ReactOption[];
}

export interface StatusData {
  cls?: string | null;
  round?: number;
  objective?: string | null;
  party?: UnitView[];
  acting?: UnitView | null;
  abilities?: AbilityChoice[];
  recent?: string[];
  controlNotice?: ControlNoticeData;
  surfaces?: string[];
  fallBack?: FallBackData;
  reactions?: ReactionPrefsData;
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
  /** AI companions who'd join to bring the party up to the minimum. */
  companions?: number;
  /** Intended encounter level compared with the campaign's current party level. */
  guidance?: string | null;
  unavailableReason?: string | null;
}

export interface WalkMember {
  name: string;
  /** The class's name, as shown. */
  cls: string;
  here: boolean;
  leads: boolean;
  /** Their colour, "#rrggbb"; absent from an older server. */
  color?: string;
}

/** The walk screen. It holds nothing that changes as the party walks: a new view would drop what the pad is sending. */
export interface WalkData {
  canGive?: boolean;
  give?: Omit<import("./give").GiveData, "gold">;
  danger?: string;
  ambush?: string;
  journal?: import("./quests").JournalData;
  pinned?: { id: string; name: string; objective: string };
  canOpenLog?: boolean;
  questNotice?: import("./quests").QuestNoticeData;
  restReady?: boolean;
  gold?: number;
  leader: boolean;
  leaderName: string | null;
  region: string | null;
  objective: string;
  /** `color`: the player's colour, "#rrggbb" (their pick, else their join order's); absent from an older server. */
  me: { name: string; cls: string; level: number; color?: string };
  party: WalkMember[];
  vip: boolean;
  /** The campaign difficulty ("Story", "Normal" or "Hard"); absent from an older server. */
  difficulty?: string;
  canBattle: boolean;
  /** The campaign portal's path; null outside a campaign. */
  portal?: string | null;
  /** The leader's Interact button ("Talk", "Look around"…), while a beat's spot is in reach; null or absent otherwise. */
  interact?: string | null;
  /**
   * The leader's things to interact with in reach (chests, doors, levers…), nearest first, each with its button's words: `interact`
   * sent with `{ target: id }` uses one. Empty for everyone else; absent in a region with no things, and from an older server. The Interact
   * button uses the first, unless a beat is in reach (then `interact` is the beat's word); with more than one choice, a list beside it has them all.
   */
  targets?: { id: string; label: string }[];
  /** With `targets`: true when the leader's button is a beat's (`interact` is its word, sent with no target); false when it's the first thing's. */
  beat?: boolean;
  /**
   * With `targets`: true on the leader's screen, where a Search button sends `search` (interactables design §7.4): the party's
   * Investigation check for what's hidden nearby. False for everyone else; absent in a region with no things.
   */
  search?: boolean;
  choices?: CharacterChoicesData | null;
  /**
   * The leader's count of steps a wall the TV draws has refused this session (never an unfound secret door): it goes up once per press
   * into a wall, and the pad buzzes and flashes the held sector when it does. Absent while it's 0, and for everyone else.
   */
  bumps?: number;
}

/**
 * A line of a scene. Everything is null between lines (the gap between scenes, or while a roll is revealed). The narrator
 * has no speaker and no portrait. `canSkip` is the leader's, and a skip names `line`, so a late tap can't skip the next.
 */
export interface DialogueData {
  title: string | null;
  line: string | null;
  speaker: string | null;
  /** A portrait preset of the pack's (`portraits/manifest.json`), drawn in `mood`. */
  portrait: string | null;
  mood: string | null;
  text: string | null;
  canSkip: boolean;
  /** This phone is the VIP's: it can end the session from the scene. */
  vip: boolean;
  /** The campaign difficulty ("Story", "Normal" or "Hard"); absent from an older server. */
  difficulty?: string;
}

export interface StoryOption {
  can?: boolean;
  needs?: string;
  id: string;
  label: string;
  /** The skill check picking it starts, if any. */
  skill: string | null;
  dc: number | null;
}

/**
 * A question: the party's vote (`party`), or a choice the leader makes for everyone. On a tie only the tied options come,
 * and `decider` breaks it. No tally: that's the TV's. `chosen` is the party's pick of a leader's choice, for those who
 * didn't make it.
 */
export interface VoteData {
  party: boolean;
  canChoose: boolean;
  options: StoryOption[];
  mine: string | null;
  chosen: string | null;
  tie: boolean;
  decider: string | null;
  /** The players away when it was asked, by name. */
  away: string[];
  /** This phone is the VIP's: it can end the session from the scene. */
  vip: boolean;
  /** The campaign difficulty ("Story", "Normal" or "Hard"); absent from an older server. */
  difficulty?: string;
  /** What a loot vote asks ("Who takes the Potion of Healing?"), the owner's decision 1; absent for a story's vote. */
  question?: string;
  /** Which of a run of loot votes this is, so each item's question is a new one. */
  vote?: number;
}

export interface CheckCard {
  /** The member's id, which `pickRoller` sends. */
  id: string;
  name: string;
  player: string;
  /** The class's name, as shown. */
  cls: string;
  bonus: number;
  /** Where the bonus comes from: "Proficient · CHA 14". */
  note: string;
  here: boolean;
}

export interface CheckRoll {
  dice?: number[] | null;
  group?: { member: string; name: string; die: number; bonus: number; success: boolean }[] | null;
  member: string;
  die: number;
  bonus: number;
  total: number;
  /** Null for a search, whose DC isn't shown. */
  dc: number | null;
  success: boolean;
}

/** A skill check: the party picks who rolls, by a vote on the same rules, then the roll. */
export interface CheckData {
  group?: boolean;
  help?: { can: boolean; member: string | null; name: string | null };
  skill: string;
  /** Null for a search (interactables design §7.4): each thing it looks for has its own DC, and it mustn't say whether there are any. */
  dc: number | null;
  /** The option it's for. */
  title: string;
  cards: CheckCard[];
  mine: string | null;
  canChoose: boolean;
  tie: boolean;
  decider: string | null;
  /** On a tie, the members it's between. */
  tied: string[];
  roll: CheckRoll | null;
  /** This phone is the VIP's: it can end the session from the scene. */
  vip: boolean;
  /** The campaign difficulty ("Story", "Normal" or "Hard"); absent from an older server. */
  difficulty?: string;
}

export interface ChoiceOptionData {
  id: string;
  name: string;
  tooltip?: string;
}

export interface ChoiceSlotData {
  level: number;
  choose?: number;
  from: ChoiceOptionData[];
  picked: string[];
}

export interface CharacterChoicesData {
  name: string;
  class: string;
  className: string;
  subclass?: string | null;
  subclassName?: string | null;
  level: number;
  xp: number;
  nextLevelXp?: number | null;
  prevLevelXp?: number;
  scores: Record<string, number>;
  needsSubclass: boolean;
  subclasses?: { id: string; name: string; role?: string }[] | null;
  increasesLeft: number;
  choices: ChoiceSlotData[];
  waiting: number;
}

export interface ResultsData {
  controlNotice?: ControlNoticeData;
  outcome: "Won" | "Lost" | "Fled" | string | null;
  encounter: string | null;
  lines: {
    name: string;
    xp: number;
    injuries: string[];
    totalXp?: number;
    nextLevelXp?: number | null;
    prevLevelXp?: number;
    level?: number;
  }[];
  /** After a loss, the one line the server words for it ("Wounds tended — no XP…"); null otherwise. */
  note?: string | null;
  vip: boolean;
  /** The campaign difficulty ("Story", "Normal" or "Hard"); absent from an older server. */
  difficulty?: string;
  level: number;
  /** The campaign portal's path, where characters are made and grown; null outside a campaign. */
  portal?: string | null;
  leveledUp?: boolean;
  gold?: number | null;
  loot?: { id: string; name: string; count?: number }[] | null;
  choices?: CharacterChoicesData | null;
}
