import type { GameModule } from "../module";
import "./fonts";
import { lobbyTheme } from "./lobby";
import { TacticsPortal } from "./portal";
import { PickClass, PickEncounter, ReactPrompt, Results, Status, Turn } from "./screens";
import { Check, Dialogue, Vote } from "./story";
import { Camp } from "./camp";
import { Shop } from "./shop";
import { Walk } from "./walk";
import "./tactics.css";

/** Wardenflame's (ArcBox Tactics') phone module: its CustomView screens (adapter spec §8), named as the game sends them, and its portal. */
export const module: GameModule = {
  id: "tactics",
  screens: {
    pickClass: PickClass, pickEncounter: PickEncounter, turn: Turn, react: ReactPrompt, status: Status, results: Results, walk: Walk,
    dialogue: Dialogue, vote: Vote, check: Check, shop: Shop, "camp-job": Camp, "camp-target": Camp, "camp-roll": Camp,
  },
  portal: TacticsPortal,
  lobbyTheme,
};
