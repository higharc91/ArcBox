import type { ControllerView } from "../../protocol";
import { crewSound, type CrewSound } from "../../feedback/crew-tones";

const record = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null;

/** Observe server views, never optimistic taps. One local confirmation per Boarding selection. */
export class CrewSelection {
  private viewId: string | null = null;
  private boarding = false;
  private armed = false;
  private selected: string | null = null;

  reset(): void { this.viewId = null;this.boarding = false;this.armed = false;this.selected = null; }

  observe(view: ControllerView, game?: string | null): CrewSound | undefined {
    if (game !== "meridian" || view.kind !== "custom" || view.module !== "meridian") {
      this.reset();return;
    }
    if (view.viewId === this.viewId) return;
    this.viewId = view.viewId;
    if (!record(view.data)) return;
    if (view.screen === "crewPick") {
      if (!Array.isArray(view.data.crew) || !view.data.crew.some(c => record(c) && crewSound(c.id))) return;
      const mine = view.data.crew.filter(c => record(c) && c.mine === true);
      const selected = mine.length === 1 && typeof mine[0].id === "string" ? mine[0].id : null;
      if (!this.boarding) {
        this.boarding = true;
        // A first view that already says Chosen is a reconnect/baseline, not a new lock-in.
        this.selected = selected;
        this.armed = true;
        return;
      }
      if (selected !== null && selected !== this.selected) {
        this.selected = selected;return crewSound(selected);
      }
      return;
    }
    this.boarding = false;
    if (!["watch", "sheet", "turn"].includes(view.screen)) { this.reset();return; }
    // The last picker can jump directly to Explore. Wait through any presentation
    // views until their own sheet/turn identifies the actual accepted (or auto-assigned) crew.
    if (this.armed && (view.screen === "sheet" || view.screen === "turn") && record(view.data.me) && typeof view.data.me.id === "string") {
      this.armed = false;
      const changed = view.data.me.id !== this.selected;
      this.selected = view.data.me.id;
      return changed ? crewSound(this.selected) : undefined;
    }
  }
}
