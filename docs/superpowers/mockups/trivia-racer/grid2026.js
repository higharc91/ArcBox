// Loads the 2026 grid (F1DB data + our livery approximations) and indexes drivers by code.
export async function loadGrid() {
  const grid = await (await fetch('grid2026.json')).json();
  const byCode = {};
  for (const team of grid.teams) for (const driver of team.drivers) byCode[driver.code] = { team, driver };
  return { ...grid, byCode };
}

// A readable accent for tags, map dots and trails: white-bodied cars use their stripe colour.
export function accent(team) {
  const hex = team.livery.top.replace('#', '');
  const [r, g, b] = [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16));
  return 0.299 * r + 0.587 * g + 0.114 * b > 200 ? team.livery.stripe : team.livery.top;
}
