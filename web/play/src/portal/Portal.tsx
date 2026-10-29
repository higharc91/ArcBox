import type { ComponentChildren } from "preact";
import { useEffect, useRef, useState } from "preact/hooks";
import type { CampaignResponse } from "../protocol";
import { useGameModule } from "../games/registry";
import { loadProfileKeys, type KeyValueStore, type StoredProfile } from "../session/storage";
import { getCampaign, sendCommand, type ApiResult } from "./api";

export const LEASE_MESSAGE = "A session is in progress. Use the in-game menu.";
export const STALE_MESSAGE = "Someone else changed this. Here's the latest.";

type Load =
  | { phase: "loading" }
  | { phase: "nokey" }
  | { phase: "failed"; message: string }
  | { phase: "ready"; campaign: CampaignResponse };

/** What a failed call says to the player (the shell handles 409, and 401 on a command, itself). */
function failureMessage(status: number, error: string | undefined): string {
  switch (status) {
    case 400: return error ?? "That didn't work.";
    case 403: return "You're not a member of this campaign.";
    case 404: return "Campaign not found.";
    case 429: return "Too many requests. Try again in a minute.";
    case 0: return "Couldn't reach the server. Check your connection and try again.";
    default: return error ?? "Something went wrong. Try again.";
  }
}

/**
 * The between-sessions campaign screen at `/c/<code>` (spec §3.4): signs in with a profile key from this phone, loads
 * the campaign, and hands the game module's `portal` component the campaign, a read-only flag and `send`.
 */
export function Portal({ code, store }: { code: string; store: KeyValueStore }) {
  const [load, setLoad] = useState<Load>({ phase: "loading" });
  const [leaseMessage, setLeaseMessage] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // `send` is handed to the game and may be called from a stale render, so it reads what is on screen from refs.
  const key = useRef<StoredProfile | null>(null);
  const shown = useRef<CampaignResponse | null>(null);

  const show = (campaign: CampaignResponse, lease: string | null) => {
    shown.current = campaign;
    setLeaseMessage(lease ?? (campaign.leasedBy ? LEASE_MESSAGE : null));
    setLoad({ phase: "ready", campaign });
  };

  useEffect(() => {
    let cancelled = false;
    void getCampaign(code, loadProfileKeys(store)).then(({ key: worked, result }) => {
      if (cancelled) return;
      key.current = worked;
      if (result.ok) show(result.body, null);
      else if (result.status === 401) setLoad({ phase: "nokey" });
      else setLoad({ phase: "failed", message: failureMessage(result.status, result.error?.error) });
    });
    return () => void (cancelled = true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [code, store]);

  const send = async (type: string, data: unknown): Promise<void> => {
    const current = shown.current;
    const as = key.current;
    if (!current || !as) return;
    setError(null);
    const result: ApiResult<CampaignResponse> = await sendCommand(code, as, type, data, current.info.version);
    if (result.ok) {
      setNotice(null);
      show(result.body, null);
      return;
    }
    const latest = result.error?.current ?? null;
    if (result.status === 409) {
      const lease = latest?.leasedBy || result.error?.error === LEASE_MESSAGE;
      if (lease) {
        const message = result.error?.error ?? LEASE_MESSAGE;
        setNotice(null);
        if (latest) show(latest, message);
        else setLeaseMessage(message);
      } else {
        setNotice(STALE_MESSAGE);
        if (latest) show(latest, null);
      }
      return;
    }
    if (result.status === 401) {
      setLoad({ phase: "nokey" }); // the key stopped working (a lost-phone revoke)
      return;
    }
    setError(failureMessage(result.status, result.error?.error));
  };

  if (load.phase === "loading") return <PortalFrame><p class="hint">Loading…</p></PortalFrame>;
  if (load.phase === "nokey") {
    return (
      <PortalFrame>
        <h1 class="portal-title">Pick yourself in a session first</h1>
        <p class="hint">Open this page after you've picked your character in a session.</p>
      </PortalFrame>
    );
  }
  if (load.phase === "failed") return <PortalFrame><p class="form-error" role="alert">{load.message}</p></PortalFrame>;

  return (
    <PortalBody campaign={load.campaign} readOnly={leaseMessage !== null} lease={leaseMessage} notice={notice} error={error} send={send} />
  );
}

function PortalBody(props: {
  campaign: CampaignResponse;
  readOnly: boolean;
  lease: string | null;
  notice: string | null;
  error: string | null;
  send: (type: string, data: unknown) => Promise<void>;
}) {
  const { campaign, readOnly, lease, notice, error, send } = props;
  const { module, state } = useGameModule(campaign.info.gameId);
  const Screen = module?.portal;
  return (
    <PortalFrame>
      {lease && <p class="portal-banner" role="status">{lease}</p>}
      {notice && <p class="portal-banner" role="status">{notice}</p>}
      {error && <p class="form-error" role="alert">{error}</p>}
      {Screen ? (
        <Screen campaign={campaign} readOnly={readOnly} send={send} />
      ) : state === "loading" ? (
        <p class="hint">Loading…</p>
      ) : (
        <p class="hint">This game has nothing to manage between sessions.</p>
      )}
    </PortalFrame>
  );
}

function PortalFrame({ children }: { children: ComponentChildren }) {
  return <div class="screen portal">{children}</div>;
}
