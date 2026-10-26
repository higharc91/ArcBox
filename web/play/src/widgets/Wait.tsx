import type { WaitView } from "../protocol";
import { useSkin } from "../skins/skin";
import { Prompt } from "./common";

/** "Spun out! −2 · now P5" → ["Spun out!", "−2 · now P5"]. */
function splitTitle(message: string): [string, string] {
  const at = message.search(/[!.]\s/);
  return at < 0 ? [message, ""] : [message.slice(0, at + 1), message.slice(at + 2)];
}

export function Wait({ view }: { view: WaitView }) {
  const skin = useSkin();
  const note = skin?.waitQuestion?.(view);
  if (note) {
    // A question shown before its answers open: the question block, then the note.
    return (
      <div class="w-wait w-listen">
        <Prompt text={view.message} />
        <p class="listen-note">{note}</p>
      </div>
    );
  }
  const tone = skin?.waitTone?.(view.message);
  if (tone) {
    const [title, rest] = splitTitle(view.message);
    return (
      <div class="w-wait">
        <div class={`wait-banner ${tone}`}>
          <div class="banner-title">{title}</div>
          {rest ? <div class="banner-rest">{rest}</div> : null}
        </div>
      </div>
    );
  }
  return (
    <div class="w-wait">
      <p class="wait-message">{view.message}</p>
    </div>
  );
}
