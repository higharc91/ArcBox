import type { TextEntryView } from "../protocol";
import { useSendOnce, useViewState, type WidgetProps } from "./common";

export function TextEntry({ view, send }: WidgetProps<TextEntryView>) {
  const [locked, fire] = useSendOnce(view.viewId, send);
  const [text, setText] = useViewState(view.viewId, "");
  const trimmed = text.trim();
  const valid = trimmed.length > 0 && trimmed.length <= view.maxLength;

  const submit = (e: Event) => {
    e.preventDefault();
    if (valid && !locked) fire({ text: trimmed });
  };

  return (
    <form class="w-text" onSubmit={submit}>
      <div class="pq"><div class="prompt">{view.prompt}</div></div>
      <div class="field">
        <input
          class="text-input"
          type="text"
          value={text}
          maxLength={view.maxLength}
          placeholder={view.placeholder ?? ""}
          autoComplete="off"
          autoCapitalize="sentences"
          disabled={locked}
          onInput={(e) => setText((e.currentTarget as HTMLInputElement).value)}
        />
        <div class="counter">{trimmed.length}/{view.maxLength}</div>
      </div>
      <div class="actions">
        <button type="submit" class="primary" disabled={!valid || locked}>Send</button>
      </div>
    </form>
  );
}
