import type { ButtonsView } from "../protocol";
import { useSkin } from "../skins/skin";
import { OptionLabel, useSendOnce, useViewState, type WidgetProps } from "./common";

export function Buttons({ view, send }: WidgetProps<ButtonsView>) {
  const [locked, fire] = useSendOnce(view.viewId, send);
  const [picked, setPicked] = useViewState<string | null>(view.viewId, null);
  const skin = useSkin();

  return (
    <div class="w-buttons">
      {view.prompt ? <div class="pq"><div class="prompt">{view.prompt}</div></div> : null}
      <div class="pbtns">
        {view.buttons.map((b) => (
          <button
            type="button"
            key={b.id}
            data-id={b.id}
            class={["tbtn", skin?.buttonClass?.(b), picked === b.id && "picked"].filter(Boolean).join(" ")}
            disabled={locked}
            onClick={() => {
              if (fire({ pressed: b.id })) setPicked(b.id);
            }}
          >
            <OptionLabel option={b} />
          </button>
        ))}
      </div>
    </div>
  );
}
