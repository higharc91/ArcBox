import type { ComponentChildren, JSX } from "preact";
import { useEffect, useRef, useState } from "preact/hooks";
import { ConditionChip, Portrait } from "./art";
import type { Economy, UnitView } from "./types";

/** Small building blocks shared by the Wardenflame phone screens. */

const titleCase = (s: string) => s.replaceAll("_", " ").replace(/\b\w/g, (c) => c.toUpperCase());
export const className = (cls: string | null | undefined) => (cls ? titleCase(cls) : null);

/** A gold small-caps section heading. */
export function Section({ children }: { children: ComponentChildren }) {
  return <h3 class="tx-sect">{children}</h3>;
}

/** An HP bar: green for heroes, red for enemies. `--fill` is its width. */
export function HpBar({ unit, enemy = false }: { unit: Pick<UnitView, "hp" | "maxHp">; enemy?: boolean }) {
  const pct = unit.maxHp > 0 ? Math.max(0, Math.min(100, Math.round((100 * unit.hp) / unit.maxHp))) : 0;
  return (
    <div class={`tx-bar${enemy ? " enemy" : ""}`} role="meter" aria-valuemin={0} aria-valuemax={unit.maxHp} aria-valuenow={unit.hp}
      aria-label={`HP ${unit.hp} of ${unit.maxHp}`}>
      <i style={{ "--fill": `${pct}%` } as JSX.CSSProperties} />
    </div>
  );
}

/** HP in numbers, with temporary HP and the downed countdown. */
export function HpText({ unit }: { unit: UnitView }) {
  return (
    <span class="tx-hp-text">
      {unit.hp}/{unit.maxHp}{unit.tempHp > 0 ? <em> +{unit.tempHp}</em> : null}
      {unit.life === "Downed" ? <b class="tx-downed">Downed · {unit.countdown}</b> : null}
    </span>
  );
}

/** Your character's card: portrait, name, class and HP. `title`/`subtitle` replace the name lines (the aiming header). */
export function MeCard({ me, cls, title, subtitle, icon }: {
  me: UnitView; cls?: string | null; title?: string; subtitle?: string; icon?: ComponentChildren;
}) {
  return (
    <div class="tx-me">
      {icon ?? <Portrait unit={me} cls={cls} size={60} tone="you" />}
      <div class="tx-me-name">
        <b>{title ?? me.name}</b>
        <span>{subtitle ?? className(cls) ?? "Hero"}</span>
      </div>
      {title ? null : (
        <div class="tx-me-hp">
          <HpBar unit={me} />
          <HpText unit={me} />
        </div>
      )}
    </div>
  );
}

/** The conditions on a unit, as chips with their status icons. */
export function Conditions({ ids }: { ids: string[] }) {
  if (ids.length === 0) return null;
  return <div class="tx-chips">{ids.map((c) => <ConditionChip key={c} id={c} />)}</div>;
}

/**
 * The turn clock. The game's deadline reaches the app shell, not the screen's data, so this mirrors the shell's own
 * countdown (`.ptimer`, which the tactics CSS hides on this screen). Null while there is none.
 */
function useShellClock(anchor: { current: HTMLElement | null }): { text: string; urgent: boolean } | null {
  const [clock, setClock] = useState<{ text: string; urgent: boolean } | null>(null);
  useEffect(() => {
    const read = () => {
      const timer = anchor.current?.closest(".screen")?.querySelector(".ptimer");
      const text = timer?.querySelector("b")?.textContent ?? null;
      setClock((old) => {
        if (!text) return null;
        const urgent = timer!.classList.contains("urgent");
        return old && old.text === text && old.urgent === urgent ? old : { text, urgent };
      });
    };
    read();
    const id = setInterval(read, 250);
    return () => clearInterval(id);
  }, []);
  return clock;
}

/** The YOUR TURN banner with the turn clock. */
export function TurnBanner() {
  const ref = useRef<HTMLDivElement>(null);
  const clock = useShellClock(ref);
  return (
    <div class="tx-banner" ref={ref}>
      <b>Your turn</b>
      {clock ? <span class={`tx-clock${clock.urgent ? " urgent" : ""}`} role="timer">{clock.text}</span> : null}
    </div>
  );
}

/** What's left of your action economy this turn: Move (tiles), Action, Bonus, Reaction. Spent ones are struck out. */
export function EconomyChips({ e }: { e: Economy }) {
  const action = e.action || e.extraActions > 0;
  return (
    <div class="tx-econ" aria-label="What you have left this turn">
      <span class={e.moveLeft > 0 ? "on" : "done"} title="Tiles of movement left">Move {e.moveLeft}</span>
      <span class={action ? "on" : "done"} title="Your action">Action</span>
      <span class={e.bonus ? "on" : "done"} title="Your bonus action">Bonus</span>
      <span class={e.reaction ? "on" : "done"} title="Your reaction">Reaction</span>
    </div>
  );
}

/** A line icon from the mockups' set (icons.js), drawn with the current colour. */
export function LineIcon({ name, rotate }: { name: keyof typeof ICONS; rotate?: number }) {
  return (
    <svg class="tx-line-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
      stroke-linejoin="round" aria-hidden="true" style={rotate ? { transform: `rotate(${rotate}deg)` } : undefined}
      // eslint-disable-next-line react/no-danger
      dangerouslySetInnerHTML={{ __html: ICONS[name] }} />
  );
}

const ICONS = {
  boot: '<path d="M6 3v10l-3 5h11l6-2v-3l-6-1-2-9z"/>',
  bag: '<path d="M5 8h14l-1 12H6z"/><path d="M9 8a3 3 0 0 1 6 0"/>',
  sheet: '<path d="M6 3h12v18H6z"/><path d="M9 8h6M9 12h6M9 16h3"/>',
  hourglass: '<path d="M6 3h12M6 21h12"/><path d="M7 3c0 6 10 5 10 9s-10 3-10 9M17 3c0 6-10 5-10 9s10 3 10 9"/>',
  up: '<path d="M12 19V5M5 12l7-7 7 7"/>',
  check: '<path d="M4 12l5 5L20 6"/>',
  swap: '<path d="M4 8h14l-3-3M20 16H6l3 3"/>',
  back: '<path d="M15 5l-7 7 7 7"/>',
  next: '<path d="M9 5l7 7-7 7"/>',
  sword: '<path d="M14.5 3.5H20.5V9.5L9 21 6 18 17.5 6.5"/><path d="M4 15l5 5M3 21l2.5-2.5"/>',
  shield: '<path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z"/><path d="M12 7v10"/>',
} as const;
