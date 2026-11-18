import type { ComponentChildren } from "preact";
import { useContext, useEffect, useState } from "preact/hooks";
import { PlayerProfileContext } from "../module";
import { Sheet } from "./details";
import { CLASS_GUIDANCE, RULE_HELP, type RuleTopic } from "./guidance";

export const classGuideId = (id: string) => CLASS_GUIDANCE[id] ? `tx-class-guide-${id}` : undefined;

export function ClassGuidance({ id }: { id: string }) {
  const guide = CLASS_GUIDANCE[id];
  return guide ? <span class="tx-class-guide" id={classGuideId(id)}><span>{guide.style}</span><small>Learning curve: {guide.complexity}</small>
    {guide.beginner ? <strong>Good for new players</strong> : null}</span> : null;
}

export function RuleExplanation({ topic, children, class: cls, label }: {
  topic: RuleTopic; children?: ComponentChildren; class?: string; label?: string;
}) {
  const [open, setOpen] = useState(false);
  return <><button type="button" class={cls ?? "tx-rule-link"} aria-label={label ?? `Explain ${topic}`} onClick={() => setOpen(true)}>{children ?? topic}</button>
    {open ? <Sheet title={topic} eyebrow="Battle help" onClose={() => setOpen(false)}><p>{RULE_HELP[topic]}</p></Sheet> : null}</>;
}

const CARDS = [
  { title: "What you can spend", text: "Move is tiles left. Action is for most attacks or spells. Bonus is for a smaller ability. Reaction answers a trigger, often on someone else's turn. Tap any chip to learn more." },
  { title: "Move, then choose your moment", text: "Tap a reachable tile on the map, then Move here. Tap an ability, choose its highlighted target and tap Use. Browsing sends nothing. You can move and act in either order. End turn when you're finished." },
  { title: "Keep your friends standing", text: "At 0 HP you are Downed. Your countdown falls on your own turns and when hit. A friend's healing or nearby Help gets you up. At 0 you are Out for this battle and gain an injury. You are not permanently dead." },
];
const shownThisPage = new Set<string>();
export const primerKey = (profile: string) => `arcbox:tactics:primer:v1:${encodeURIComponent(profile)}`;
function seen(key: string): boolean {
  try { return localStorage.getItem(key) === "seen" || shownThisPage.has(key); } catch { return shownThisPage.has(key); }
}
function markSeen(key: string) {
  try { localStorage.setItem(key, "seen"); } catch { shownThisPage.add(key); /* Blocked/full storage: keep playing, remembering for this page. */ }
}

/** An inline introduction never blocks timed controls or sends a game command. */
export function BattleGuide({ unit, firstTurn = false }: { unit: string; firstTurn?: boolean }) {
  const profile = useContext(PlayerProfileContext);
  const key = primerKey(profile ?? `unit:${unit}`);
  const [intro, setIntro] = useState(false);
  const [step, setStep] = useState(0);
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const show = firstTurn && !seen(key);
    setIntro(show); setStep(0); setOpen(false);
    if (show) markSeen(key);
  }, [key, firstTurn]);
  const card = CARDS[step]!;
  return <div class="tx-battle-guide">
    <button type="button" class="tx-rule-link" onClick={() => setOpen(true)}>How to play</button>
    {intro ? <section class="tx-primer" aria-label="First battle quick start">
      <small>Quick start · {step + 1} of 3 · Your turn clock keeps running</small>
      <h3>{card.title}</h3><p>{card.text}</p>
      <div><button type="button" class="tx-btn ghost" onClick={() => setIntro(false)}>Hide tips</button>
        <button type="button" class="tx-btn" onClick={() => step < 2 ? setStep(step + 1) : setIntro(false)}>{step < 2 ? "Next tip" : "Ready to play"}</button></div>
    </section> : null}
    {open ? <Sheet title="How to play" eyebrow="Wardenflame" onClose={() => setOpen(false)}>
      <p class="tx-note">Your turn clock keeps running. Close this help whenever you're ready.</p>
      {CARDS.map(c => <section key={c.title}><h3>{c.title}</h3><p>{c.text}</p></section>)}
      <h3>Battle words</h3>{Object.entries(RULE_HELP).map(([term, text]) => <details key={term}><summary>{term}</summary><p>{text}</p></details>)}
    </Sheet> : null}
  </div>;
}
