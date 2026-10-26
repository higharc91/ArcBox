import { useEffect } from "preact/hooks";
import type { ControllerView } from "../protocol";
import { Buttons } from "./Buttons";
import { Choice } from "./Choice";
import type { Send } from "./common";
import { Countdown } from "./Countdown";
import { Draw } from "./Draw";
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
  }
}

/** The in-game screen: status bar (name, header), the widget for the view's kind, and the countdown. */
export function ControllerScreen({ view, send, me }: { view: ControllerView; send: Send; me: string }) {
  const input = view.kind !== "wait";
  useEffect(() => {
    void syncWakeLock(input);
  }, [input]);
  useEffect(() => () => void syncWakeLock(false), []);

  return (
    <div class={`screen controller k-${view.kind}`}>
      <header class="pstatus">
        <div class="me"><i />{me}</div>
        {view.header ? <div class="pos">{view.header}</div> : null}
      </header>
      <main class="widget">
        <Widget view={view} send={send} />
      </main>
      {view.deadlineUtc ? <Countdown deadlineUtc={view.deadlineUtc} /> : null}
    </div>
  );
}
