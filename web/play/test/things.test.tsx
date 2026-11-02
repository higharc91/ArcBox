import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

// Real views from a real session (the vault's chest and key in reach), written by
// games/Tactics/Tactics.Game.Tests/PhoneFixtureTests.cs.
const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/tactics/*.json", { eager: true });
const view = (name: string, change?: (data: Record<string, unknown>) => void, viewId = "v1"): CustomView => {
  const mod = fixtures[`./fixtures/tactics/${name}.json`];
  if (!mod) throw new Error(`missing fixture tactics/${name}`);
  const v = structuredClone(mod.default) as CustomView;
  change?.(v.data as Record<string, unknown>);
  return { ...v, viewId };
};

function show(name: string, change?: (data: Record<string, unknown>) => void) {
  const send = vi.fn();
  const utils = render(<ControllerScreen view={view(name, change)} send={send} me="Player1" game="tactics" />);
  const again = (next: CustomView) => utils.rerender(<ControllerScreen view={next} send={send} me="Player1" game="tactics" />);
  return { send, again, ...utils };
}

const interacts = (send: ReturnType<typeof vi.fn>) =>
  send.mock.calls.map((c) => c[0] as { action: string; data?: unknown }).filter((c) => c.action === "interact");

/** A beat's word on the button, with the vault's chest in reach as well. */
const beatAndChest = (d: Record<string, unknown>) => {
  d.interact = "Talk";
  d.beat = true;
  d.targets = [{ id: "chest", label: "Open the chest" }];
};

beforeAll(async () => {
  await loadModule("tactics");
});
beforeEach(() => {
  vi.useFakeTimers();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("things to interact with, on the walk screen", () => {
  it("the button says what the nearest thing does, and uses that thing", () => {
    const { send } = show("walk-things");
    fireEvent.click(screen.getByRole("button", { name: "Pick up the iron key" }));
    expect(interacts(send)).toEqual([{ action: "interact", data: { target: "key" } }]);
  });

  it("with more than one in reach, a picker lists each, and a pick uses that one and closes", () => {
    const { send } = show("walk-things");
    const more = screen.getByRole("button", { name: "2 things in reach: choose one" });
    expect(more.getAttribute("aria-expanded")).toBe("false");
    fireEvent.click(more);

    const list = screen.getByRole("group", { name: "What to use" });
    const options = within(list).getAllByRole("button").map((b) => b.textContent);
    expect(options).toEqual(["Pick up the iron key", "Open the chest", "Back"]);
    fireEvent.click(within(list).getByRole("button", { name: "Open the chest" }));

    expect(interacts(send)).toEqual([{ action: "interact", data: { target: "chest" } }]);
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();
  });

  it("a beat in reach keeps its button, and the picker offers it beside the things", () => {
    const { send } = show("walk-things", beatAndChest);
    fireEvent.click(screen.getByRole("button", { name: "Talk" }));
    expect(interacts(send)).toEqual([{ action: "interact" }]);     // no target: the server does the beat

    cleanup();
    const second = show("walk-things", beatAndChest);
    fireEvent.click(screen.getByRole("button", { name: "2 things in reach: choose one" }));
    const list = screen.getByRole("group", { name: "What to use" });
    expect(within(list).getAllByRole("button").map((b) => b.textContent)).toEqual(["Talk", "Open the chest", "Back"]);
    fireEvent.click(within(list).getByRole("button", { name: "Talk" }));
    expect(interacts(second.send)).toEqual([{ action: "interact" }]);
  });

  it("the server says which the button is: a beat whose word matches a thing's is still the beat's, and a thing's is never taken for one", () => {
    const same = show("walk-things", (d) => {
      d.interact = "Open the chest";
      d.beat = true;
      d.targets = [{ id: "chest", label: "Open the chest" }];
    });
    fireEvent.click(screen.getByRole("button", { name: "Open the chest" }));
    expect(interacts(same.send)).toEqual([{ action: "interact" }]);          // the beat, though its word is the chest's too
    expect(screen.getByRole("button", { name: "2 things in reach: choose one" })).toBeTruthy();

    cleanup();
    const thing = show("walk-things", (d) => { d.interact = "Talk"; d.beat = false; d.targets = [{ id: "talker", label: "Talk" }]; });
    fireEvent.click(screen.getByRole("button", { name: "Talk" }));
    expect(interacts(thing.send)).toEqual([{ action: "interact", data: { target: "talker" } }]);
  });

  it("one thing in reach has no picker", () => {
    show("walk-things", (d) => {
      d.interact = "Open the chest";
      d.targets = [{ id: "chest", label: "Open the chest" }];
    });
    expect(screen.getByRole("button", { name: "Open the chest" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /choose one/ })).toBeNull();
  });

  it("nothing for a follower, whose list is empty", () => {
    show("walk-things-follower");
    expect(screen.queryByRole("button", { name: /Pick up|Open|choose one/ })).toBeNull();
  });

  it("a server that sends no list keeps the old button, which names no target", () => {
    const { send } = show("walk-interact");
    expect(screen.queryByRole("button", { name: /choose one/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Talk" }));
    expect(interacts(send)).toEqual([{ action: "interact" }]);
  });

  it("opening the picker moves focus into it, and Back or Escape closes it and gives focus back", () => {
    show("walk-things");
    const more = screen.getByRole("button", { name: "2 things in reach: choose one" });
    more.focus();
    fireEvent.click(more);
    expect(document.activeElement?.textContent).toBe("Pick up the iron key");
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Pick up the iron key" }));

    fireEvent.click(screen.getByRole("button", { name: "2 things in reach: choose one" }));
    fireEvent.keyDown(screen.getByRole("group", { name: "What to use" }), { key: "Escape" });
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();
  });

  it("a pick sends once for a double tap, and wakes again if nothing came of it", async () => {
    const { send } = show("walk-things");
    const button = screen.getByRole("button", { name: "Pick up the iron key" });
    act(() => {
      button.click();
      button.click();
    });
    expect(interacts(send)).toHaveLength(1);
    expect(button.getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(screen.getByRole("button", { name: "2 things in reach: choose one" }));
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();    // one use per view: the list waits too
    expect(interacts(send)).toHaveLength(1);

    await vi.advanceTimersByTimeAsync(2500);
    fireEvent.click(screen.getByRole("button", { name: "Pick up the iron key" }));
    expect(interacts(send)).toHaveLength(2);
  });

  it("the picker follows the list as things come and go, and closes when one is left", () => {
    const { again } = show("walk-things");
    fireEvent.click(screen.getByRole("button", { name: "2 things in reach: choose one" }));
    again(view("walk-things", (d) => {
      d.targets = [...(d.targets as object[]), { id: "lever", label: "Pull the lever" }];
    }, "v2"));
    const list = screen.getByRole("group", { name: "What to use" });
    expect(within(list).getAllByRole("button").map((b) => b.textContent)).toContain("Pull the lever");

    again(view("walk-things", (d) => {
      d.interact = "Open the chest";
      d.targets = [{ id: "chest", label: "Open the chest" }];
    }, "v3"));
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();
    expect(screen.getByRole("button", { name: "Open the chest" })).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Open the chest" }));    // not lost to the page
  });

  it("closes by itself after a while, so a forgotten list doesn't sit on the screen, and the focus goes back to the button", () => {
    show("walk-things");
    fireEvent.click(screen.getByRole("button", { name: "2 things in reach: choose one" }));
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Pick up the iron key" }));
  });

  it("any touch in the list starts its wait again", () => {
    show("walk-things");
    fireEvent.click(screen.getByRole("button", { name: "2 things in reach: choose one" }));
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    fireEvent.pointerDown(screen.getByRole("button", { name: "Open the chest" }));
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    expect(screen.getByRole("group", { name: "What to use" })).toBeTruthy();
    fireEvent.keyDown(screen.getByRole("group", { name: "What to use" }), { key: "ArrowDown" });
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    expect(screen.getByRole("group", { name: "What to use" })).toBeTruthy();
    act(() => {
      vi.advanceTimersByTime(3_000);
    });
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();
  });

  it("the list button names the list it opens, and sleeps with the rest while a use is on its way", () => {
    show("walk-things");
    const more = screen.getByRole("button", { name: "2 things in reach: choose one" });
    expect(more.getAttribute("aria-disabled")).toBe("false");
    fireEvent.click(more);
    const list = screen.getByRole("group", { name: "What to use" });
    expect(more.getAttribute("aria-controls")).toBe(list.id);
    expect(list.id).not.toBe("");
    fireEvent.click(within(list).getByRole("button", { name: "Open the chest" }));
    expect(more.getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(more);
    expect(screen.queryByRole("group", { name: "What to use" })).toBeNull();      // asleep: nothing to choose until it wakes
  });
});
