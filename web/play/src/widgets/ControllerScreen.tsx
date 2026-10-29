import { useEffect, useLayoutEffect } from "preact/hooks";
import { useFeedback } from "../feedback/feedback";
import { MuteToggle } from "../feedback/MuteToggle";
import type { ControllerView } from "../protocol";
import { SkinContext, type Skin } from "../games/module";
import { useGameModule } from "../games/registry";
import { Buttons } from "./Buttons";
import { Choice } from "./Choice";
import type { Send } from "./common";
import { Countdown } from "./Countdown";
import { Custom } from "./Custom";
import { Draw } from "./Draw";
import { Shake } from "./Shake";
import { TextEntry } from "./TextEntry";
import { Wait } from "./Wait";
import { syncWakeLock } from "./wakeLock";

function Widget({ view, send }: { view: ControllerView; send: Send }) {
  switch (view.kind) {
    case "wait": return <Wait view={view} />;
    case "buttons": return <Buttons view={view} send={send} />;
    case "choice": return <Choice view={view} send={send} />;
    case "textEntry": return <TextEntry view={view} send={send} />;
    case "draw": return <Draw view={view} send={send} />;
    case "shake": return <Shake view={view} send={send} />;
    case "custom": return <Custom view={view} send={send} />;
  }
}

function Header({ header, skin }: { header: string; skin?: Skin }) {
  const parts = skin?.header?.(header);
  if (!parts) return <div class="pos">{header}</div>;
  return (
    <div class="pos">
      {parts.chip ? <span class={`chip ${parts.chip}`}>{parts.chip}</span> : null}
      <b>{parts.big}</b>
      <small>{parts.small}</small>
    </div>
  );
}

/**
 * The in-game screen: status bar (name, header, speaker toggle), the widget for the view's kind, and the countdown.
 * `game` is the lobby's selected game id; once that game's module has loaded (e.g. "redline"), its skin applies.
 * With a Feedback in context, sends tap, results cue, and the last seconds of an input view tick.
 */
export function ControllerScreen({ view, send, me, game }: { view: ControllerView; send: Send; me: string; game?: string | null }) {
  const input = view.kind !== "wait";
  const skin = useGameModule(game).module?.skin; // generic look until the game's module has loaded
  const feedback = useFeedback();
  useEffect(() => {
    void syncWakeLock(input);
  }, [input]);
  useEffect(() => () => void syncWakeLock(false), []);
  // A layout effect, so the view is known before the Countdown's first tick (a child's passive effect).
  useLayoutEffect(() => feedback?.onView(view, game), [feedback, view.viewId]);
  // The shake widget streams charge updates and cues its own fizz and pop: no tap click on each of them.
  const sendWithFeedback: Send = feedback && view.kind !== "shake"
    ? (payload) => {
        feedback.onSend();
        send(payload);
      }
    : send;

  return (
    <SkinContext.Provider value={skin}>
      <div class={`screen controller k-${view.kind}`} data-game={game ?? undefined}>
        <header class="pstatus">
          <div class="me"><i /><span class="me-name">{me}</span></div>
          {view.header ? <Header header={view.header} skin={skin} /> : null}
          <MuteToggle />
        </header>
        <main class="widget">
          <Widget view={view} send={sendWithFeedback} />
        </main>
        {view.deadlineUtc ? <Countdown deadlineUtc={view.deadlineUtc} onSecond={feedback ? (s) => feedback.onCountdown(s) : undefined} /> : null}
      </div>
    </SkinContext.Provider>
  );
}
