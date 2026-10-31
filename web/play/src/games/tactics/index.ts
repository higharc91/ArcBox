import type { GameModule } from "../module";
import "./fonts";
import { TacticsPortal } from "./portal";
import { PickClass, PickEncounter, ReactPrompt, Results, Status, Turn } from "./screens";
import "./tactics.css";

/** Wardenflame's (ArcBox Tactics') phone module: its CustomView screens (adapter spec §8), named as the game sends them, and its portal. */
export const module: GameModule = {
  id: "tactics",
  screens: { pickClass: PickClass, pickEncounter: PickEncounter, turn: Turn, react: ReactPrompt, status: Status, results: Results },
  portal: TacticsPortal,
};
