// Phone-visible forecasts only: no battle state or hidden enemy plans.
const risk = (move) => move.risk ?? 0;

function safestMoves(moves) {
  const least = Math.min(...moves.map(risk));
  return moves.filter((move) => risk(move) === least);
}

function escapeBurning(data) {
  if (!data.surfaces?.includes('burning')) return null;
  // A zero-damage path includes the destination's next-turn exposure. Do not trade
  // the known fire for another damaging hazard just to make the bot move.
  return data.choices.moves.filter((move) => risk(move) === 0)
    .reduce((best, move) => !best || move.cost < best.cost ? move : best, null);
}

module.exports = { safestMoves, escapeBurning };
