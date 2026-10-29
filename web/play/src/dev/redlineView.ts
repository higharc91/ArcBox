// Mirrors games/Redline/Redline.Logic/Views/HostView.cs (RedlineHostView) as serialized by ArcJson:
// camelCase properties, enums as PascalCase strings, PlayerId as a plain string.

/**
 * Reading (audio spec §2): the question is read out before the answer clock starts; skipped when the server turns it off.
 * Anthem and Champagne (podium spec §2): the podium ceremony after the Chequered flag; skipped with no players or Celebrate off.
 */
export type RacePhase = "DriverSelect" | "Grid" | "Throttle" | "Reading" | "Question" | "Reveal" | "Chequered" | "Anthem" | "Champagne";
export type Throttle = "Cruise" | "Push" | "Redline";
export type RoundOutcome = "Correct" | "Wrong" | "NoAnswer" | "SpunOut";
export type DriverStatus = "Unclaimed" | "Seated" | "Ready" | "Choosing" | "Locked" | "Limited" | "Answering" | "Answered";
export type Difficulty = "Easy" | "Medium" | "Hard";

export interface QuestionCard {
  category: string;
  difficulty: Difficulty;
  /** Null during Throttle; set from Reading on. */
  text: string | null;
  answers: string[] | null;
}

export interface DriverRow {
  code: string;
  number: number;
  teamId: string;
  player: string | null;
  playerName: string | null;
  position: number;
  positionChange: number;
  distance: number;
  previousDistance: number;
  status: DriverStatus;
  /** Null during Grid and Throttle; public from Reading on. */
  throttle: Throttle | null;
  /** Null until Reveal. */
  answerIndex: number | null;
  /** Reveal only. */
  outcome: RoundOutcome | null;
  pitLimiter: boolean;
  connected: boolean;
}

export interface Award {
  title: string;
  winner: string;
  name: string;
  detail: string;
}

/** The throttle payouts from the server's RedlineRules (losses are positive numbers). */
export interface Payouts {
  cruiseGain: number;
  pushGain: number;
  pushLoss: number;
  redlineGain: number;
  redlineLoss: number;
}

/** A spray from a podium bottle: seq is 1, 2, 3… across the whole podium; strength 0.15..1. */
export interface Spray {
  seq: number;
  strength: number;
}

export interface PodiumStep {
  /** 1..3. */
  step: number;
  player: string;
  name: string;
  code: string;
  teamId: string;
  racePosition: number;
  /** 0..1 in steps of 0.05. */
  charge: number;
  sprays: Spray[];
}

export interface Podium {
  /** Display name, e.g. "Great Britain". */
  anthemCountry: string;
  /** The anthem's key ("gb"), or null when only the REDLINE theme plays. */
  anthemId: string | null;
  steps: PodiumStep[];
}

export interface RedlineHostView {
  phase: RacePhase;
  round: number;
  maxRounds: number;
  finishLine: number;
  circuitId: string;
  question: QuestionCard | null;
  correctIndex: number | null;
  drivers: DriverRow[];
  deadlineUtc: string | null;
  finish: { awards: Award[] } | null;
  attribution: string;
  /** Optional so a view from an older server still parses. */
  payouts?: Payouts | null;
  /** Anthem and Champagne only. Optional so a view from an older server still parses. */
  podium?: Podium | null;
}

export function isRedlineHostView(v: unknown): v is RedlineHostView {
  const r = v as Partial<RedlineHostView> | null;
  return !!r && typeof r === "object" && typeof r.phase === "string" && Array.isArray(r.drivers) && typeof r.finishLine === "number";
}
