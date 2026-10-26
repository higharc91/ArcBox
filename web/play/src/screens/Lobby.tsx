import type { LobbyState, PlayerId, PlayerScore } from "../protocol";

interface Props {
  lobby: LobbyState;
  me: PlayerId;
  error?: string;
  onSelectGame: (gameId: string) => void;
  onStart: () => void;
}

const range = (min: number, max: number) => (min === max ? `${min}` : `${min}–${max}`);

/** "Ann won with 12", or "Ann and Bo tied with 12" when the top scores are equal (Counter orders a tie by name). */
function winnerLine(scores: PlayerScore[]): string {
  const top = scores[0]!.score;
  const names = scores.filter((s) => s.score === top).map((s) => s.name);
  if (names.length === 1) return `${names[0]} won with ${top}`;
  return `${names.slice(0, -1).join(", ")} and ${names.at(-1)} tied with ${top}`;
}

export function Lobby({ lobby, me, error, onSelectGame, onStart }: Props) {
  const vip = lobby.players.find((p) => p.isVip);
  const iAmVip = vip?.id === me;
  const count = lobby.players.length;
  // Games that can never fit this many players are hidden; ones that need more are shown with their range.
  const games = lobby.games.filter((g) => count <= g.maxPlayers);
  const selected = lobby.games.find((g) => g.id === lobby.selectedGameId);

  return (
    <div class="screen lobby">
      <header class="lobby-head">
        <div>
          <small>ROOM</small>
          <div class="room-code">{lobby.code}</div>
        </div>
        <div class="count"><b>{count}</b><small>{count === 1 ? "PLAYER" : "PLAYERS"}</small></div>
      </header>

      {lobby.lastResults?.message || lobby.lastResults?.scores.length ? (
        <section class="last-results">
          <small>LAST GAME</small>
          {lobby.lastResults.message ? <p>{lobby.lastResults.message}</p> : null}
          {!lobby.lastResults.message && lobby.lastResults.scores[0] ? <p>{winnerLine(lobby.lastResults.scores)}</p> : null}
        </section>
      ) : null}

      <ul class="players">
        {lobby.players.map((p) => (
          <li key={p.id} class={["player", !p.connected && "offline", p.id === me && "me"].filter(Boolean).join(" ")}>
            <span class="pname">{p.name}</span>
            {p.isVip ? <span class="vip" title="VIP: picks the game">VIP</span> : null}
            {!p.connected ? <span class="away">away</span> : null}
          </li>
        ))}
      </ul>

      {iAmVip ? (
        <section class="picker">
          <small>PICK A GAME</small>
          <div class="games">
            {games.map((g) => {
              const on = g.id === lobby.selectedGameId;
              return (
                <button
                  type="button"
                  key={g.id}
                  class={`game-card${on ? " picked" : ""}`}
                  aria-pressed={on ? "true" : "false"}
                  onClick={() => onSelectGame(g.id)}
                >
                  <span class="gname">{g.name}</span>
                  {g.description ? <span class="gdesc">{g.description}</span> : null}
                  <span class="gplayers">{range(g.minPlayers, g.maxPlayers)} players</span>
                </button>
              );
            })}
          </div>
          <button type="button" class="primary big start" disabled={!lobby.canStart} onClick={onStart}>
            Everybody's in
          </button>
          {selected && !lobby.canStart && count < selected.minPlayers ? (
            <p class="hint">{selected.name} needs at least {selected.minPlayers} players.</p>
          ) : null}
        </section>
      ) : (
        <section class="waiting">
          {selected ? <p class="next-game">Next up: <b>{selected.name}</b></p> : null}
          <p>{vip ? `Waiting for ${vip.name} to start` : "Waiting for the host to start"}</p>
        </section>
      )}

      {error ? <p class="form-error" role="alert">{error}</p> : null}
    </div>
  );
}
