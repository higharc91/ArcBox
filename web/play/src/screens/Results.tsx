import type { GameResults, PlayerId } from "../protocol";

/** Scores in the order the game ranked them (REDLINE sends race order, which breaks ties by more than score). */
export function Results({ results, me, onBack }: { results: GameResults; me: PlayerId; onBack: () => void }) {
  return (
    <div class="screen results">
      <header class="results-head">
        <small>RESULTS</small>
        {results.message ? <h1>{results.message}</h1> : <h1>Game over</h1>}
      </header>
      {results.scores.length ? (
        <ol class="scores">
          {results.scores.map((s, i) => (
            <li key={s.playerId} class={["score-row", i === 0 && "first", s.playerId === me && "me"].filter(Boolean).join(" ")}>
              <span class="spos">{i + 1}</span>
              <span class="sname">{s.name}</span>
              <span class="sscore">{s.score}</span>
            </li>
          ))}
        </ol>
      ) : null}
      <div class="actions">
        <button type="button" class="primary big" onClick={onBack}>Back to lobby</button>
      </div>
    </div>
  );
}
