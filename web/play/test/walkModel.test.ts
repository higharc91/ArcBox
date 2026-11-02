import { describe, expect, it, vi } from "vitest";
import {
  dirAt, stickyDirAt, WalkSender, DEAD_ZONE, IDLE_PING_MS, ROAD_BEARING, SECTORS, STICK_DEGREES, type Dir, type WalkClock,
} from "../src/games/tactics/walkModel";

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

/** Each sector boundary clockwise from up, with the sector before and after it. */
const BOUNDARIES: [number, Dir, Dir][] = [
  [30, "n", "ne"], [78, "ne", "e"], [102, "e", "se"], [150, "se", "s"],
  [210, "s", "sw"], [258, "sw", "w"], [282, "w", "nw"], [330, "nw", "n"],
];
const ROADS: [number, Dir][] = [[ROAD_BEARING, "ne"], [180 - ROAD_BEARING, "se"], [180 + ROAD_BEARING, "sw"], [360 - ROAD_BEARING, "nw"]];

/** A seeded generator, so a failing model run can be replayed. */
function random(seed: number) {
  return () => {
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** How far `deg` is outside `dir`'s sector, in degrees (0 inside). */
function past(dir: Dir, deg: number) {
  const s = SECTORS.find((x) => x.dir === dir)!;
  const mid = (s.from + s.to) / 2;
  const off = Math.abs(((((deg - mid) % 360) + 540) % 360) - 180);
  return Math.max(0, off - (s.to - s.from) / 2);
}

/** Thumb moves in the walk-pad model run. */
const WALK_MODEL_EVENTS = 20_000;

describe("dirAt", () => {
  it("reads the eight screen directions, up being north, and a road's bearing as its direction", () => {
    expect(ROAD_BEARING).toBeCloseTo((Math.atan2(2, 1) * 180) / Math.PI, 10);
    expect(ROAD_BEARING).toBeCloseTo(63.43, 2);
    for (const [deg, dir] of [[0, "n"], [90, "e"], [180, "s"], [270, "w"], ...ROADS] as [number, Dir][]) {
      expect(dirAt(...at(deg), DEAD_ZONE)).toBe(dir);
    }
  });

  it("n and s are 60 degrees wide, the road directions 48, e and w 24, mirrored in every quadrant", () => {
    const width = Object.fromEntries(SECTORS.map((s) => [s.dir, s.to - s.from]));
    expect(width).toEqual({ n: 60, ne: 48, e: 24, se: 48, s: 60, sw: 48, w: 24, nw: 48 });
    expect(SECTORS.map((s) => s.dir)).toEqual(DIRS);
    SECTORS.forEach((s, i) => expect(Math.abs((SECTORS[(i + 1) % 8]!.from - s.to) % 360)).toBe(0));   // they tile the circle
  });

  it.each(BOUNDARIES)("changes sector at %s degrees, from %s to %s", (deg, before, after) => {
    expect(dirAt(...at(deg - 0.05), DEAD_ZONE)).toBe(before);
    expect(dirAt(...at(deg + 0.05), DEAD_ZONE)).toBe(after);
  });

  it("points each chevron where its direction goes: the road directions on a road's bearing", () => {
    expect(Object.fromEntries(SECTORS.map((s) => [s.dir, +s.aim.toFixed(1)]))).toEqual(
      { n: 0, ne: 63.4, e: 90, se: 116.6, s: 180, sw: 243.4, w: 270, nw: 296.6 });
    for (const s of SECTORS) expect(dirAt(...at(s.aim), DEAD_ZONE)).toBe(s.dir);
  });

  it.each(ROADS)("a thumb within 10 degrees of a road's bearing (%s) goes along the road, %s", (deg, dir) => {
    for (let off = -10; off <= 10; off += 0.5) {
      expect(dirAt(...at(deg + off), DEAD_ZONE)).toBe(dir);
      for (const held of [null, ...DIRS]) {
        // The one exception is hysteresis itself: a thumb sliding round from e or w keeps it to STICK_DEGREES past e's edge.
        if ((held === "e" || held === "w") && past(held, deg + off) <= STICK_DEGREES) continue;
        expect(stickyDirAt(...at(deg + off), DEAD_ZONE, held)).toBe(dir);
      }
    }
  });

  it("has a dead zone in the middle", () => {
    expect(DEAD_ZONE).toBe(0.18);
    expect(dirAt(0, 0, DEAD_ZONE)).toBeNull();
    expect(dirAt(...at(90, DEAD_ZONE - 0.01), DEAD_ZONE)).toBeNull();
    expect(dirAt(...at(90, DEAD_ZONE + 0.01), DEAD_ZONE)).toBe("e");
  });

  it("goes by angle alone outside the pad", () => {
    expect(dirAt(...at(ROAD_BEARING, 5), DEAD_ZONE)).toBe("ne");
  });

  it("reads two arrow keys as the screen diagonal's sector", () => {
    expect([dirAt(1, -1, 0), dirAt(1, 1, 0), dirAt(-1, 1, 0), dirAt(-1, -1, 0)]).toEqual(["ne", "se", "sw", "nw"]);
  });
});

describe("stickyDirAt", () => {
  it("keeps STICK_DEGREES at 6", () => {
    expect(STICK_DEGREES).toBe(6);
  });

  it.each(BOUNDARIES)("at %s degrees, holds %s and %s each 6 degrees past the edge, and no further", (deg, before, after) => {
    expect(stickyDirAt(...at(deg + STICK_DEGREES - 0.1), DEAD_ZONE, before)).toBe(before);
    expect(stickyDirAt(...at(deg + STICK_DEGREES + 0.1), DEAD_ZONE, before)).toBe(after);
    expect(stickyDirAt(...at(deg - STICK_DEGREES + 0.1), DEAD_ZONE, after)).toBe(after);
    expect(stickyDirAt(...at(deg - STICK_DEGREES - 0.1), DEAD_ZONE, after)).toBe(before);
    expect(stickyDirAt(...at(deg + 0.05), DEAD_ZONE, null)).toBe(after);
  });

  it("lets go once the thumb has really moved", () => {
    expect(stickyDirAt(...at(40), DEAD_ZONE, "n")).toBe("ne");
    expect(stickyDirAt(...at(180), DEAD_ZONE, "n")).toBe("s");
  });

  it("wraps around north", () => {
    expect(stickyDirAt(...at(325), DEAD_ZONE, "n")).toBe("n");
    expect(stickyDirAt(...at(35), DEAD_ZONE, "nw")).toBe("ne");
  });

  it("holds a direction a little way into the dead zone, and not to the middle", () => {
    expect(stickyDirAt(...at(90, DEAD_ZONE - 0.02), DEAD_ZONE, "e")).toBe("e");
    expect(stickyDirAt(...at(90, DEAD_ZONE / 2), DEAD_ZONE, "e")).toBeNull();
    expect(stickyDirAt(...at(90, DEAD_ZONE - 0.02), DEAD_ZONE, null)).toBeNull();
  });

  it(`model: ${WALK_MODEL_EVENTS} random thumb moves keep the pad's rules`, () => {
    const next = random(0x5b);
    let [deg, r] = [next() * 360, 0.5];
    let held: Dir | null = null;
    for (let i = 0; i < WALK_MODEL_EVENTS; i++) {
      // Mostly small slides, now and then a jump, and sometimes into the dead zone or off the pad.
      const jump = next() < 0.05;
      deg = (((jump ? next() * 360 : deg + (next() - 0.5) * 16) % 360) + 360) % 360;
      r = jump ? next() * 1.6 : Math.min(2, Math.max(0, r + (next() - 0.5) * 0.2));
      const [dx, dy] = at(deg, r);
      // A fresh reading, with the dead zone a held thumb gets: three quarters of the full one.
      const plain = dirAt(dx, dy, held === null ? DEAD_ZONE : DEAD_ZONE * 0.75);
      const got = stickyDirAt(dx, dy, DEAD_ZONE, held);
      const where = `event ${i}: ${deg.toFixed(2)}° r ${r.toFixed(3)}, held ${held}, got ${got}`;

      // Exactly one sector holds every bearing.
      expect(SECTORS.filter((s) => past(s.dir, deg) === 0 && (deg - s.from + 360) % 360 < s.to - s.from), where).toHaveLength(1);
      if (got === null) {
        // Stops only in the dead zone: the full one from rest, three quarters of it under a held thumb.
        expect(r, where).toBeLessThanOrEqual(held === null ? DEAD_ZONE : DEAD_ZONE * 0.75 + 1e-9);
        expect(plain, where).toBeNull();
      } else if (got !== plain) {
        // Disagrees with a fresh reading only by keeping what it held, within STICK_DEGREES of that sector.
        expect(got, where).toBe(held);
        expect(past(got, deg), where).toBeLessThanOrEqual(STICK_DEGREES);
      }
      // Never keeps a direction the thumb has clearly left.
      if (got !== null) expect(past(got, deg), where).toBeLessThanOrEqual(STICK_DEGREES);
      // A press from rest outside the dead zone is always the fresh reading.
      if (held === null && r > DEAD_ZONE) expect(got, where).toBe(plain);
      held = got;
    }
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
