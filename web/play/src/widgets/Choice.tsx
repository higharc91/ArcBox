import type { ChoiceView } from "../protocol";
import { OptionLabel, Prompt, useSendOnce, useViewState, type WidgetProps } from "./common";

/** Single choice sends on tap; multiple choice toggles up to maxSelections and sends on "Lock in". */
export function Choice({ view, send }: WidgetProps<ChoiceView>) {
  const [locked, fire] = useSendOnce(view.viewId, send);
  const [selected, setSelected] = useViewState<string[]>(view.viewId, []);
  const max = view.allowMultiple ? Math.max(1, view.maxSelections ?? view.options.length) : 1;

  const tap = (id: string) => {
    if (locked) return;
    if (!view.allowMultiple) {
      if (fire({ selected: [id] })) setSelected([id]);
      return;
    }
    if (selected.includes(id)) setSelected(selected.filter((s) => s !== id));
    else if (selected.length < max) setSelected([...selected, id]);
  };

  const lockIn = () => {
    if (selected.length === 0) return;
    // Send in option order, whatever order they were tapped in.
    fire({ selected: view.options.map((o) => o.id).filter((id) => selected.includes(id)) });
  };

  return (
    <div class="w-choice">
      <Prompt text={view.prompt} note={view.allowMultiple ? `Pick up to ${max}` : undefined} />
      <div class="options">
        {view.options.map((o) => {
          const on = selected.includes(o.id);
          return (
            <button
              type="button"
              key={o.id}
              data-id={o.id}
              class={`opt${on ? " picked" : ""}`}
              aria-pressed={on ? "true" : "false"}
              disabled={locked || (view.allowMultiple && !on && selected.length >= max)}
              onClick={() => tap(o.id)}
            >
              <OptionLabel option={o} />
            </button>
          );
        })}
      </div>
      {view.allowMultiple ? (
        <div class="actions">
          <button type="button" class="primary" disabled={locked || selected.length === 0} onClick={lockIn}>
            Lock in{selected.length ? ` (${selected.length})` : ""}
          </button>
        </div>
      ) : null}
    </div>
  );
}
