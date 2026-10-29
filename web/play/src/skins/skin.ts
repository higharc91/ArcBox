import { createContext } from "preact";
import { useContext } from "preact/hooks";
import type { Option, ShakeView, WaitView } from "../protocol";
import { redlineSkin } from "./redline";

export interface Badge {
  text: string;
  /** The option label without the badge text. */
  label: string;
  color: string;
  ink: string;
}

/** A round result a phone can cue with a sound and a vibration (src/feedback). */
export type ResultKind = "correct" | "wrong" | "spin";

export interface HeaderParts {
  chip?: string;
  big: string;
  small: string;
}

/**
 * Optional per-game decoration of the generic controller kit. Every hook may return undefined, in which case the
 * widget falls back to the generic look, so a skin can never hide content.
 */
export interface Skin {
  id: string;
  optionBadge?(option: Option): Badge | undefined;
  buttonClass?(option: Option): string | undefined;
  header?(header: string): HeaderParts | undefined;
  /** Splits a prompt into a small meta line and the prompt proper. */
  prompt?(prompt: string): { meta: string; text: string } | undefined;
  waitTone?(message: string): "good" | "bad" | undefined;
  /** A wait view whose message is a question whose answers are not open yet: the note to show under it. */
  waitQuestion?(view: WaitView): string | undefined;
  /** A wait view that reports this player's round result: which cue the phone plays. Null for any other view. */
  classifyResult?(view: WaitView): ResultKind | null;
  /** A shake view that cannot fire yet: why, as a short sentence put in front of its prompt ("Hold on — …"). */
  shakeHold?(view: ShakeView): string | undefined;
}

const skins: Record<string, Skin> = { redline: redlineSkin };

export function skinFor(gameId?: string | null): Skin | undefined {
  return gameId ? skins[gameId] : undefined;
}

export const SkinContext = createContext<Skin | undefined>(undefined);

export const useSkin = () => useContext(SkinContext);
