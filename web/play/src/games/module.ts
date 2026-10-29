import { createContext, type ComponentType } from "preact";
import { useContext } from "preact/hooks";
import type { CampaignResponse, Option, ShakeView, WaitView } from "../protocol";

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

/** What a CustomView screen is given: its `data` (screen-specific JSON) and a way to answer. */
export interface CustomScreenProps {
  data: unknown;
  /** Posts a CustomInput `{ action, data }` for the current view; `data` is left out when undefined. Callable as often as needed. */
  send(action: string, data?: unknown): void;
}

/** What a game's between-sessions portal screen is given (portal API, spec §3.4). */
export interface PortalProps {
  campaign: CampaignResponse;
  readOnly: boolean;
  send(type: string, data: unknown): Promise<void>;
}

/**
 * A game's phone code: the module `src/games/<id>/index.ts` exports as `module`. Loaded lazily (src/games/registry.ts),
 * so a game's code is downloaded only when a room selects it.
 */
export interface GameModule {
  id: string;
  skin?: Skin;
  /** The screens a CustomView can name, by CustomView.Screen. */
  screens?: Record<string, ComponentType<CustomScreenProps>>;
  portal?: ComponentType<PortalProps>;
}

export const SkinContext = createContext<Skin | undefined>(undefined);

export const useSkin = () => useContext(SkinContext);
