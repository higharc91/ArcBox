import { useEffect, useState } from "preact/hooks";
import { FeedbackContext, type Feedback } from "./feedback/feedback";
import { Closed } from "./screens/Closed";
import { Join } from "./screens/Join";
import { Lobby } from "./screens/Lobby";
import { Results } from "./screens/Results";
import type { Session, SessionState } from "./session/session";
import { loadSeat, type KeyValueStore } from "./session/storage";
import { ControllerScreen } from "./widgets/ControllerScreen";

/**
 * First load: `?room=CODE` resumes a stored seat for that room, else shows Join with the code pre-filled.
 * `?slot=n` and `?devName=P1` come from the dev page: the slot namespaces storage, and devName joins automatically.
 */
export async function boot(session: Session, store: KeyValueStore, search: string): Promise<void> {
  const params = new URLSearchParams(search);
  const room = params.get("room")?.trim().toUpperCase();
  const slot = params.get("slot") ?? undefined;
  const devName = params.get("devName")?.trim();
  if (!room) return;
  if (loadSeat(store, room, slot)) {
    if (await session.resume(room)) return;
  } else {
    await session.resume(room); // shows Join with the code
  }
  if (devName && session.state.screen === "join" && !session.state.error) await session.join(room, devName);
}

/** `feedback` (haptics, sounds, the speaker toggle) is optional so tests and the dev board can leave it out. */
export function App({ session, feedback = null }: { session: Session; feedback?: Feedback | null }) {
  const [state, setState] = useState<SessionState>(session.state);
  useEffect(() => {
    setState(session.state);
    return session.subscribe(setState);
  }, [session]);

  return (
    <FeedbackContext.Provider value={feedback}>
      <Screen state={state} session={session} />
    </FeedbackContext.Provider>
  );
}

function Screen({ state, session }: { state: SessionState; session: Session }) {
  switch (state.screen) {
    case "join":
      return (
        <Join
          key={`${state.code ?? ""}|${state.name ?? ""}`}
          code={state.code}
          name={state.name}
          error={state.error}
          busy={state.busy}
          onJoin={(code, name) => void session.join(code, name)}
        />
      );
    case "connecting":
      return (
        <div class="screen connecting">
          <div class="spinner" aria-hidden="true" />
          <p>Connecting…</p>
        </div>
      );
    case "lobby":
      return (
        <Lobby
          lobby={state.lobby}
          me={state.me}
          error={state.error}
          onSelectGame={(id) => void session.selectGame(id)}
          onStart={() => void session.startGame()}
        />
      );
    case "game":
      return (
        <ControllerScreen
          view={state.view}
          send={(payload) => void session.send(state.view.viewId, payload)}
          me={state.lobby.players.find((p) => p.id === state.me)?.name ?? ""}
          game={state.lobby.selectedGameId}
        />
      );
    case "results":
      return <Results results={state.results} me={state.me} onBack={() => session.dismissResults()} />;
    case "closed":
      return <Closed reason={state.reason} onLeave={() => session.leaveClosed()} />;
  }
}
