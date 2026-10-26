import { useEffect, useState } from "preact/hooks";
import { useFeedback } from "./feedback";

/**
 * The speaker toggle for the status bar: mutes sounds and haptics on this device. Labelled by what a tap will do.
 * Renders nothing without a Feedback in context (unit renders, the dev board).
 */
export function MuteToggle() {
  const feedback = useFeedback();
  const [muted, setMuted] = useState(feedback?.muted ?? false);
  useEffect(() => {
    if (!feedback) return;
    setMuted(feedback.muted);
    return feedback.subscribe(setMuted);
  }, [feedback]);
  if (!feedback) return null;

  const label = muted ? "Unmute sounds" : "Mute sounds";
  return (
    <button type="button" class={`mute-toggle${muted ? " muted" : ""}`} aria-label={label} title={label} onClick={() => feedback.toggleMuted()}>
      <svg viewBox="0 0 24 24" width="24" height="24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z" fill="currentColor" />
        {muted ? (
          <path d="M16 9.5l5 5M21 9.5l-5 5" />
        ) : (
          <>
            <path d="M15.5 9a4 4 0 0 1 0 6" />
            <path d="M18 6.5a7.5 7.5 0 0 1 0 11" />
          </>
        )}
      </svg>
    </button>
  );
}
