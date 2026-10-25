// Shared 22-car race state for the full-grid mockups: players claim seats, AI fills the rest.
export const PLAYERS = { NOR: 'JO', VER: 'SAM', LEC: 'ALEX', ANT: 'RIA', ALO: 'MO', PER: 'KAI' };
export const FIELD = [
  ['NOR', 6, 'CRUISE'], ['RUS', 6, 'PUSH'], ['PIA', 5, 'PUSH'], ['LEC', 4, 'REDLINE'], ['VER', 4, 'PUSH'], ['HAM', 4, 'REDLINE'],
  ['ANT', 3, 'REDLINE'], ['HAD', 3, 'CRUISE'], ['LAW', 3, 'PUSH'], ['LIN', 3, 'PUSH'], ['ALO', 2, 'CRUISE'], ['GAS', 2, 'PUSH'],
  ['COL', 2, 'REDLINE'], ['HUL', 2, 'CRUISE'], ['SAI', 2, 'PUSH'], ['PER', 1, 'REDLINE'], ['BEA', 1, 'PUSH'], ['BOR', 1, 'CRUISE'],
  ['ALB', 1, 'CRUISE'], ['OCO', 0, 'REDLINE'], ['STR', 0, 'CRUISE'], ['BOT', 0, 'PUSH'],
];
export function field(grid, accent) {
  return FIELD.map(([code, dist, thr]) => {
    const c = grid.byCode[code];
    return { name: code, code, dist, thr, ...c, color: accent(c.team), player: PLAYERS[code] ?? null };
  });
}
