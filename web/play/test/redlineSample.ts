import type { DriverRow, RacePhase, RedlineHostView } from "../src/dev/redlineView";

// The 2026 grid in team order (games/Redline/Redline.Logic/Data/grid2026.json).
const GRID: [code: string, number: number, teamId: string][] = [
  ["NOR", 1, "mclaren"], ["PIA", 81, "mclaren"], ["LEC", 16, "ferrari"], ["HAM", 44, "ferrari"],
  ["VER", 3, "red-bull"], ["HAD", 6, "red-bull"], ["RUS", 63, "mercedes"], ["ANT", 12, "mercedes"],
  ["ALO", 14, "aston-martin"], ["STR", 18, "aston-martin"], ["GAS", 10, "alpine"], ["COL", 43, "alpine"],
  ["ALB", 23, "williams"], ["SAI", 55, "williams"], ["LAW", 30, "racing-bulls"], ["LIN", 41, "racing-bulls"],
  ["OCO", 31, "haas"], ["BEA", 87, "haas"], ["HUL", 27, "audi"], ["BOR", 5, "audi"], ["PER", 11, "cadillac"], ["BOT", 77, "cadillac"],
];

const PLAYERS: Record<string, [id: string, name: string]> = {
  NOR: ["p1", "JO"], ALO: ["p2", "MO"], VER: ["p3", "SAM"], ANT: ["p4", "RIA"], PIA: ["p5", "ALEX"], HAM: ["p6", "KAI"],
};
const THROTTLES = ["Cruise", "Push", "Redline"] as const;

/** A deterministic 22-car REDLINE HostView for tests and screenshots, shaped exactly like the server's JSON. */
export function sampleRedlineView(phase: RacePhase, deadlineUtc: string | null = null): RedlineHostView {
  const round = phase === "DriverSelect" || phase === "Grid" ? 0 : phase === "Chequered" ? 9 : 7;
  const correctIndex = 1;
  const drivers: DriverRow[] = GRID.map(([code, number, teamId], i) => {
    const human = PLAYERS[code];
    const racing = phase !== "DriverSelect" && phase !== "Grid";
    const distance = racing ? Math.max(0, 27 - Math.floor(i * 1.1)) : 0;
    const throttle = THROTTLES[(i * 7) % 3]!;
    const answerIndex = (i * 5) % 4;
    const outcome = answerIndex !== correctIndex ? (throttle === "Redline" ? "SpunOut" : i % 5 === 0 ? "NoAnswer" : "Wrong") : "Correct";
    const gain = outcome === "Correct" ? (throttle === "Cruise" ? 1 : throttle === "Push" ? 3 : 4) : throttle === "Cruise" ? 0 : throttle === "Push" ? -1 : -2;
    return {
      code, number, teamId,
      player: human ? human[0] : null,
      playerName: human ? human[1] : null,
      position: i + 1,
      positionChange: phase === "Reveal" ? [0, 2, -1, 0, 1, -2][i % 6]! : 0,
      distance,
      previousDistance: phase === "Reveal" ? Math.max(0, distance - gain) : distance,
      status: phase === "DriverSelect" ? (human ? "Seated" : "Unclaimed")
        : phase === "Throttle" ? (i === 4 ? "Limited" : i % 4 === 1 ? "Choosing" : "Locked")
        : phase === "Question" ? (i % 3 === 0 ? "Answering" : "Answered")
        : "Ready",
      throttle: phase === "Reading" || phase === "Question" || phase === "Reveal" ? throttle : null,
      answerIndex: phase === "Reveal" ? answerIndex : null,
      outcome: phase === "Reveal" ? outcome : null,
      pitLimiter: i === 4,
      connected: code !== "HAM",
    };
  });
  return {
    phase, round, maxRounds: 15, finishLine: 28, circuitId: "cota",
    question: phase === "Grid" || phase === "DriverSelect" ? null : {
      category: "Science & Nature",
      difficulty: "Hard",
      text: phase === "Throttle" ? null : "Which planet has the most known moons?",
      answers: phase === "Throttle" ? null : ["Jupiter", "Saturn", "Uranus", "Neptune"],
    },
    correctIndex: phase === "Reveal" ? correctIndex : null,
    drivers,
    deadlineUtc,
    finish: phase === "Chequered" ? {
      awards: [
        { title: "Boldest driver", winner: "p2", name: "MO (ALO)", detail: "6 Redlines" },
        { title: "Sharpest mind", winner: "p1", name: "JO (NOR)", detail: "8 correct" },
        { title: "Biggest crash", winner: "p3", name: "SAM (VER)", detail: "1 spin-out" },
      ],
    } : null,
    attribution: "Questions: Open Trivia DB (CC BY-SA 4.0)",
    payouts: { cruiseGain: 1, pushGain: 3, pushLoss: 1, redlineGain: 4, redlineLoss: 2 },
  };
}
