import type { WaitView } from "../protocol";
import { useSkin } from "../skins/skin";

/** "Spun out! −2 · now P5" → ["Spun out!", "−2 · now P5"]. */
function splitTitle(message: string): [string, string] {
  const at = message.search(/[!.]\s/);
  return at < 0 ? [message, ""] : [message.slice(0, at + 1), message.slice(at + 2)];
}

export function Wait({ view }: { view: WaitView }) {
  const tone = useSkin()?.waitTone?.(view.message);
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
