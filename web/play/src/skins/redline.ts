import type { Option } from "../protocol";
import type { Badge, HeaderParts, Skin } from "./skin";

export interface Team {
  id: string;
  name: string;
  /** A readable accent for badges and tower bars. */
  accent: string;
}

/**
 * The 11 teams of the 2026 grid. Copied from games/Redline/Redline.Logic/Data/grid2026.json (`teams[].id`, `.name`,
 * `.livery`): accent = `livery.top`, or `livery.stripe` for a white-bodied car (luma of top > 200), the same rule as
 * `accent()` in docs/superpowers/mockups/trivia-racer/grid2026.js. Livery colours are approximations, not official values.
 */
export const TEAMS: readonly Team[] = [
  { id: "mclaren", name: "McLaren", accent: "#ff8000" },
  { id: "ferrari", name: "Ferrari", accent: "#dc0000" },
  { id: "red-bull", name: "Red Bull", accent: "#1b2550" },
  { id: "mercedes", name: "Mercedes", accent: "#c3c7cb" },
  { id: "aston-martin", name: "Aston Martin", accent: "#00594f" },
  { id: "alpine", name: "Alpine", accent: "#0a5fc2" },
  { id: "williams", name: "Williams", accent: "#0b2a5b" },
  { id: "racing-bulls", name: "Racing Bulls", accent: "#e3102f" },
  { id: "haas", name: "Haas", accent: "#e1061f" },
  { id: "audi", name: "Audi", accent: "#a7abb0" },
  { id: "cadillac", name: "Cadillac", accent: "#b9bec4" },
];

const byName = new Map(TEAMS.map((t) => [t.name.toLowerCase(), t]));
const byId = new Map(TEAMS.map((t) => [t.id, t]));

/** Team accent from a Driver Select option hint, "{Team} · {Chassis}" (e.g. "McLaren · MCL40"). */
export function teamColor(hint?: string | null): string | undefined {
  if (!hint) return undefined;
  return byName.get(hint.split("·")[0]!.trim().toLowerCase())?.accent;
}

/** Team accent from a HostView DriverRow.teamId (e.g. "red-bull"). */
export function teamColorById(teamId?: string | null): string | undefined {
  return teamId ? byId.get(teamId)?.accent : undefined;
}

/** Dark or light text, whichever reads on the given background. */
export function inkFor(hex: string): string {
  const n = parseInt(hex.replace("#", ""), 16);
  const luma = 0.299 * (n >> 16) + 0.587 * ((n >> 8) & 255) + 0.114 * (n & 255);
  return luma > 150 ? "#111" : "#fff";
}

/** A driver option ("#81 Oscar Piastri", hint "McLaren · MCL40") becomes a team-coloured number badge plus the name. */
export function driverBadge(option: Option): Badge | undefined {
  const color = teamColor(option.hint);
  const match = /^#(\d+)\s+(.+)$/.exec(option.label);
  if (!color || !match) return undefined;
  return { text: match[1]!, label: match[2]!, color, ink: inkFor(color) };
}

export const THROTTLES = ["cruise", "push", "redline"] as const;

/** "P5 · ROUND 3/15", optionally prefixed by the throttle ("PUSH · P5 · ROUND 3/15"). */
export function splitHeader(header: string): HeaderParts | undefined {
  const parts = header.split("·").map((p) => p.trim());
  const chip = parts.length === 3 && THROTTLES.includes(parts[0]!.toLowerCase() as (typeof THROTTLES)[number]) ? parts.shift() : undefined;
  if (parts.length !== 2 || !/^P\d+$/.test(parts[0]!)) return undefined;
  return { chip, big: parts[0]!, small: parts[1]! };
}

export const redlineSkin: Skin = {
  id: "redline",
  optionBadge: driverBadge,
  buttonClass: (o) => ((THROTTLES as readonly string[]).includes(o.id) ? `throttle throttle-${o.id}` : undefined),
  header: splitHeader,
  waitTone: (message) => {
    if (message.startsWith("Correct!")) return "good";
    if (message.startsWith("Spun out!") || message.startsWith("Wrong answer") || message.startsWith("No answer")) return "bad";
    return undefined;
  },
};
