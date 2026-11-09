import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { decodeRegion, drawing, open, screen as onDrawing, snap, tileAt, CHUNK, IMPASSABLE } from "../src/games/tactics/regionMap";

const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/tactics/*.json", { eager: true });
const view = (name: string, viewId = "v1"): CustomView => {
  const mod = fixtures[`./fixtures/tactics/${name}.json`];
  if (!mod) throw new Error(`missing fixture tactics/${name}`);
  return { ...(structuredClone(mod.default) as CustomView), viewId };
};

const sent = (send: ReturnType<typeof vi.fn>) => send.mock.calls.map((c) => c[0] as { action: string; data?: unknown }).filter((c) => c.action !== "walk");

function show(name: string) {
  const send = vi.fn();
  const utils = render(<ControllerScreen view={view(name)} send={send} me="ALEX" game="tactics" />);
  const again = (next: string, id: string) => utils.rerender(<ControllerScreen view={view(next, id)} send={send} me="ALEX" game="tactics" />);
  return { send, again, ...utils };
}

beforeAll(async () => {
  await loadModule("tactics");
});
beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new Error("no pack in tests"))));
  vi.spyOn(console, "warn").mockImplementation(() => {});
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the map sheet (tap to travel)", () => {
  it("is the leader's: a follower has no Map button", () => {
    show("walk-follower");
    expect(screen.queryByRole("button", { name: "Map" })).toBeNull();
    cleanup();
    show("walk-leader");
    expect(screen.getByRole("button", { name: "Map" })).not.toBeNull();
  });

  it("opens on Map, asks the server for the map, and shows the region, the party and the landmarks", () => {
    const { send, again } = show("walk-leader");
    fireEvent.click(screen.getByRole("button", { name: "Map" }));
    expect(sent(send)).toEqual([{ action: "mapOpen" }]);
    expect(screen.getByRole("dialog")).not.toBeNull();
    expect(screen.getByText("Opening the map…")).not.toBeNull();

    again("walk-map", "v2");
    expect(screen.getByRole("img", { name: "The party" })).not.toBeNull();
    expect(screen.getByRole("button", { name: "The Last Coal: go there" })).not.toBeNull();
    expect(screen.getByRole("button", { name: "Emberwick: go there" })).not.toBeNull();
    const places = screen.getByRole("list", { name: "Places" });
    expect(places.textContent).toContain("Chapel of the Hearth");
    expect(places.textContent).toContain("Temple");
  });

  it("a tap on a pin sends travel with its tile, and the sheet closes", () => {
    const { send } = show("walk-map");          // the server says it's open: a reconnect opens it again
    fireEvent.click(screen.getByRole("button", { name: "The Last Coal: go there" }));
    expect(sent(send)).toEqual([{ action: "travel", data: { x: 25, y: 55 } }]);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("a tap anywhere on the map sends the tile under it", () => {
    const { send } = show("walk-map");
    const plane = document.querySelector<HTMLElement>(".tx-map-plane")!;
    const { width, height } = drawing(160, 120);
    plane.getBoundingClientRect = () => ({ left: 0, top: 0, right: width, bottom: height, width, height, x: 0, y: 0, toJSON: () => ({}) });
    const { sx, sy } = onDrawing(120, 30, 60);
    fireEvent.click(plane, { clientX: sx, clientY: sy });
    expect(sent(send)).toEqual([{ action: "travel", data: { x: 30, y: 60 } }]);
  });

  it("closes with Close, and tells the server", () => {
    const { send } = show("walk-map");
    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(sent(send)).toEqual([{ action: "mapClose" }]);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("closes when the lead goes to someone else", () => {
    const { again } = show("walk-map");
    expect(screen.getByRole("dialog")).not.toBeNull();
    again("walk-follower", "v2");
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("says a journey is under way, and when a pick went nowhere", () => {
    show("walk-travelling");
    expect(screen.getByText("Travelling… (tap the pad to stop)")).not.toBeNull();
    cleanup();
    show("walk-travel-nowhere");
    expect(screen.getByText("Can't find a way there.")).not.toBeNull();
    expect(screen.queryByText("Travelling… (tap the pad to stop)")).toBeNull();
  });

  it("zooms in and back out", () => {
    show("walk-map");
    fireEvent.click(screen.getByRole("button", { name: "Zoom in" }));
    expect(document.querySelector(".tx-map-scroll.zoomed")).not.toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Show it all" }));
    expect(document.querySelector(".tx-map-scroll.zoomed")).toBeNull();
  });
});

describe("the region on the map", () => {
  /** A 40 × 34 region of two chunks side by side, all ground but a wall at (5, 5) and a block of wall from (20, 0) to (30, 10). */
  const region = () => {
    const chunk = (cx: number) => {
      const bytes = new Uint8Array(CHUNK * CHUNK * 3);
      for (let i = 0; i < CHUNK * CHUNK; i++) {
        const [x, y] = [cx * CHUNK + (i % CHUNK), Math.floor(i / CHUNK)];
        bytes[i * 3] = 1;
        if ((x === 5 && y === 5) || (x >= 20 && x <= 30 && y <= 10)) bytes[i * 3 + 2] = IMPASSABLE;
      }
      return { x: cx, y: 0, tiles: btoa(String.fromCharCode(...bytes)) };
    };
    return decodeRegion({ size: [40, 34], materials: ["grass/lush"], chunks: [chunk(0), chunk(1)] });
  };

  it("decodes its chunks, with what isn't in one as void", () => {
    const r = region();
    expect([r.w, r.h]).toEqual([40, 34]);
    expect(open(r, 0, 0)).toBe(true);
    expect(open(r, 35, 20)).toBe(true);     // in the second chunk
    expect(open(r, 5, 5)).toBe(false);
    expect(open(r, 3, 33)).toBe(false);     // below both chunks: void
    expect(open(r, -1, 0)).toBe(false);
  });

  it("snaps a tap off the ground to the nearest ground, and leaves one with none near as it is", () => {
    const r = region();
    expect(snap(r, 4, 4)).toEqual([4, 4]);
    expect(snap(r, 5, 5)).toEqual([4, 5]);
    expect(snap(r, 25, 5)).toEqual([25, 5]);      // more than 4 from any ground: the server says so
    expect(snap(r, 21, 5)).toEqual([19, 5]);
    expect(snap(null, 5, 5)).toEqual([5, 5]);
  });

  it("finds the tile under any point of its drawing", () => {
    for (const [x, y] of [[0, 0], [39, 0], [0, 33], [39, 33], [17, 9]] as const) {
      const { sx, sy } = onDrawing(34, x, y);
      expect(tileAt(34, sx, sy)).toEqual([x, y]);
      expect(tileAt(34, sx + 1.5, sy + 0.5)).toEqual([x, y]);    // anywhere inside its diamond
    }
  });
});
