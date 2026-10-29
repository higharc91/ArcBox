import { useEffect, useState } from "preact/hooks";
import { FeedbackContext, type Feedback } from "./feedback/feedback";
import { Closed } from "./screens/Closed";
import { Join } from "./screens/Join";
import { Lobby } from "./screens/Lobby";
import { Pick } from "./screens/Pick";
import { Results } from "./screens/Results";
import type { Session, SessionState } from "./session/session";
import { loadSeat, type KeyValueStore } from "./session/storage";
import { ControllerScreen } from "./widgets/ControllerScreen";

/**
 * First load: `?room=CODE` rejoins a stored seat for that room without asking anything (`Rejoin`). With no seat, or
 * when the server no longer knows it, the phone asks the room what it can take (`Peek`) and shows the picker.
 * `?slot=n` and `?devName=P1` come from the dev page: the slot namespaces storage, and devName joins under that name.
 */
export async function boot(session: Session, store: KeyValueStore, search: string): Promise<void> {
  const params = new URLSearchParams(search);
  const room = params.get("room")?.trim().toUpperCase();
  const slot = params.get("slot") ?? undefined;
  const devName = params.get("devName")?.trim();
  if (!room) return;
  const hasSeat = loadSeat(store, room, slot) !== null;
  // A failed rejoin has already opened the picker (once); only a phone with no seat still needs to.
  if (hasSeat && (await session.resume(room))) return;
  if (devName && (session.state.screen === "join" || session.state.screen === "pick")) return session.joinNew(devName, room);
  if (!hasSeat) await session.open(room);
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
          onJoin={(code, name) => void session.joinRoom(code, name)}
        />
      );
    case "connecting":
      return (
        <div class="screen connecting">
          <div class="spinner" aria-hidden="true" />
          <p>Connecting…</p>
        </div>
      );
    case "pick":
      return (
        <Pick
          peek={state.peek}
          profiles={session.profiles()}
          showAll={state.showAll}
          error={state.error}
          busy={state.busy}
          onTake={(seat) => void session.takeSeat(seat)}
          onNew={(name) => void session.joinNew(name)}
          onShowAll={() => session.showEveryone()}
        />
      );
    case "lobby":
      return (
        <Lobby
          lobby={state.lobby}
          me={state.me}
          error={state.error}
          onSelectGame={(id) => void session.selectGame(id)}
          onStart={() => void session.startGame()}
          onSelectCampaign={(id) => void session.selectCampaign(id)}
          onCreateCampaign={(name) => void session.createCampaign(name)}
          onClaim={(member, name) => void session.claimMember(member, name)}
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
