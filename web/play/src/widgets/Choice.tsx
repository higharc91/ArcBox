import { useEffect } from "preact/hooks";
import type { ChoiceInput, ChoiceView } from "../protocol";
import { OptionLabel, Prompt, useSendOnce, useViewState, type WidgetProps } from "./common";

/**
 * The time-to-answer stopwatch for one view. Starts on the second animation frame after the options mount (the first
 * frame that has them painted); if the page is hidden then, on the first frame after it becomes visible. The start is
 * kept per viewId, so a re-render of the same view never restarts it. Returns a reader for "ms since the start", which
 * is 0 for a tap that lands before the start is set (the server clamps elapsedMs anyway).
 */
function useStopwatch(viewId: string): () => number {
  const [startedAt, setStartedAt] = useViewState<number | null>(viewId, null);

  useEffect(() => {
    const start = () => setStartedAt(performance.now());
    let first = 0;
    let second = 0;
    let only = 0;
    const onVisible = () => {
      if (document.visibilityState !== "visible") return;
      document.removeEventListener("visibilitychange", onVisible);
      only = requestAnimationFrame(start);
    };

    if (document.visibilityState === "hidden") {
      document.addEventListener("visibilitychange", onVisible);
    } else {
      first = requestAnimationFrame(() => {
        second = requestAnimationFrame(start);
      });
    }
    return () => {
      cancelAnimationFrame(first);
      cancelAnimationFrame(second);
      cancelAnimationFrame(only);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [viewId]); // setStartedAt is recreated each render but always targets this viewId

  return () => (startedAt === null ? 0 : Math.max(0, performance.now() - startedAt));
}

/** Single choice sends on tap; multiple choice toggles up to maxSelections and sends on "Lock in". */
export function Choice({ view, send }: WidgetProps<ChoiceView>) {
  const [locked, fire] = useSendOnce(view.viewId, send);
  const [selected, setSelected] = useViewState<string[]>(view.viewId, []);
  const elapsed = useStopwatch(view.viewId);
  const max = view.allowMultiple ? Math.max(1, view.maxSelections ?? view.options.length) : 1;

  const answer = (ids: string[]): ChoiceInput => ({ selected: ids, elapsedMs: elapsed() });

  const tap = (id: string) => {
    if (locked) return;
    if (!view.allowMultiple) {
      if (fire(answer([id]))) setSelected([id]);
      return;
    }
    if (selected.includes(id)) setSelected(selected.filter((s) => s !== id));
    else if (selected.length < max) setSelected([...selected, id]);
  };

  const lockIn = () => {
    if (selected.length === 0) return;
    // Send in option order, whatever order they were tapped in.
    fire(answer(view.options.map((o) => o.id).filter((id) => selected.includes(id))));
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
