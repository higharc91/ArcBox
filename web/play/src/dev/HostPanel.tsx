import { useEffect, useState } from "preact/hooks";
import type { HostClient, HostState } from "./host";
import { RedlineBoard } from "./RedlineBoard";
import { isRedlineHostView } from "./redlineView";

/** The host side of the dev page: room code, lobby controls and the latest HostView. */
export function HostPanel({ host }: { host: HostClient }) {
  const [state, setState] = useState<HostState>(host.state);
  useEffect(() => {
    setState(host.state);
    return host.subscribe(setState);
  }, [host]);

  const { lobby, hostView, results } = state;
  const inGame = !!lobby?.inGame;

  return (
    <section class="host">
      <div class="host-bar">
        <div class="host-code">
          <small>ROOM</small>
          <b>{state.code ?? "…"}</b>
        </div>
        <ul class="host-players">
          {lobby?.players.map((p) => (
            <li key={p.id} class={p.connected ? "" : "offline"}>
              {p.name}
              {p.isVip ? <span class="vip">VIP</span> : null}
              {!inGame ? (
                <button type="button" class="kick" aria-label={`Kick ${p.name}`} onClick={() => void host.kick(p.id)}>×</button>
              ) : null}
            </li>
          ))}
          {lobby && lobby.players.length === 0 ? <li class="empty">No players yet</li> : null}
        </ul>
        <div class="host-actions">
          {!inGame && lobby ? (
            <>
              {lobby.games.map((g) => (
                <button
                  type="button"
                  key={g.id}
                  class={g.id === lobby.selectedGameId ? "picked" : ""}
                  aria-pressed={g.id === lobby.selectedGameId ? "true" : "false"}
                  onClick={() => void host.selectGame(g.id)}
                >
                  {g.name}
                </button>
              ))}
              <button type="button" class="primary" disabled={!lobby.canStart} onClick={() => void host.startGame()}>
                Start
              </button>
            </>
          ) : null}
          {inGame ? (
            <button type="button" onClick={() => void host.returnToLobby()}>Return to lobby</button>
          ) : null}
        </div>
      </div>

      {state.error ? <p class="form-error" role="alert">{state.error}</p> : null}
      {state.closed ? <p class="form-error" role="alert">Room closed: {state.closed}</p> : null}

      {results ? (
        <div class="host-results">
          <small>LAST GAME</small>
          <b>{results.message ?? "Game over"}</b>
          <ol>{results.scores.map((s) => <li key={s.playerId}>{s.name} <span>{s.score}</span></li>)}</ol>
        </div>
      ) : null}

      {hostView ? (
        hostView.gameId === "redline" && isRedlineHostView(hostView.view) ? (
          <RedlineBoard view={hostView.view} />
        ) : (
          <pre class="host-json">{JSON.stringify(hostView.view, null, 2)}</pre>
        )
      ) : !inGame ? (
        <p class="host-idle">Pick a game and press Start. Phones join below.</p>
      ) : null}
    </section>
  );
}
