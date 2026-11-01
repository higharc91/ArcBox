import { options } from "preact";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { Feedback, FeedbackContext } from "../src/feedback/feedback";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/tactics/*.json", { eager: true });
const view = (name: string, change?: (data: Record<string, unknown>) => void, viewId = "v1"): CustomView => {
  const mod = fixtures[`./fixtures/tactics/${name}.json`];
  if (!mod) throw new Error(`missing fixture tactics/${name}`);
  const v = structuredClone(mod.default) as CustomView;
  change?.(v.data as Record<string, unknown>);
  return { ...v, viewId };
};

const walk = (dir: string | null) => ({ action: "walk", data: { dir } });
const walks = (send: ReturnType<typeof vi.fn>) => send.mock.calls.map((c) => (c[0] as { data?: { dir?: string | null } }).data?.dir);

function show(name: string, change?: (data: Record<string, unknown>) => void) {
  const send = vi.fn();
  const utils = render(<ControllerScreen view={view(name, change)} send={send} me="ALEX" game="tactics" />);
  return { send, ...utils };
}

const box = (left: number, top: number) => ({
  left, top, right: left + 200, bottom: top + 200, width: 200, height: 200, x: left, y: top, toJSON: () => ({}),
});

/** The pad, laid out as a 200 px circle at the page's corner, as happy-dom doesn't lay anything out. */
function pad() {
  const face = document.querySelector<HTMLElement>(".tx-pad")!;
  face.getBoundingClientRect = () => box(0, 0);
  const zone = document.querySelector<HTMLElement>(".tx-padzone")!;
  zone.setPointerCapture = vi.fn();
  return { face, zone };
}

const pointer = (el: Element, type: string, x: number, y: number, pointerId = 1) => {
  const e = new MouseEvent(type, { bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 });
  Object.defineProperty(e, "pointerId", { value: pointerId });
  el.dispatchEvent(e);
  return e;
};

const NORTH = [100, 20] as const;
const NORTH_EAST = [160, 40] as const;
const EAST = [180, 100] as const;

const quietFeedback = () =>
  new Feedback({ getItem: () => null, setItem() {}, removeItem() {} } as never, { unlock() {}, play() {} } as never);

beforeAll(async () => {
  await loadModule("tactics");
});
beforeEach(() => {
  vi.useFakeTimers();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the walk pad", () => {
  it("shows the pad only to the leader", () => {
    show("walk-follower");
    expect(document.querySelector(".tx-pad")).toBeNull();
    cleanup();
    show("walk-leader");
    expect(document.querySelector(".tx-pad")).not.toBeNull();
  });

  it("draws its chevrons where the party goes, the road directions on a road's bearing", () => {
    show("walk-leader");
    const turn = (dir: string) => document.querySelector(`.tx-chevron.${dir}`)!.getAttribute("transform");
    expect([turn("n"), turn("ne"), turn("e"), turn("se"), turn("s"), turn("sw"), turn("w"), turn("nw")].map((t) => t!.match(/rotate\(([\d.]+)\)/)![1]))
      .toEqual(["0.00", "63.43", "90.00", "116.57", "180.00", "243.43", "270.00", "296.57"]);
    expect([...document.querySelectorAll(".tx-chevron.small")].map((c) => c.classList[1])).toEqual(["e", "w"]);
  });

  it("a press along a road, 10 degrees off it either way, walks the road", () => {
    const { send } = show("walk-leader");
    const { zone } = pad();
    const road = Math.atan2(2, 1);
    for (const off of [-10, 10]) {
      const a = road + (off * Math.PI) / 180;
      pointer(zone, "pointerdown", 100 + 80 * Math.sin(a), 100 - 80 * Math.cos(a));
      pointer(zone, "pointerup", 100, 100);
      vi.advanceTimersByTime(200);
    }
    expect(walks(send).filter((d) => d !== null)).toEqual(["ne", "ne"]);
  });

  it("sends a direction at once on press, and steers without a stop", () => {
    const { send } = show("walk-leader");
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenLastCalledWith(walk("n"));
    expect(zone.setPointerCapture).toHaveBeenCalledWith(1);

    vi.advanceTimersByTime(100);
    pointer(zone, "pointermove", ...EAST);
    expect(walks(send)).toEqual(["n", "e"]);
  });

  it("pings while held and stops twice on release", () => {
    const { send } = show("walk-leader");
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    vi.advanceTimersByTime(250);
    expect(walks(send)).toEqual(["n", "n"]);
    pointer(zone, "pointerup", ...NORTH);
    vi.advanceTimersByTime(150);
    expect(walks(send)).toEqual(["n", "n", null, null]);
  });

  it("the middle of the pad is a dead zone: a press there sends nothing, and sliding back in stops", () => {
    const { send } = show("walk-leader");
    const { zone, face } = pad();
    pointer(zone, "pointerdown", 104, 96);
    expect(send).not.toHaveBeenCalled();
    expect(face.classList.contains("down")).toBe(true);

    pointer(zone, "pointermove", ...NORTH);
    pointer(zone, "pointermove", 100, 100);
    expect(walks(send)).toEqual(["n", null]);
    expect(face.classList.contains("down")).toBe(true);
    expect(face.dataset.dir).toBe("");
  });

  it("a thumb off the pad keeps steering by its angle from the centre", () => {
    const { send } = show("walk-leader");
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    vi.advanceTimersByTime(100);
    pointer(zone, "pointermove", 900, 100);
    expect(walks(send)).toEqual(["n", "e"]);
  });

  it("a press beside the circle counts too", () => {
    const { send } = show("walk-leader");
    const { zone } = pad();
    pointer(zone, "pointerdown", 330, 100);
    expect(walks(send)).toEqual(["e"]);
  });

  it("reads the pad's place at every move, so a page that shifts under a held thumb doesn't throw it off", () => {
    const { send } = show("walk-leader");
    const { zone, face } = pad();
    pointer(zone, "pointerdown", ...EAST);
    face.getBoundingClientRect = () => box(0, -60);
    vi.advanceTimersByTime(100);
    pointer(zone, "pointermove", 100, 120);     // to the old layout's centre, this is the dead zone
    expect(walks(send)).toEqual(["e", "s"]);
  });

  it("shows the direction on the pad, written straight to the DOM", () => {
    show("walk-leader");
    const { zone, face } = pad();
    pointer(zone, "pointerdown", ...NORTH_EAST);
    expect(face.dataset.dir).toBe("ne");
    expect(face.classList.contains("down")).toBe(true);
    pointer(zone, "pointerup", ...NORTH_EAST);
    expect(face.dataset.dir).toBe("");
    expect(face.classList.contains("down")).toBe(false);
  });

  it("doesn't re-render while held", () => {
    show("walk-leader");
    const { zone } = pad();
    const before = options.diffed;
    let renders = 0;
    options.diffed = (vnode) => {
      renders++;
      before?.(vnode);
    };
    try {
      pointer(zone, "pointerdown", ...NORTH);
      vi.advanceTimersByTime(300);
      pointer(zone, "pointermove", ...EAST);
      vi.advanceTimersByTime(300);
      pointer(zone, "pointerup", ...EAST);
      vi.advanceTimersByTime(300);
    } finally {
      options.diffed = before;
    }
    expect(renders).toBe(0);
  });

  it("ignores a second finger", () => {
    const { send } = show("walk-leader");
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH, 1);
    pointer(zone, "pointerdown", ...EAST, 2);
    pointer(zone, "pointermove", ...EAST, 2);
    pointer(zone, "pointerup", ...EAST, 2);
    pointer(zone, "pointercancel", ...EAST, 2);
    expect(walks(send)).toEqual(["n"]);
    expect(zone.setPointerCapture).toHaveBeenCalledTimes(1);

    pointer(zone, "pointerup", ...NORTH, 1);
    expect(walks(send)).toEqual(["n", null]);
  });

  it.each([
    ["pointercancel", (zone: HTMLElement) => pointer(zone, "pointercancel", ...NORTH)],
    ["lostpointercapture", (zone: HTMLElement) => pointer(zone, "lostpointercapture", ...NORTH)],
    ["blur", () => window.dispatchEvent(new Event("blur"))],
    ["pagehide", () => window.dispatchEvent(new Event("pagehide"))],
    ["visibility hidden", () => {
      vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
      document.dispatchEvent(new Event("visibilitychange"));
    }],
  ])("stops on %s, and the thumb still down steers nothing", (_, happen) => {
    const { send } = show("walk-leader");
    const { zone, face } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    happen(zone);
    expect(walks(send)).toEqual(["n", null]);
    expect(face.classList.contains("down")).toBe(false);

    vi.advanceTimersByTime(150);
    pointer(zone, "pointermove", ...EAST);
    vi.advanceTimersByTime(100);
    expect(walks(send)).toEqual(["n", null, null]);
  });

  it("a blur ends the hold's ping and leaves the idle ping, as the page is still showing", () => {
    const { send } = show("walk-leader");
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    window.dispatchEvent(new Event("blur"));
    vi.advanceTimersByTime(150);
    send.mockClear();
    vi.advanceTimersByTime(3000);
    expect(walks(send)).toEqual([null]);
  });

  it("hidden, the phone goes quiet; back, it's heard at once", () => {
    const { send } = show("walk-follower");
    const state = vi.spyOn(document, "visibilityState", "get").mockReturnValue("hidden");
    document.dispatchEvent(new Event("visibilitychange"));
    vi.advanceTimersByTime(60_000);
    expect(send).not.toHaveBeenCalled();

    state.mockReturnValue("visible");
    document.dispatchEvent(new Event("visibilitychange"));
    expect(walks(send)).toEqual([null]);
  });

  it("prevents the long-press menu, a text selection and a drag", () => {
    show("walk-leader");
    const { zone } = pad();
    for (const type of ["contextmenu", "selectstart", "dragstart"]) {
      const e = new Event(type, { bubbles: true, cancelable: true });
      zone.dispatchEvent(e);
      expect(e.defaultPrevented).toBe(true);
    }
  });

  it("uses the newest send after a new view", () => {
    const first = vi.fn();
    const { rerender } = render(<ControllerScreen view={view("walk-leader")} send={first} me="ALEX" game="tactics" />);
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    expect(first).toHaveBeenCalledTimes(1);

    const second = vi.fn();
    rerender(<ControllerScreen view={view("walk-leader", undefined, "v2")} send={second} me="ALEX" game="tactics" />);
    vi.advanceTimersByTime(1000);
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalled();
    expect(walks(second).every((d) => d === "n")).toBe(true);
  });

  it("resends its state on a new view, held or not", () => {
    const first = vi.fn();
    const { rerender } = render(<ControllerScreen view={view("walk-leader")} send={first} me="ALEX" game="tactics" />);
    const { zone } = pad();
    const second = vi.fn();
    rerender(<ControllerScreen view={view("walk-leader", undefined, "v2")} send={second} me="ALEX" game="tactics" />);
    expect(walks(second)).toEqual([null]);

    pointer(zone, "pointerdown", ...NORTH);
    const third = vi.fn();
    rerender(<ControllerScreen view={view("walk-leader", undefined, "v3")} send={third} me="ALEX" game="tactics" />);
    expect(walks(third)).toEqual(["n"]);
  });

  it("the same view again sends nothing", () => {
    const send = vi.fn();
    const { rerender } = render(<ControllerScreen view={view("walk-leader")} send={send} me="ALEX" game="tactics" />);
    rerender(<ControllerScreen view={view("walk-leader")} send={send} me="ALEX" game="tactics" />);
    expect(send).not.toHaveBeenCalled();
  });

  it("losing the lead under a held thumb stops, and takes the pad away", () => {
    const send = vi.fn();
    const { rerender } = render(<ControllerScreen view={view("walk-leader")} send={send} me="ALEX" game="tactics" />);
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    rerender(<ControllerScreen view={view("walk-follower", undefined, "v2")} send={send} me="ALEX" game="tactics" />);
    expect(document.querySelector(".tx-pad")).toBeNull();
    expect(walks(send).at(-1)).toBeNull();
    send.mockClear();
    vi.advanceTimersByTime(1000);
    expect(walks(send).filter((d) => d !== null)).toEqual([]);
  });

  it("leaving the walk screen stops every timer", () => {
    const send = vi.fn();
    const { rerender } = render(<ControllerScreen view={view("walk-leader")} send={send} me="ALEX" game="tactics" />);
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    rerender(<ControllerScreen view={view("pickEncounter", undefined, "v2")} send={send} me="ALEX" game="tactics" />);
    send.mockClear();
    vi.advanceTimersByTime(10_000);
    expect(send).not.toHaveBeenCalled();
  });

  it("key repeat sends nothing extra, and two arrows make a diagonal", () => {
    const { send } = show("walk-leader");
    const key = (type: string, k: string, repeat = false) =>
      window.dispatchEvent(new KeyboardEvent(type, { key: k, repeat, bubbles: true, cancelable: true }));
    key("keydown", "ArrowUp");
    key("keydown", "ArrowUp", true);
    key("keydown", "ArrowUp", true);
    expect(walks(send)).toEqual(["n"]);

    vi.advanceTimersByTime(100);
    key("keydown", "ArrowRight");
    expect(walks(send)).toEqual(["n", "ne"]);
    vi.advanceTimersByTime(100);
    key("keyup", "ArrowUp");
    expect(walks(send)).toEqual(["n", "ne", "e"]);
    key("keyup", "ArrowRight");
    expect(walks(send)).toEqual(["n", "ne", "e", null]);
  });
});

describe("the walk screen", () => {
  it("every walk view pings slowly, so a silenced phone can be heard again", () => {
    const { send } = show("walk-follower");
    vi.advanceTimersByTime(3000);
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenLastCalledWith(walk(null));
    vi.advanceTimersByTime(3000);
    expect(send).toHaveBeenCalledTimes(2);
  });

  it("doesn't play the tap cue on a ping, and does on a button", () => {
    const feedback = quietFeedback();
    const onSend = vi.spyOn(feedback, "onSend");
    const send = vi.fn();
    render(
      <FeedbackContext.Provider value={feedback}>
        <ControllerScreen view={view("walk-leader")} send={send} me="ALEX" game="tactics" />
      </FeedbackContext.Provider>,
    );
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    vi.advanceTimersByTime(1000);
    pointer(zone, "pointerup", ...NORTH);
    vi.advanceTimersByTime(4000);
    expect(send.mock.calls.length).toBeGreaterThan(4);
    expect(onSend).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "End session" }));
    fireEvent.click(screen.getByRole("button", { name: "Yes, end it" }));
    expect(onSend).toHaveBeenCalledTimes(1);
  });

  it("vibrates on press, differently on a change of direction, and not at all muted", () => {
    const vibrate = vi.fn(() => true);
    vi.stubGlobal("navigator", { ...navigator, vibrate });
    const feedback = quietFeedback();
    render(
      <FeedbackContext.Provider value={feedback}>
        <ControllerScreen view={view("walk-leader")} send={vi.fn()} me="ALEX" game="tactics" />
      </FeedbackContext.Provider>,
    );
    const { zone } = pad();
    pointer(zone, "pointerdown", ...NORTH);
    expect(vibrate).toHaveBeenCalledTimes(1);
    const press = (vibrate.mock.calls[0] as unknown[])[0];
    expect(press).toBe(8);

    vi.advanceTimersByTime(100);
    pointer(zone, "pointermove", ...EAST);
    expect(vibrate).toHaveBeenCalledTimes(2);
    expect((vibrate.mock.calls[1] as unknown[])[0]).not.toEqual(press);

    pointer(zone, "pointerup", ...EAST);
    feedback.setMuted(true);
    vibrate.mockClear();
    pointer(zone, "pointerdown", ...NORTH);
    expect(vibrate).not.toHaveBeenCalled();
  });

  it("a follower sees who leads, the objective, their character and the party", () => {
    show("walk-follower");
    const text = document.querySelector(".tx-walk")!.textContent!;
    expect(text).toContain("Player1 leads");
    expect(text).toContain("Explore the region");
    expect(text).toContain("Cleric");
    for (const name of ["Player1", "Player2", "Player3", "Player4"]) expect(text).toContain(name);
    expect(document.querySelectorAll(".tx-walk-party .tx-crown")).toHaveLength(1);
    expect(document.querySelector(".tx-walk-party .leads")!.textContent).toContain("Player1");
  });

  it("says someone is away", () => {
    show("walk-follower", (d) => {
      (d.party as { here: boolean }[])[2]!.here = false;
    });
    expect(document.querySelector(".tx-walk-party .away")!.textContent).toContain("Player3");
  });

  it("copes with nobody leading", () => {
    show("walk-follower", (d) => {
      d.leaderName = null;
      for (const m of d.party as { leads: boolean }[]) m.leads = false;
    });
    expect(document.querySelector(".tx-walk")!.textContent).toContain("The party waits for someone to lead it");
  });

  it("opening as leader buzzes and flags it, as nobody has told this phone yet; a reconnect a moment later doesn't", () => {
    const vibrate = vi.fn(() => true);
    vi.stubGlobal("navigator", { ...navigator, vibrate });
    vi.setSystemTime(Date.now() + 60_000);      // clear of any alert an earlier test played
    const feedback = quietFeedback();
    const shown = (v: CustomView) => (
      <FeedbackContext.Provider value={feedback}>
        <ControllerScreen view={v} send={vi.fn()} me="ALEX" game="tactics" />
      </FeedbackContext.Provider>
    );
    const opened = render(shown(view("walk-leader")));
    vi.advanceTimersByTime(10);
    expect(vibrate).toHaveBeenCalledTimes(1);
    expect(vibrate).toHaveBeenLastCalledWith([60, 80, 120]);
    expect(document.querySelector(".tx-walk.arrived")).not.toBeNull();
    opened.rerender(shown(view("walk-leader", undefined, "v2")));
    expect(vibrate).toHaveBeenCalledTimes(1);
    opened.unmount();

    render(shown(view("walk-leader", undefined, "v3")));     // the same view again after a reconnect
    vi.advanceTimersByTime(10);
    expect(vibrate).toHaveBeenCalledTimes(1);
    expect(document.querySelector(".tx-walk")).not.toBeNull();
    expect(document.querySelector(".tx-walk.arrived")).toBeNull();       // nor does the pulse replay
  });

  it("opening as a follower says nothing", () => {
    const vibrate = vi.fn(() => true);
    vi.stubGlobal("navigator", { ...navigator, vibrate });
    vi.setSystemTime(Date.now() + 60_000);
    render(
      <FeedbackContext.Provider value={quietFeedback()}>
        <ControllerScreen view={view("walk-follower")} send={vi.fn()} me="ALEX" game="tactics" />
      </FeedbackContext.Provider>,
    );
    vi.advanceTimersByTime(10);
    expect(vibrate).not.toHaveBeenCalled();
    expect(document.querySelector(".tx-walk.arrived")).toBeNull();
  });

  it("becoming leader buzzes and flags the change, once", () => {
    const vibrate = vi.fn(() => true);
    vi.stubGlobal("navigator", { ...navigator, vibrate });
    vi.setSystemTime(Date.now() + 60_000);
    const feedback = quietFeedback();
    const shown = (v: CustomView) => (
      <FeedbackContext.Provider value={feedback}>
        <ControllerScreen view={v} send={vi.fn()} me="ALEX" game="tactics" />
      </FeedbackContext.Provider>
    );
    const { rerender } = render(shown(view("walk-follower")));
    expect(document.querySelector(".tx-walk.arrived")).toBeNull();
    rerender(shown(view("walk-leader", undefined, "v2")));
    expect(vibrate).toHaveBeenCalledTimes(1);
    expect(document.querySelector(".tx-walk.arrived")).not.toBeNull();
    rerender(shown(view("walk-leader", undefined, "v3")));
    expect(vibrate).toHaveBeenCalledTimes(1);
    rerender(shown(view("walk-follower", undefined, "v4")));
    expect(document.querySelector(".tx-walk.arrived")).toBeNull();
  });

  it("says under the banner what to do: the leader walks, everyone watches the TV", () => {
    show("walk-leader");
    expect(document.querySelector(".tx-walk-who + .tx-walk-hint")!.textContent).toBe("Hold a direction to walk. Watch the TV.");
    cleanup();
    show("walk-follower");
    expect(document.querySelector(".tx-walk-who + .tx-walk-hint")!.textContent).toBe("Player1 steers. Watch the TV.");
    cleanup();
    show("walk-follower", (d) => {
      d.leaderName = null;
    });
    expect(document.querySelector(".tx-walk-hint")).toBeNull();
  });

  it("the VIP can go to the battle list, after a confirm; nobody else sees it", () => {
    const { send } = show("walk-leader");
    fireEvent.click(screen.getByRole("button", { name: "Choose a battle" }));
    expect(send).not.toHaveBeenCalledWith({ action: "battles" });
    fireEvent.click(screen.getByRole("button", { name: "Yes, choose one" }));
    expect(send).toHaveBeenLastCalledWith({ action: "battles" });
    cleanup();
    show("walk-follower");
    expect(screen.queryByRole("button", { name: "Choose a battle" })).toBeNull();
    expect(screen.queryByRole("button", { name: "End session" })).toBeNull();
  });

  it("no battles, no button", () => {
    show("walk-leader", (d) => {
      d.canBattle = false;
    });
    expect(screen.queryByRole("button", { name: "Choose a battle" })).toBeNull();
    expect(screen.getByRole("button", { name: "End session" })).toBeTruthy();
  });

  it("End session needs a confirm, in a different place from the first tap", () => {
    const { send } = show("walk-leader");
    const buttons = () => [...document.querySelectorAll<HTMLButtonElement>(".tx-walk-host button")];
    const first = screen.getByRole("button", { name: "End session" });
    const place = buttons().indexOf(first as HTMLButtonElement);
    fireEvent.click(first);
    expect(send).not.toHaveBeenCalled();

    const sure = screen.getByRole("button", { name: "Yes, end it" });
    expect(buttons().indexOf(sure as HTMLButtonElement)).not.toBe(place);
    fireEvent.click(buttons()[place]!);        // a double tap lands on "Keep walking"
    expect(send).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Yes, end it" })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "End session" }));
    fireEvent.click(screen.getByRole("button", { name: "Yes, end it" }));
    expect(send).toHaveBeenLastCalledWith({ action: "endSession" });
  });

  it("Choose a battle needs a confirm too, and \"Keep walking\" takes the place of the first tap", () => {
    const { send } = show("walk-leader");
    const buttons = () => [...document.querySelectorAll<HTMLButtonElement>(".tx-walk-host button")];
    const first = screen.getByRole("button", { name: "Choose a battle" });
    const place = buttons().indexOf(first as HTMLButtonElement);
    fireEvent.click(first);
    expect(screen.getByRole("group", { name: "Stop walking to choose a battle?" })).toBeTruthy();

    const sure = screen.getByRole("button", { name: "Yes, choose one" });
    expect(buttons().indexOf(sure as HTMLButtonElement)).not.toBe(place);
    expect(buttons()[place]!.textContent).toBe("Keep walking");
    fireEvent.click(buttons()[place]!);        // a double tap lands on "Keep walking"
    expect(screen.queryByRole("button", { name: "Yes, choose one" })).toBeNull();
    expect(screen.getByRole("button", { name: "Choose a battle" })).toBeTruthy();
    expect(send.mock.calls.some((c) => (c[0] as { action: string }).action !== "walk")).toBe(false);   // only pings went out
  });

  it("a battle confirm goes when the battles do, and never sends battles", () => {
    const send = vi.fn();
    const { rerender } = render(<ControllerScreen view={view("walk-leader")} send={send} me="ALEX" game="tactics" />);
    fireEvent.click(screen.getByRole("button", { name: "Choose a battle" }));
    expect(screen.getByRole("button", { name: "Yes, choose one" })).toBeTruthy();

    rerender(<ControllerScreen view={view("walk-leader", (d) => { d.canBattle = false; }, "v2")} send={send} me="ALEX" game="tactics" />);
    expect(screen.queryByRole("button", { name: "Yes, choose one" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Keep walking" })).toBeNull();
    expect(screen.getByRole("button", { name: "End session" })).toBeTruthy();

    rerender(<ControllerScreen view={view("walk-leader", undefined, "v3")} send={send} me="ALEX" game="tactics" />);
    expect(screen.queryByRole("button", { name: "Yes, choose one" })).toBeNull();      // and doesn't come back with them
    expect(send.mock.calls.some((c) => (c[0] as { action: string }).action === "battles")).toBe(false);
  });

  it.each([["End session", "Yes, end it"], ["Choose a battle", "Yes, choose one"]])("the %s confirm lapses by itself", async (ask, yes) => {
    show("walk-leader");
    fireEvent.click(screen.getByRole("button", { name: ask }));
    await vi.advanceTimersByTimeAsync(5000);
    expect(screen.queryByRole("button", { name: yes })).toBeNull();
  });

  it("the VIP's buttons sleep while a thumb is on the pad", () => {
    show("walk-leader");
    const { zone } = pad();
    const root = document.querySelector(".tx-walk")!;
    pointer(zone, "pointerdown", ...NORTH);
    expect(root.classList.contains("held")).toBe(true);
    pointer(zone, "pointerup", ...NORTH);
    expect(root.classList.contains("held")).toBe(false);
  });

  it("links to the portal", () => {
    show("walk-follower");
    expect(document.querySelector<HTMLAnchorElement>(".tx-walk a")!.getAttribute("href")).toBe("/c/ABCD");
  });
});

describe("the battle list, from the world", () => {
  it("goes back to walking", () => {
    const { send } = show("pickEncounter");
    fireEvent.click(screen.getByRole("button", { name: "Back to walking" }));
    expect(send).toHaveBeenLastCalledWith({ action: "backToWalk" });
  });
});
