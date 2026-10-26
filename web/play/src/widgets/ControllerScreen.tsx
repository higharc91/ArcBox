import { useEffect } from "preact/hooks";
import type { ControllerView } from "../protocol";
import { SkinContext, skinFor, type Skin } from "../skins/skin";
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
 * The in-game screen: status bar (name, header), the widget for the view's kind, and the countdown.
 * `game` is the lobby's selected game id; a known one (e.g. "redline") applies its skin.
 */
export function ControllerScreen({ view, send, me, game }: { view: ControllerView; send: Send; me: string; game?: string | null }) {
  const input = view.kind !== "wait";
  const skin = skinFor(game);
  useEffect(() => {
    void syncWakeLock(input);
  }, [input]);
  useEffect(() => () => void syncWakeLock(false), []);

  return (
    <SkinContext.Provider value={skin}>
      <div class={`screen controller k-${view.kind}`} data-game={game ?? undefined}>
        <header class="pstatus">
          <div class="me"><i />{me}</div>
          {view.header ? <Header header={view.header} skin={skin} /> : null}
        </header>
        <main class="widget">
          <Widget view={view} send={send} />
        </main>
        {view.deadlineUtc ? <Countdown deadlineUtc={view.deadlineUtc} /> : null}
      </div>
    </SkinContext.Provider>
  );
}
