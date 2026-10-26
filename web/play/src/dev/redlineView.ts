// Mirrors games/Redline/Redline.Logic/Views/HostView.cs (RedlineHostView) as serialized by ArcJson:
// camelCase properties, enums as PascalCase strings, PlayerId as a plain string.

export type RacePhase = "DriverSelect" | "Grid" | "Throttle" | "Question" | "Reveal" | "Chequered";
export type Throttle = "Cruise" | "Push" | "Redline";
export type RoundOutcome = "Correct" | "Wrong" | "NoAnswer" | "SpunOut";
export type DriverStatus = "Unclaimed" | "Seated" | "Ready" | "Choosing" | "Locked" | "Limited" | "Answering" | "Answered";
export type Difficulty = "Easy" | "Medium" | "Hard";

export interface QuestionCard {
  category: string;
  difficulty: Difficulty;
  /** Null during Throttle. */
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
  /** Null during Grid and Throttle. */
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
}

export function isRedlineHostView(v: unknown): v is RedlineHostView {
  const r = v as Partial<RedlineHostView> | null;
  return !!r && typeof r === "object" && typeof r.phase === "string" && Array.isArray(r.drivers) && typeof r.finishLine === "number";
}
