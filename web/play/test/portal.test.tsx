import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/preact";
import type { CampaignResponse } from "../src/protocol";
import type { GameModule, PortalProps } from "../src/games/module";
import { registerModuleForTests, resetModulesForTests } from "../src/games/registry";
import { Portal } from "../src/portal/Portal";
import { getCampaign, sendCommand } from "../src/portal/api";
import { profilesKey, type KeyValueStore } from "../src/session/storage";

class MapStore implements KeyValueStore {
  map = new Map<string, string>();
  getItem(k: string) { return this.map.get(k) ?? null; }
  setItem(k: string, v: string) { this.map.set(k, v); }
  removeItem(k: string) { this.map.delete(k); }
}

const LEASE = "A session is in progress. Use the in-game menu.";
const STALE = "Someone else changed this. Here's the latest.";

const campaign = (version: number, over: Partial<CampaignResponse> = {}, view: unknown = { gold: version }): CampaignResponse => ({
  info: { id: "c1", code: "EMBR", gameId: "tactics", name: "Embers", owner: "pr_a", members: [], version },
  view,
  leasedBy: null,
  ...over,
});

const json = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

function storeWith(...keys: { profileId: string; key: string; lastUsed: number }[]): MapStore {
  const store = new MapStore();
  store.setItem(profilesKey(), JSON.stringify(keys.map((k) => ({ ...k, name: k.profileId }))));
  return store;
}

let last: PortalProps | null = null;
function Fake(props: PortalProps) {
  last = props;
  return (
    <div>
      <span data-testid="gold">{String((props.campaign.view as { gold: number }).gold)}</span>
      <span data-testid="ro">{String(props.readOnly)}</span>
      <button type="button" onClick={() => void props.send("spend", { n: 1 })}>spend</button>
    </div>
  );
}

const fetchMock = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>();
const auth = (call: number) => (fetchMock.mock.calls[call][1]?.headers as Record<string, string>).Authorization;

beforeEach(() => {
  resetModulesForTests();
  last = null;
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  registerModuleForTests("tactics", { id: "tactics", portal: Fake });
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("portal shell", () => {
  it("No_key_shows_pick_yourself_card", async () => {
    render(<Portal code="EMBR" store={new MapStore()} />);
    expect(await screen.findByText("Pick yourself in a session first")).toBeTruthy();
    expect(screen.getByText("Open this page after you've picked your character in a session.")).toBeTruthy();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("Tries_keys_until_not_401", async () => {
    fetchMock.mockImplementation(async (_url, init) =>
      (init?.headers as Record<string, string>).Authorization === "Profile good" ? json(200, campaign(3)) : json(401));
    const store = storeWith(
      { profileId: "old", key: "stale1", lastUsed: 1 },
      { profileId: "new", key: "stale2", lastUsed: 9 },
      { profileId: "mid", key: "good", lastUsed: 5 },
    );
    render(<Portal code="EMBR" store={store} />);
    expect((await screen.findByTestId("gold")).textContent).toBe("3");
    expect([auth(0), auth(1)]).toEqual(["Profile stale2", "Profile good"]); // most recent first, 401 skipped
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/campaigns/EMBR");

    // The key that worked is the one later commands use.
    fetchMock.mockResolvedValueOnce(json(200, campaign(4)));
    fireEvent.click(screen.getByText("spend"));
    await waitFor(() => expect(screen.getByTestId("gold").textContent).toBe("4"));
    expect(auth(2)).toBe("Profile good");
  });

  it("All_keys_401_shows_pick_yourself_card", async () => {
    fetchMock.mockResolvedValue(json(401));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    expect(await screen.findByText("Pick yourself in a session first")).toBeTruthy();
  });

  it("Leased_campaign_is_read_only_with_banner", async () => {
    fetchMock.mockResolvedValue(json(200, campaign(3, { leasedBy: "ZZZZ" })));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    expect(await screen.findByText(LEASE)).toBeTruthy();
    expect(screen.getByTestId("ro").textContent).toBe("true");
  });

  it("Lease_409_on_command_shows_server_message_and_goes_read_only", async () => {
    fetchMock.mockResolvedValueOnce(json(200, campaign(3)));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    await screen.findByTestId("gold");
    expect(screen.getByTestId("ro").textContent).toBe("false");

    fetchMock.mockResolvedValueOnce(json(409, { error: LEASE, current: campaign(3, { leasedBy: "ZZZZ" }) }));
    fireEvent.click(screen.getByText("spend"));
    expect(await screen.findByText(LEASE)).toBeTruthy();
    expect(screen.getByTestId("ro").textContent).toBe("true");
    expect(screen.queryByText(STALE)).toBeNull();
  });

  it("Version_409_refreshes_view_with_notice", async () => {
    fetchMock.mockResolvedValueOnce(json(200, campaign(3)));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    await screen.findByTestId("gold");

    fetchMock.mockResolvedValueOnce(json(409, { error: STALE, current: campaign(7, {}, { gold: 70 }) }));
    fireEvent.click(screen.getByText("spend"));
    expect(await screen.findByText(STALE)).toBeTruthy();
    expect(screen.getByTestId("gold").textContent).toBe("70");
    expect(screen.getByTestId("ro").textContent).toBe("false");
    // The command carried the version on screen, and the retry carries the refreshed one.
    expect(JSON.parse(fetchMock.mock.calls[1][1]?.body as string)).toEqual({ type: "spend", data: { n: 1 }, version: 3 });
    fetchMock.mockResolvedValueOnce(json(200, campaign(8, {}, { gold: 69 })));
    fireEvent.click(screen.getByText("spend"));
    await waitFor(() => expect(screen.getByTestId("gold").textContent).toBe("69"));
    expect(JSON.parse(fetchMock.mock.calls[2][1]?.body as string).version).toBe(7);
    expect(screen.queryByText(STALE)).toBeNull();
  });

  it("Rules_400_shows_error", async () => {
    fetchMock.mockResolvedValueOnce(json(200, campaign(3)));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    await screen.findByTestId("gold");

    fetchMock.mockResolvedValueOnce(json(400, { error: "Not enough gold." }));
    fireEvent.click(screen.getByText("spend"));
    expect(await screen.findByText("Not enough gold.")).toBeTruthy();
    expect(screen.getByTestId("gold").textContent).toBe("3"); // unchanged
  });

  it("Module_portal_renders_with_campaign", async () => {
    fetchMock.mockResolvedValue(json(200, campaign(5)));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    await screen.findByTestId("gold");
    expect(last?.campaign.info.version).toBe(5);
    expect(last?.readOnly).toBe(false);
  });

  it("Module_without_portal_shows_nothing_to_manage", async () => {
    registerModuleForTests("tactics", { id: "tactics" } satisfies GameModule);
    fetchMock.mockResolvedValue(json(200, campaign(5)));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    expect(await screen.findByText("This game has nothing to manage between sessions.")).toBeTruthy();
  });

  it("Unknown_game_shows_nothing_to_manage", async () => {
    const other = campaign(5);
    fetchMock.mockResolvedValue(json(200, { ...other, info: { ...other.info, gameId: "nope" } }));
    render(<Portal code="EMBR" store={storeWith({ profileId: "a", key: "k", lastUsed: 1 })} />);
    expect(await screen.findByText("This game has nothing to manage between sessions.")).toBeTruthy();
  });

  it("Not_found_and_rate_limit_show_their_messages", async () => {
    fetchMock.mockResolvedValueOnce(json(404, { error: "Campaign not found." }));
    const store = storeWith({ profileId: "a", key: "k", lastUsed: 1 });
    const first = render(<Portal code="EMBR" store={store} />);
    expect(await screen.findByText("Campaign not found.")).toBeTruthy();
    first.unmount();

    fetchMock.mockResolvedValueOnce(json(429));
    render(<Portal code="EMBR" store={store} />);
    expect(await screen.findByText("Too many requests. Try again in a minute.")).toBeTruthy();
  });
});

describe("portal api", () => {
  it("getCampaign_returns_null_key_when_no_keys", async () => {
    const r = await getCampaign("EMBR", []);
    expect(r.key).toBeNull();
    expect(r.result.ok).toBe(false);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("getCampaign_stops_at_a_403_that_is_not_401", async () => {
    fetchMock.mockResolvedValueOnce(json(403));
    const keys = [{ profileId: "a", key: "k1", name: "A", lastUsed: 2 }, { profileId: "b", key: "k2", name: "B", lastUsed: 1 }];
    const r = await getCampaign("EMBR", keys);
    expect(r.key?.key).toBe("k1");
    expect(r.result).toMatchObject({ ok: false, status: 403 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("sendCommand_posts_camelCase_body_with_profile_header", async () => {
    fetchMock.mockResolvedValueOnce(json(200, campaign(2)));
    const key = { profileId: "a", key: "k1", name: "A", lastUsed: 1 };
    const r = await sendCommand("EMBR", key, "spend", { n: 2 }, 1);
    expect(r).toMatchObject({ ok: true, status: 200 });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/campaigns/EMBR/commands");
    expect(init?.method).toBe("POST");
    expect((init?.headers as Record<string, string>)["Content-Type"]).toBe("application/json");
    expect(auth(0)).toBe("Profile k1");
    expect(JSON.parse(init?.body as string)).toEqual({ type: "spend", data: { n: 2 }, version: 1 });
  });

  it("a_network_failure_is_status_0", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("offline"));
    const r = await sendCommand("EMBR", { profileId: "a", key: "k", name: "A", lastUsed: 1 }, "x", null, 1);
    expect(r).toMatchObject({ ok: false, status: 0, error: null });
  });
});
