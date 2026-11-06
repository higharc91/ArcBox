import { useState } from "preact/hooks";
import type { CharacterChoicesData, ChoiceSlotData } from "./types";

const STATS = ["str", "dex", "con", "int", "wis", "cha"] as const;
type StatKey = (typeof STATS)[number];

export function ChoicesCard({ choices, send }: { choices: CharacterChoicesData; send: (action: string, data?: unknown) => void }) {
  if (choices.waiting <= 0) return null;

  return (
    <div class="tx-choices-card">
      <div class="tx-choice-header">
        <span class="tx-badge gold">{choices.waiting} choice{choices.waiting > 1 ? "s" : ""} waiting</span>
        <h3 class="tx-choice-heading">Level up choices for {choices.name}</h3>
      </div>

      {choices.needsSubclass && choices.subclasses && choices.subclasses.length > 0 ? (
        <div class="tx-choice-section">
          <h4 class="tx-choice-subtitle">Choose your subclass</h4>
          <div class="tx-choice-buttons">
            {choices.subclasses.map((s) => (
              <button
                type="button"
                key={s.id}
                class="tx-btn outline tx-choice-btn"
                onClick={() => send("chooseSubclass", { subclass: s.id })}
              >
                <b>{s.name}</b>
              </button>
            ))}
          </div>
        </div>
      ) : null}

      {choices.increasesLeft >= 2 ? (
        <IncreaseChoice increasesLeft={choices.increasesLeft} send={send} />
      ) : null}

      {choices.choices.map((slot, i) => {
        const choose = slot.choose ?? 1;
        if (slot.picked.length >= choose) return null;
        return (
          <SlotPickChoice
            key={i}
            slot={slot}
            allChoices={choices.choices}
            slotIndex={i}
            send={send}
          />
        );
      })}
    </div>
  );
}

function IncreaseChoice({ increasesLeft, send }: { increasesLeft: number; send: (action: string, data?: unknown) => void }) {
  const [chosen, setChosen] = useState<StatKey[]>([]);

  const toggle = (s: StatKey) => {
    setChosen((c) => (c.includes(s) ? c.filter((x) => x !== s) : [...c, s].slice(-2)));
  };

  const label =
    chosen.length === 1
      ? `${chosen[0].toUpperCase()} +2`
      : chosen.length === 2
      ? `${chosen[0].toUpperCase()} +1, ${chosen[1].toUpperCase()} +1`
      : "Select ability to raise";

  return (
    <div class="tx-choice-section">
      <h4 class="tx-choice-subtitle">Ability score increase ({increasesLeft} pts)</h4>
      <p class="tx-note">One score +2, or two scores +1</p>
      <div class="tx-stat-pills">
        {STATS.map((s) => (
          <button
            type="button"
            key={s}
            class={`tx-pill${chosen.includes(s) ? " on" : ""}`}
            onClick={() => toggle(s)}
          >
            {s.toUpperCase()}
          </button>
        ))}
      </div>
      <button
        type="button"
        class="tx-btn gold"
        disabled={chosen.length === 0}
        onClick={() => {
          send("spendIncrease", { stats: chosen });
          setChosen([]);
        }}
      >
        {label}
      </button>
    </div>
  );
}

function SlotPickChoice({
  slot,
  allChoices,
  slotIndex,
  send,
}: {
  slot: ChoiceSlotData;
  allChoices: ChoiceSlotData[];
  slotIndex: number;
  send: (action: string, data?: unknown) => void;
}) {
  const choose = slot.choose ?? 1;

  return (
    <div class="tx-choice-section">
      <h4 class="tx-choice-subtitle">
        Level {slot.level} pick ({slot.picked.length}/{choose})
      </h4>
      <div class="tx-choice-buttons">
        {slot.from.map((opt) => {
          const isPicked = slot.picked.includes(opt.id);
          return (
            <button
              type="button"
              key={opt.id}
              class={`tx-btn outline tx-choice-btn${isPicked ? " selected" : ""}`}
              disabled={isPicked}
              title={opt.tooltip || opt.name}
              onClick={() => {
                const currentSlotPicks = [...slot.picked, opt.id];
                const otherPicks = allChoices.flatMap((c, idx) => (idx === slotIndex ? [] : c.picked));
                send("setPicks", { picks: [...otherPicks, ...currentSlotPicks] });
              }}
            >
              <b>{opt.name}</b>
              {opt.tooltip ? <span class="tx-choice-tip">{opt.tooltip}</span> : null}
            </button>
          );
        })}
      </div>
    </div>
  );
}
