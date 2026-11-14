import type { GameModule } from "../module";
import { Allocate, Briefing, Card, CrewPick, Debrief, Echo, Orient, Over, Roll, Sheet, Trade, Turn, Vote, Watch } from "./screens";
import "./meridian.css";

/** Derelict: ISV Meridian's phone module: its CustomView screens, named as Meridian.Game sends them (spec §3.4). */
export const module: GameModule = {
  id: "meridian",
  screens: {
    crewPick: CrewPick, turn: Turn, orient: Orient, card: Card, roll: Roll, trade: Trade, sheet: Sheet, watch: Watch, over: Over,
    briefing: Briefing, allocate: Allocate, vote: Vote, echo: Echo, debrief: Debrief,
  },
};
