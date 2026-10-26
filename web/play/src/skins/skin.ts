import { createContext } from "preact";
import { useContext } from "preact/hooks";
import type { Option } from "../protocol";
import { redlineSkin } from "./redline";

export interface Badge {
  text: string;
  /** The option label without the badge text. */
  label: string;
  color: string;
  ink: string;
}

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
}

const skins: Record<string, Skin> = { redline: redlineSkin };

export function skinFor(gameId?: string | null): Skin | undefined {
  return gameId ? skins[gameId] : undefined;
}

export const SkinContext = createContext<Skin | undefined>(undefined);

export const useSkin = () => useContext(SkinContext);
