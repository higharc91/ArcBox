import { useRef, useState } from "preact/hooks";
import type { Option } from "../protocol";
import { useSkin } from "../skins/skin";

export type Send = (payload: object) => void;

export interface WidgetProps<V> {
  view: V;
  send: Send;
}

/**
 * The send-once rule: a widget sends at most once per viewId and locks immediately (the ref blocks a double tap
 * inside one frame); a new viewId unlocks it. The server drops input for a stale viewId anyway.
 */
export function useSendOnce(viewId: string, send: Send): [locked: boolean, fire: (payload: object) => boolean] {
  const sentRef = useRef<string | null>(null);
  const [sentFor, setSentFor] = useState<string | null>(null);
  const fire = (payload: object) => {
    if (sentRef.current === viewId) return false;
    sentRef.current = viewId;
    setSentFor(viewId);
    send(payload);
    return true;
  };
  return [sentFor === viewId, fire];
}

/** State that belongs to one viewId and resets when the view changes. */
export function useViewState<T>(viewId: string, initial: T): [T, (v: T) => void] {
  const [state, setState] = useState<{ for: string; value: T }>({ for: viewId, value: initial });
  const value = state.for === viewId ? state.value : initial;
  return [value, (v: T) => setState({ for: viewId, value: v })];
}

/** Label on the first line, the hint as a dim second line; a skin may add a coloured badge in front. */
export function OptionLabel({ option }: { option: Option }) {
  const badge = useSkin()?.optionBadge?.(option);
  return (
    <>
      {badge ? <span class="badge" style={{ background: badge.color, color: badge.ink }}>{badge.text}</span> : null}
      <span class="opt-text">
        <span class="opt-label">{badge ? badge.label : option.label}</span>
        {option.hint ? <span class="opt-hint">{option.hint}</span> : null}
      </span>
    </>
  );
}
