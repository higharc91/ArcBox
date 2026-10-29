import type { GameModule } from "../module";
import { redlineSkin } from "./skin";

/** REDLINE's phone module: its skin (no custom screens; REDLINE uses the generic widgets). */
export const module: GameModule = { id: "redline", skin: redlineSkin };
