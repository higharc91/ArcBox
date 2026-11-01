import { describe, expect, it, vi } from "vitest";
import { dirAt, stickyDirAt, WalkSender, DEAD_ZONE, IDLE_PING_MS, type Dir, type WalkClock } from "../src/games/tactics/walkModel";

/** A clock a test moves by hand. */
class FakeClock implements WalkClock {
  time = 0;
  private timers: { at: number; fn: () => void }[] = [];
  now = () => this.time;
  after = (ms: number, fn: () => void) => {
    const timer = { at: this.time + ms, fn };
    this.timers.push(timer);
    return () => {
      this.timers = this.timers.filter((t) => t !== timer);
    };
  };
  advance(ms: number) {
    const end = this.time + ms;
    for (;;) {
      const next = this.timers.filter((t) => t.at <= end).sort((a, b) => a.at - b.at)[0];
      if (!next) break;
      this.timers = this.timers.filter((t) => t !== next);
      this.time = next.at;
      next.fn();
    }
    this.time = end;
  }
  get pending() {
    return this.timers.length;
  }
}

function rig() {
  const clock = new FakeClock();
  const sent: (Dir | null)[] = [];
  const send = vi.fn((dir: Dir | null) => void sent.push(dir));
  const sender = new WalkSender(send, clock);
  return { clock, sent, send, sender };
}

const at = (deg: number, r = 1): [number, number] => [r * Math.sin((deg * Math.PI) / 180), -r * Math.cos((deg * Math.PI) / 180)];

const DIRS: Dir[] = ["n", "ne", "e", "se", "s", "sw", "w", "nw"];

describe("dirAt", () => {
  it("reads the eight screen directions, up being north", () => {
    DIRS.forEach((dir, i) => expect(dirAt(...at(i * 45), DEAD_ZONE)).toBe(dir));
  });

  it("gives every sector the same 45 degrees", () => {
    DIRS.forEach((dir, i) => {
      expect(dirAt(...at(i * 45 - 22.4), DEAD_ZONE)).toBe(dir);
      expect(dirAt(...at(i * 45 + 22.4), DEAD_ZONE)).toBe(dir);
    });
  });

  it("has a dead zone in the middle", () => {
    expect(DEAD_ZONE).toBe(0.18);
    expect(dirAt(0, 0, DEAD_ZONE)).toBeNull();
    expect(dirAt(...at(90, DEAD_ZONE - 0.01), DEAD_ZONE)).toBeNull();
    expect(dirAt(...at(90, DEAD_ZONE + 0.01), DEAD_ZONE)).toBe("e");
  });

  it("goes by angle alone outside the pad", () => {
    expect(dirAt(...at(45, 5), DEAD_ZONE)).toBe("ne");
  });
});

describe("stickyDirAt", () => {
  it("holds a direction across a sector's edge, so a resting thumb doesn't flicker", () => {
    expect(stickyDirAt(...at(24), DEAD_ZONE, "n")).toBe("n");
    expect(stickyDirAt(...at(21), DEAD_ZONE, "ne")).toBe("ne");
    expect(dirAt(...at(24), DEAD_ZONE)).toBe("ne");
  });

  it("lets go once the thumb has really moved", () => {
    expect(stickyDirAt(...at(35), DEAD_ZONE, "n")).toBe("ne");
    expect(stickyDirAt(...at(180), DEAD_ZONE, "n")).toBe("s");
  });

  it("wraps around north", () => {
    expect(stickyDirAt(...at(338), DEAD_ZONE, "n")).toBe("n");
    expect(stickyDirAt(...at(24), DEAD_ZONE, "nw")).toBe("ne");
  });

  it("holds a direction a little way into the dead zone, and not to the middle", () => {
    expect(stickyDirAt(...at(90, DEAD_ZONE - 0.02), DEAD_ZONE, "e")).toBe("e");
    expect(stickyDirAt(...at(90, DEAD_ZONE / 2), DEAD_ZONE, "e")).toBeNull();
    expect(stickyDirAt(...at(90, DEAD_ZONE - 0.02), DEAD_ZONE, null)).toBeNull();
  });
});

describe("WalkSender", () => {
  it("sends a direction at once on press", () => {
    const { sender, sent } = rig();
    sender.press("ne");
    expect(sent).toEqual(["ne"]);
  });

  it("pings every 250 ms while held", () => {
    const { sender, sent, clock } = rig();
    sender.press("e");
    clock.advance(1000);
    expect(sent).toEqual(["e", "e", "e", "e", "e"]);
  });

  it("sends a stop at once, and again 150 ms later", () => {
    const { sender, sent, clock } = rig();
    sender.press("e");
    sent.length = 0;
    sender.release();
    expect(sent).toEqual([null]);
    clock.advance(149);
    expect(sent).toEqual([null]);
    clock.advance(1);
    expect(sent).toEqual([null, null]);
  });

  it("a press takes back the retry stop", () => {
    const { sender, sent, clock } = rig();
    sender.press("e");
    sender.release();
    clock.advance(100);
    sender.press("w");
    clock.advance(100);
    expect(sent).toEqual(["e", null, "w"]);
  });

  it("a slid thumb changes direction without a stop", () => {
    const { sender, sent, clock } = rig();
    sender.press("n");
    clock.advance(100);
    sender.press("ne");
    expect(sent).toEqual(["n", "ne"]);
  });

  it("coalesces fast direction changes but never a stop", () => {
    const { sender, sent, clock } = rig();
    sender.press("n");
    clock.advance(10);
    sender.press("ne");
    clock.advance(10);
    sender.press("e");
    expect(sent).toEqual(["n"]);
    clock.advance(46);
    expect(sent).toEqual(["n", "e"]);

    clock.advance(10);
    sender.press("s");
    sender.release();
    expect(sent).toEqual(["n", "e", null]);
    clock.advance(150);
    expect(sent).toEqual(["n", "e", null, null]);     // the "s" never went: the stop overtook it
  });

  it("a repeat of the held direction sends nothing", () => {
    const { sender, sent } = rig();
    sender.press("n");
    sender.press("n");
    sender.press("n");
    expect(sent).toEqual(["n"]);
  });

  it("pings every 3 s while idle", () => {
    const { sent, clock } = rig();
    expect(IDLE_PING_MS).toBe(3000);
    clock.advance(9000);
    expect(sent).toEqual([null, null, null]);
  });

  it("goes back to the idle ping after a stop", () => {
    const { sender, sent, clock } = rig();
    sender.press("n");
    sender.release();
    clock.advance(150);
    sent.length = 0;
    clock.advance(2999);
    expect(sent).toEqual([]);
    clock.advance(1);
    expect(sent).toEqual([null]);
  });

  it("resends its state on a new view", () => {
    const { sender, sent } = rig();
    sender.newView();
    expect(sent).toEqual([null]);
    sender.press("sw");
    sender.newView();
    expect(sent).toEqual([null, "sw", "sw"]);
  });

  it("a release with nothing held sends nothing", () => {
    const { sender, sent } = rig();
    sender.release();
    expect(sent).toEqual([]);
  });

  it("asleep it stops at once and pings no more, and on waking is heard at once", () => {
    const { sender, sent, clock } = rig();
    sender.press("n");
    sent.length = 0;
    sender.sleep();
    clock.advance(150);
    expect(sent).toEqual([null, null]);     // the stop and its retry
    clock.advance(60_000);
    expect(sent).toEqual([null, null]);

    sender.wake();
    expect(sent).toEqual([null, null, null]);
    clock.advance(3000);
    expect(sent).toEqual([null, null, null, null]);
  });

  it("sleeping idle sends nothing, and a press wakes it", () => {
    const { sender, sent, clock } = rig();
    sender.sleep();
    clock.advance(10_000);
    expect(sent).toEqual([]);
    sender.press("n");
    expect(sent).toEqual(["n"]);
    clock.advance(250);
    expect(sent).toEqual(["n", "n"]);
  });

  it("disposed, it stops what it held, leaves no timer and sends nothing more", () => {
    const { sender, sent, clock } = rig();
    sender.press("n");
    sent.length = 0;
    sender.dispose();
    expect(sent).toEqual([null]);
    expect(clock.pending).toBe(0);
    sender.press("e");
    sender.newView();
    clock.advance(10_000);
    expect(sent).toEqual([null]);
  });
});
