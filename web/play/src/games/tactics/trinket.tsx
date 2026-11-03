import { useState } from "preact/hooks";
import { Crest } from "./art";
import { Sheet } from "./details";

export interface Trinket {
  id: string; name: string; text: string; rarity: string;
  class: string; className: string; active: boolean;
}

/** The one worn keepsake. Its rules and eligibility come from the server. */
export function TrinketSlot({ trinket }: { trinket: Trinket }) {
  const [open, setOpen] = useState(false);
  return <>
    <button type="button" class="tx-trinket" onClick={() => setOpen(true)} aria-label={`Trinket: ${trinket.name}, ${trinket.active ? "active" : "dormant"}`}>
      <Crest cls={trinket.class} scale={2} />
      <span><small>Trinket · {trinket.rarity}</small><strong>{trinket.name}</strong><span class="tx-note">{trinket.active ? "Active" : `Dormant · requires ${trinket.className}`}</span></span>
      <span aria-hidden="true">›</span>
    </button>
    {open ? <Sheet title={trinket.name} eyebrow={`Rare trinket · ${trinket.className}`} onClose={() => setOpen(false)}>
      <p>{trinket.text}</p>
      <p class="tx-note">{trinket.active ? "This trinket is active for your class." : `Its effects are dormant. They return if you become a ${trinket.className} again.`}</p>
      <p class="tx-note">You wear one trinket. Remaking your character keeps it.</p>
    </Sheet> : null}
  </>;
}
