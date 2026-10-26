import type { ButtonsView } from "../protocol";
import { OptionLabel, useSendOnce, useViewState, type WidgetProps } from "./common";

export function Buttons({ view, send }: WidgetProps<ButtonsView>) {
  const [locked, fire] = useSendOnce(view.viewId, send);
  const [picked, setPicked] = useViewState<string | null>(view.viewId, null);

  return (
    <div class="w-buttons">
      {view.prompt ? <div class="pq"><div class="prompt">{view.prompt}</div></div> : null}
      <div class="pbtns">
        {view.buttons.map((b) => (
          <button
            type="button"
            key={b.id}
            data-id={b.id}
            class={`tbtn${picked === b.id ? " picked" : ""}`}
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
