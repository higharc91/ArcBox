import type { ButtonsView } from "../protocol";
import { useSkin } from "../skins/skin";
import { OptionLabel, Prompt, useSendOnce, useViewState, type WidgetProps } from "./common";

/**
 * Several buttons are a decision: the first tap sends and locks until the viewId changes.
 * A lone button is a mash button: the SDK lets a game keep a view unchanged while it accepts repeated input
 * (Counter does exactly that), so every tap sends.
 */
export function Buttons({ view, send }: WidgetProps<ButtonsView>) {
  const [locked, fire] = useSendOnce(view.viewId, send);
  const [picked, setPicked] = useViewState<string | null>(view.viewId, null);
  const skin = useSkin();
  const mash = view.buttons.length === 1;

  return (
    <div class="w-buttons">
      <Prompt text={view.prompt} />
      <div class={`pbtns${mash ? " single" : ""}`}>
        {view.buttons.map((b) => (
          <button
            type="button"
            key={b.id}
            data-id={b.id}
            class={["tbtn", skin?.buttonClass?.(b), picked === b.id && "picked"].filter(Boolean).join(" ")}
            disabled={locked && !mash}
            onClick={() => {
              if (mash) send({ pressed: b.id });
              else if (fire({ pressed: b.id })) setPicked(b.id);
            }}
          >
            <OptionLabel option={b} />
          </button>
        ))}
      </div>
    </div>
  );
}
