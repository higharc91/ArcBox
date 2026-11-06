import { Countdown } from "../../widgets/Countdown";
import type { FallBackData } from "./types";

type Send = (action: string, data?: unknown) => void;

/**
 * Falling back from a losing battle (#111), on every battle screen: a quiet "Fall back" button from round 3, the vote once someone
 * proposes it (a strict majority of the players here ends the fight as a flight; 20 s), or a note that it was voted down this round.
 * Nothing is shown before round 3 or in a fight that forbids it, when the server sends no `fallBack`. Falling back is the player's
 * action, whichever of their units is acting, so it is the same on a hero's turn and a summon's.
 */
export function FallBack({ fallBack, send }: { fallBack?: FallBackData; send: Send }) {
  if (!fallBack) return null;
  const vote = fallBack.vote;
  if (vote) {
    return (
      <section class="tx-card tx-fallback vote" aria-label="Fall back vote">
        <b>{vote.proposer} wants to fall back</b>
        <span class="tx-tip">
          Half XP, injuries kept, no loot. {vote.yes} of {vote.needed} needed · {vote.present} here
        </span>
        {vote.until ? <Countdown deadlineUtc={vote.until} /> : null}
        {vote.mine ? (
          <span class="tx-tip">You voted {vote.mine === "yes" ? "to fall back" : "to stay and fight"}. You can change it.</span>
        ) : null}
        <div class="tx-fallback-buttons">
          <button type="button" class={`tx-btn${vote.mine === "yes" ? " gold" : ""}`} aria-pressed={vote.mine === "yes"}
            onClick={() => send("fallBackVote", { yes: true })}>Fall back</button>
          <button type="button" class={`tx-btn${vote.mine === "no" ? " gold" : ""}`} aria-pressed={vote.mine === "no"}
            onClick={() => send("fallBackVote", { yes: false })}>Stay and fight</button>
        </div>
      </section>
    );
  }
  if (fallBack.failed) return <p class="tx-note tx-fallback failed">The party voted to stay and fight. Fall back can be proposed again next round.</p>;
  if (fallBack.canPropose) {
    return (
      <div class="tx-fallback propose">
        <button type="button" class="tx-btn ghost" onClick={() => send("fallBack")}>Fall back</button>
        <span class="tx-note">Propose a retreat: the party votes</span>
      </div>
    );
  }
  return null;
}
