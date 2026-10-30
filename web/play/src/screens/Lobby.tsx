import { useState } from "preact/hooks";
import { MuteToggle } from "../feedback/MuteToggle";
import type { LobbyState, PlayerId, PlayerScore, RosterEntry, SeatRef } from "../protocol";
import type { StoredProfile } from "../session/storage";
import { NAME_MAX, isValidName } from "./Join";
import { cardStyle } from "./Pick";

export const CAMPAIGN_NAME_MAX = 32;

interface Props {
  lobby: LobbyState;
  me: PlayerId;
  error?: string;
  onSelectGame: (gameId: string) => void;
  onStart: () => void;
  onSelectCampaign: (campaignId: string) => void;
  onCreateCampaign: (name: string) => void;
  /** Say which campaign member this phone is; a null member is "I'm new here", under `newName` when one was typed. */
  onClaim: (member: SeatRef | null, newName?: string) => void;
  /** The profile keys this phone holds, most recently used first: "Who are you?" offers the first one on the roster. */
  profiles?: StoredProfile[];
}

const range = (min: number, max: number) => (min === max ? `${min}` : `${min}–${max}`);

/** "Ann won with 12", or "Ann and Bo tied with 12" when the top scores are equal (Counter orders a tie by name). */
function winnerLine(scores: PlayerScore[]): string {
  const top = scores[0]!.score;
  const names = scores.filter((s) => s.score === top).map((s) => s.name);
  if (names.length === 1) return `${names[0]} won with ${top}`;
  return `${names.slice(0, -1).join(", ")} and ${names.at(-1)} tied with ${top}`;
}

export function Lobby({ lobby, me, error, onSelectGame, onStart, onSelectCampaign, onCreateCampaign, onClaim, profiles = [] }: Props) {
  const vip = lobby.players.find((p) => p.isVip);
  const iAmVip = vip?.id === me;
  const count = lobby.players.length;
  // Games that can never fit this many players are hidden; ones that need more are shown with their range.
  const games = lobby.games.filter((g) => count <= g.maxPlayers);
  const selected = lobby.games.find((g) => g.id === lobby.selectedGameId);
  const campaign = lobby.campaign;
  // A seated phone that isn't one of the campaign's members yet is asked who it is (members with no connected seat).
  const mine = lobby.players.find((p) => p.id === me);
  const unmatched = campaign && !lobby.inGame && !(mine?.profile && campaign.roster.some((r) => r.profile === mine.profile));

  return (
    <div class="screen lobby">
      <header class="lobby-head">
        <div>
          <small>ROOM</small>
          <div class="room-code">{lobby.code}</div>
        </div>
        <div class="count"><b>{count}</b><small>{count === 1 ? "PLAYER" : "PLAYERS"}</small></div>
        <MuteToggle />
      </header>

      {lobby.lastResults?.message || lobby.lastResults?.scores.length ? (
        <section class="last-results">
          <small>LAST GAME</small>
          {lobby.lastResults.message ? <p>{lobby.lastResults.message}</p> : null}
          {!lobby.lastResults.message && lobby.lastResults.scores[0] ? <p>{winnerLine(lobby.lastResults.scores)}</p> : null}
        </section>
      ) : null}

      <ul class="players">
        {lobby.players.map((p) => (
          <li key={p.id} class={["player", !p.connected && "offline", p.id === me && "me"].filter(Boolean).join(" ")}>
            <span class="pname">{p.name}</span>
            {p.isVip ? <span class="vip" title="VIP: picks the game">VIP</span> : null}
            {!p.connected ? <span class="away">away</span> : null}
          </li>
        ))}
      </ul>

      {campaign && unmatched ? (
        <Claim
          campaignName={campaign.name}
          entries={campaign.roster.filter((r) => !r.connected)}
          placeholder={mine?.name ?? ""}
          profiles={profiles}
          onClaim={onClaim}
        />
      ) : null}

      {iAmVip ? (
        <section class="picker">
          <small>PICK A GAME</small>
          <div class="games">
            {games.map((g) => {
              const on = g.id === lobby.selectedGameId;
              return (
                <button
                  type="button"
                  key={g.id}
                  class={`game-card${on ? " picked" : ""}`}
                  aria-pressed={on ? "true" : "false"}
                  onClick={() => onSelectGame(g.id)}
                >
                  <span class="gname">{g.name}</span>
                  {g.description ? <span class="gdesc">{g.description}</span> : null}
                  <span class="gplayers">{range(g.minPlayers, g.maxPlayers)} players</span>
                </button>
              );
            })}
          </div>
          {selected?.usesCampaigns ? (
            <Campaigns lobby={lobby} onSelect={onSelectCampaign} onCreate={onCreateCampaign} />
          ) : null}
          <button type="button" class="primary big start" disabled={!lobby.canStart} onClick={onStart}>
            Everybody's in
          </button>
          {selected && !lobby.canStart && count < selected.minPlayers ? (
            <p class="hint">{selected.name} needs at least {selected.minPlayers} players.</p>
          ) : null}
        </section>
      ) : (
        <section class="waiting">
          {selected ? <p class="next-game">Next up: <b>{selected.name}</b></p> : null}
          {campaign ? <p class="next-game">Campaign: <b>{campaign.name}</b></p> : null}
          <p>{vip ? `Waiting for ${vip.name} to start` : "Waiting for the host to start"}</p>
        </section>
      )}

      {error ? <p class="form-error" role="alert">{error}</p> : null}
    </div>
  );
}

/** The VIP's campaign step for a game that uses campaigns: pick one of the game's campaigns, or start a new one. */
function Campaigns({ lobby, onSelect, onCreate }: { lobby: LobbyState; onSelect: (id: string) => void; onCreate: (name: string) => void }) {
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const valid = name.trim().length >= 1 && name.trim().length <= CAMPAIGN_NAME_MAX;

  const submit = (e: Event) => {
    e.preventDefault();
    if (!valid) return;
    onCreate(name.trim());
    setCreating(false);
    setName("");
  };

  return (
    <div class="campaigns">
      <small>PICK A CAMPAIGN</small>
      <div class="campaign-list">
        {(lobby.campaigns ?? []).map((c) => {
          const on = c.id === lobby.campaign?.id;
          return (
            <button
              type="button"
              key={c.id}
              class={`campaign-card${on ? " picked" : ""}`}
              aria-pressed={on ? "true" : "false"}
              onClick={() => onSelect(c.id)}
            >
              <span class="cname">{c.name}</span>
              <span class="cmeta">{c.members} {c.members === 1 ? "member" : "members"}</span>
            </button>
          );
        })}
      </div>
      {creating ? (
        <form class="campaign-form" onSubmit={submit}>
          <label class="field">
            <span>Campaign name</span>
            <input
              class="text-input"
              name="campaignName"
              value={name}
              maxLength={CAMPAIGN_NAME_MAX}
              placeholder={`Up to ${CAMPAIGN_NAME_MAX} characters`}
              onInput={(e) => setName((e.currentTarget as HTMLInputElement).value)}
            />
          </label>
          <button type="submit" class="primary" disabled={!valid}>Create</button>
        </form>
      ) : (
        <button type="button" onClick={() => setCreating(true)}>New campaign</button>
      )}
    </div>
  );
}

/** The member a roster entry stands for, as ClaimMember takes it: their offline seat if they have one. */
const claimRef = (r: RosterEntry): SeatRef => (r.seatedAs ? { slot: r.seatedAs } : { member: r.profile });

/**
 * "Who are you in {campaign}?": the members nobody is playing, and "I'm new here" (with an optional name, so two Dans
 * can differ). A phone that holds a key for one of those members, most recently used first, gets "Continue as" on top.
 */
function Claim({ campaignName, entries, placeholder, profiles, onClaim }: {
  campaignName: string;
  entries: RosterEntry[];
  placeholder: string;
  profiles: StoredProfile[];
  onClaim: (member: SeatRef | null, newName?: string) => void;
}) {
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState("");
  const typed = name.trim();
  const valid = typed === "" || isValidName(typed);
  const suggested = profiles
    .map((p) => ({ p, r: entries.find((r) => r.profile === p.profileId && !r.connected) }))
    .find((s) => s.r !== undefined);

  return (
    <section class="claim">
      <h2>Who are you in {campaignName}?</h2>
      {suggested?.r ? (
        <button type="button" class="primary big claim-continue" onClick={() => onClaim(claimRef(suggested.r!))}>
          Continue as {suggested.p.name}
        </button>
      ) : null}
      <ul class="claim-list">
        {entries.map((r) => (
          <li key={r.profile}>
            <button
              type="button"
              class="claim-row"
              style={cardStyle(r.card)}
              onClick={() => onClaim(claimRef(r))}
            >
              <b>{r.card.title}</b>
              {r.card.subtitle ? <span>{r.card.subtitle}</span> : null}
            </button>
          </li>
        ))}
      </ul>
      {adding ? (
        <form
          class="campaign-form"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) onClaim(null, typed || undefined);
          }}
        >
          <label class="field">
            <span>Your name (optional)</span>
            <input
              class="text-input"
              name="claimName"
              value={name}
              maxLength={NAME_MAX}
              placeholder={placeholder}
              onInput={(e) => setName((e.currentTarget as HTMLInputElement).value)}
            />
          </label>
          <button type="submit" class="primary" disabled={!valid}>I'm new here</button>
        </form>
      ) : (
        <button type="button" class="claim-new" onClick={() => setAdding(true)}>I'm new here</button>
      )}
    </section>
  );
}
