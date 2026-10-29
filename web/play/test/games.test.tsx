import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/preact";
import type { CustomView, LobbyState } from "../src/protocol";
import type { CustomScreenProps, GameModule } from "../src/games/module";
import { loadModule, moduleFor, registerModuleForTests, resetModulesForTests } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { Session } from "../src/session/session";
import type { KeyValueStore } from "../src/session/storage";
import lobbyFixture from "./fixtures/lobby.json";
import customFixture from "./fixtures/custom.json";
import { FakeTransport, flush } from "./fakeTransport";

class MapStore implements KeyValueStore {
  map = new Map<string, string>();
  getItem(k: string) { return this.map.get(k) ?? null; }
  setItem(k: string, v: string) { this.map.set(k, v); }
  removeItem(k: string) { this.map.delete(k); }
}

const customView = (over: Partial<CustomView> = {}): CustomView => ({ ...(structuredClone(customFixture) as CustomView), ...over });

beforeEach(resetModulesForTests);
afterEach(cleanup);

describe("game modules", () => {
  it("moduleFor_is_undefined_until_loadModule_resolves_redline", async () => {
    expect(moduleFor("redline")).toBeUndefined();
    const pending = loadModule("redline");
    expect(moduleFor("redline")).toBeUndefined(); // a dynamic import is never synchronous
    const loaded = await pending;
    expect(loaded?.id).toBe("redline");
    expect(moduleFor("redline")).toBe(loaded);
    expect(moduleFor("redline")?.skin?.id).toBe("redline");
    expect(await loadModule("redline")).toBe(loaded); // idempotent, cached
  });

  it("loadModule resolves undefined for an unknown or missing id", async () => {
    expect(await loadModule("nope")).toBeUndefined();
    expect(await loadModule(null)).toBeUndefined();
    expect(await loadModule(undefined)).toBeUndefined();
    expect(moduleFor("nope")).toBeUndefined();
    expect(moduleFor(null)).toBeUndefined();
  });

  it("Session_loads_module_when_lobby_selects_a_game", async () => {
    const t = new FakeTransport();
    const s = new Session(t, new MapStore());
    t.responders.Join = () => ({ code: "BCDF", playerId: "p1", secret: "s" });
    await s.join("bcdf", "ALEX");
    expect(moduleFor("redline")).toBeUndefined();

    t.fire("LobbyChanged", { ...(structuredClone(lobbyFixture) as LobbyState), selectedGameId: "redline" });
    await waitFor(() => expect(moduleFor("redline")).toBeDefined());
    expect(moduleFor("redline")?.id).toBe("redline");
  });

  it("Session does not load anything while no game is selected", async () => {
    const t = new FakeTransport();
    const s = new Session(t, new MapStore());
    t.responders.Join = () => ({ code: "BCDF", playerId: "p1", secret: "s" });
    await s.join("bcdf", "ALEX");
    t.fire("LobbyChanged", { ...(structuredClone(lobbyFixture) as LobbyState), selectedGameId: null });
    await flush();
    expect(moduleFor("redline")).toBeUndefined();
  });
});

describe("Custom view", () => {
  const targetPicker = ({ data, send }: CustomScreenProps) => (
    <div>
      <p data-testid="data">{JSON.stringify(data)}</p>
      <button onClick={() => send("target", { id: "orc1" })}>Hit orc</button>
      <button onClick={() => send("cancel")}>Cancel</button>
    </div>
  );
  const tactics: GameModule = { id: "tactics", screens: { targetPicker } };

  it("Custom_view_renders_module_screen_and_send_posts_CustomInput", () => {
    registerModuleForTests("tactics", tactics);
    const send = vi.fn();
    render(<ControllerScreen view={customView()} send={send} me="ALEX" game="tactics" />);

    expect(JSON.parse(screen.getByTestId("data").textContent!)).toEqual((customFixture as { data: unknown }).data);
    fireEvent.click(screen.getByRole("button", { name: "Hit orc" }));
    expect(send).toHaveBeenLastCalledWith({ action: "target", data: { id: "orc1" } });

    // Repeated input on the same viewId is fine (a d-pad): custom screens don't lock after one send.
    fireEvent.click(screen.getByRole("button", { name: "Hit orc" }));
    expect(send).toHaveBeenCalledTimes(2);
  });

  it("send omits data when none is given", () => {
    registerModuleForTests("tactics", tactics);
    const send = vi.fn();
    render(<ControllerScreen view={customView()} send={send} me="ALEX" game="tactics" />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(send).toHaveBeenCalledWith({ action: "cancel" });
    expect(Object.keys(send.mock.calls[0]![0] as object)).toEqual(["action"]);
  });

  it("the module need not be the lobby's game", () => {
    registerModuleForTests("tactics", tactics);
    render(<ControllerScreen view={customView()} send={() => {}} me="ALEX" game="redline" />);
    expect(screen.getByRole("button", { name: "Hit orc" })).toBeTruthy();
  });

  it("Unknown_screen_shows_newer_app_card_with_Reload", () => {
    registerModuleForTests("tactics", tactics);
    const reload = vi.fn();
    vi.stubGlobal("location", { ...window.location, reload });
    try {
      render(<ControllerScreen view={customView({ screen: "charSheet" })} send={() => {}} me="ALEX" game="tactics" />);
      expect(screen.getByText("This screen needs a newer app")).toBeTruthy();
      fireEvent.click(screen.getByRole("button", { name: "Reload" }));
      expect(reload).toHaveBeenCalledTimes(1);
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("an unknown module shows the newer app card", () => {
    render(<ControllerScreen view={customView({ module: "ghost" })} send={() => {}} me="ALEX" game="ghost" />);
    expect(screen.getByText("This screen needs a newer app")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Reload" })).toBeTruthy();
  });

  it("shows the Wait spinner while the module loads, then the screen or the card", async () => {
    // "redline" has a real (lazy) loader and no screens: loading, then the card.
    render(<ControllerScreen view={customView({ module: "redline" })} send={() => {}} me="ALEX" game={null} />);
    expect(document.querySelector(".spinner")).not.toBeNull();
    expect(screen.queryByText("This screen needs a newer app")).toBeNull();

    await act(async () => { await loadModule("redline"); });
    expect(document.querySelector(".spinner")).toBeNull();
    expect(screen.getByText("This screen needs a newer app")).toBeTruthy();
  });

  it("applies the skin once the game's module has loaded", async () => {
    const wait = { kind: "wait", viewId: "w", message: "Correct! +4 · now P2" } as const;
    const { container } = render(<ControllerScreen view={wait} send={() => {}} me="ALEX" game="redline" />);
    expect(container.querySelector(".wait-banner")).toBeNull(); // generic look while loading
    await act(async () => { await loadModule("redline"); });
    expect(container.querySelector(".wait-banner.good")).not.toBeNull();
  });
});
