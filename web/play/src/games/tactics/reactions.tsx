import { useState } from "preact/hooks";
import type { ReactionPrefsData } from "./types";

type Send = (action: string, data?: unknown) => void;

const OPPORTUNITY = [
  { id: "ask", label: "Ask" },
  { id: "always", label: "Always" },
  { id: "never", label: "Never" },
] as const;

const DEFENCE = [
  { id: "ask", label: "Ask" },
  { id: "miss", label: "Only when it turns a hit into a miss" },
  { id: "never", label: "Never" },
] as const;

/**
 * The "Reactions" sheet (#113), on the waiting screen and on a turn: whether this player is asked about an opportunity attack, and about
 * Shield and the other defensive reactions, or has the game answer for them with no prompt and no pause. The choices are saved with the
 * character; a reaction a choice answers happens at once, as if they had tapped it. Nothing is shown when the server sends no settings.
 * Each tap changes one setting.
 */
export function ReactionsSheet({ reactions, send }: { reactions?: ReactionPrefsData; send: Send }) {
  const [open, setOpen] = useState(false);
  if (!reactions) return null;
  return (
    <div class="tx-reactions">
      <button type="button" class="tx-btn ghost" aria-expanded={open} onClick={() => setOpen(!open)}>Reactions</button>
      {open ? (
        <section class="tx-card tx-reactions-sheet" aria-label="Reaction settings">
          <Setting title="Opportunity attacks" noun="opportunity attacks" options={OPPORTUNITY} value={reactions.opportunity}
            onPick={(id) => send("reactionPrefs", { opportunity: id })} />
          <Setting title="Shield and other defensive reactions" noun="defensive reactions" options={DEFENCE} value={reactions.defence}
            onPick={(id) => send("reactionPrefs", { defence: id })} />
          <span class="tx-tip">Ask shows the prompt. The other choices answer for you at once, so the table never waits on a reaction.</span>
        </section>
      ) : null}
    </div>
  );
}

function Setting({ title, noun, options, value, onPick }: {
  title: string; noun: string; options: readonly { id: string; label: string }[]; value: string; onPick: (id: string) => void;
}) {
  return (
    <div class="tx-reactions-setting" role="group" aria-label={title}>
      <b>{title}</b>
      <div class="tx-reactions-options">
        {options.map((o) => (
          <button type="button" key={o.id} class={`tx-btn${value === o.id ? " gold" : ""}`} aria-pressed={value === o.id}
            aria-label={`${o.label} · ${noun}`} onClick={() => onPick(o.id)}>{o.label}</button>
        ))}
      </div>
    </div>
  );
}
