import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { h } from "preact";
import type { ButtonsView, ChoiceView, GameResults, LobbyState, WaitView } from "../src/protocol";
import { Lobby } from "../src/screens/Lobby";
import { Results } from "../src/screens/Results";
import lobbyFixture from "./fixtures/lobby.json";
import { Feedback, FeedbackContext, MUTE_KEY } from "../src/feedback/feedback";
import { PATTERNS, vibrate } from "../src/feedback/haptics";
import { MAX_GAIN, Sounds, type SoundName } from "../src/feedback/sounds";
import { MuteToggle } from "../src/feedback/MuteToggle";
import { Join } from "../src/screens/Join";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { redlineSkin } from "../src/skins/redline";
import type { KeyValueStore } from "../src/session/storage";

// ---------------------------------------------------------------- fakes

class MapStore implements KeyValueStore {
  map = new Map<string, string>();
  getItem(k: string) { return this.map.get(k) ?? null; }
  setItem(k: string, v: string) { this.map.set(k, v); }
  removeItem(k: string) { this.map.delete(k); }
}

/** An AudioParam that records every value it is given. */
class FakeParam {
  values: number[] = [];
  value = 0;
  private set(v: number) { this.values.push(v); this.value = v; return this; }
  setValueAtTime(v: number) { return this.set(v); }
  linearRampToValueAtTime(v: number) { return this.set(v); }
  exponentialRampToValueAtTime(v: number) { return this.set(v); }
  setTargetAtTime(v: number) { return this.set(v); }
}

class FakeNode {
  connect<T>(n: T) { return n; }
  disconnect() {}
}

class FakeSource extends FakeNode {
  startAt = -1;
  stopAt = Infinity;
  start(t = 0) { this.startAt = t; }
  stop(t = 0) { this.stopAt = t; }
}

class FakeOscillator extends FakeSource {
  type = "sine";
  frequency = new FakeParam();
  detune = new FakeParam();
}

class FakeBufferSource extends FakeSource {
  buffer: { length: number } | null = null;
}

class FakeAudioContext {
  static instances: FakeAudioContext[] = [];
  state: "suspended" | "running" = "suspended";
  currentTime = 0;
  sampleRate = 44100;
  destination = new FakeNode();
  sources: FakeSource[] = [];
  gains: FakeParam[] = [];
  resume = vi.fn(() => {
    this.state = "running";
    return Promise.resolve();
  });
  constructor() {
    FakeAudioContext.instances.push(this);
  }
  createOscillator() {
    const o = new FakeOscillator();
    this.sources.push(o);
    return o;
  }
  createBufferSource() {
    const s = new FakeBufferSource();
    this.sources.push(s);
    return s;
  }
  createGain() {
    const g = Object.assign(new FakeNode(), { gain: new FakeParam() });
    this.gains.push(g.gain);
    return g;
  }
  createBiquadFilter() {
    return Object.assign(new FakeNode(), { type: "lowpass", frequency: new FakeParam(), Q: new FakeParam() });
  }
  createBuffer(_channels: number, length: number, sampleRate: number) {
    const data = new Float32Array(length);
    return { length, sampleRate, duration: length / sampleRate, getChannelData: () => data };
  }
  /** Oscillators and noise started after the iOS unlock blip (a one-sample buffer). */
  get sounds() {
    return this.sources.filter((s) => !(s instanceof FakeBufferSource && s.buffer?.length === 1));
  }
}

const ctx = () => FakeAudioContext.instances.at(-1)!;

let vibrateSpy: ReturnType<typeof vi.fn>;

beforeEach(() => {
  FakeAudioContext.instances = [];
  vi.stubGlobal("AudioContext", FakeAudioContext);
  vibrateSpy = vi.fn(() => true);
  Object.defineProperty(navigator, "vibrate", { value: vibrateSpy, configurable: true, writable: true });
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  delete (navigator as { vibrate?: unknown }).vibrate;
});

const wait = (message: string, viewId = message): WaitView => ({ kind: "wait", viewId, message, header: "P5 · ROUND 3/15" });
const buttons = (viewId: string): ButtonsView => ({
  kind: "buttons", viewId, prompt: "Go?", buttons: [{ id: "a", label: "A" }], deadlineUtc: new Date(Date.now() + 10_000).toISOString(),
});

// ---------------------------------------------------------------- tests

describe("feedback", () => {
  it("send vibrates tap and clicks", () => {
    const fb = new Feedback(new MapStore());
    fb.unlock();
    fb.onSend();
    expect(vibrateSpy).toHaveBeenCalledWith(PATTERNS.tap);
    expect(PATTERNS.tap).toBe(15);
    expect(ctx().sounds.length).toBeGreaterThan(0);

    // The in-game screen sends through feedback: a tap on an option vibrates and clicks too.
    vibrateSpy.mockClear();
    const send = vi.fn();
    const choice: ChoiceView = { kind: "choice", viewId: "c1", prompt: "Pick", options: [{ id: "a", label: "Alpha" }], allowMultiple: false };
    render(h(FeedbackContext.Provider, { value: fb }, h(ControllerScreen, { view: choice, send, me: "A", game: "counter" })));
    const before = ctx().sounds.length;
    fireEvent.click(screen.getByRole("button", { name: /Alpha/ }));
    expect(send).toHaveBeenCalledWith({ selected: ["a"] });
    expect(vibrateSpy).toHaveBeenCalledWith(15);
    expect(ctx().sounds.length).toBeGreaterThan(before);
  });

  it("redline result views map to patterns", () => {
    const skin = redlineSkin.classifyResult!;
    expect(skin(wait("Correct! +4 · now P2"))).toBe("correct");
    expect(skin(wait("Wrong answer. No change · P5"))).toBe("wrong");
    expect(skin(wait("No answer. −1 · now P7"))).toBe("wrong");
    expect(skin(wait("Spun out! −2 · pit limiter next round · now P5"))).toBe("spin");
    expect(skin(wait("Spun out! Pit limiter next round · P5"))).toBe("spin");
    expect(skin(wait("Answer locked. Eyes on the TV!"))).toBeNull();
    // Reading shows the question as the message; even a question that starts "Correct!" is not a result.
    expect(skin({ kind: "wait", viewId: "r", message: "Correct! is the catchphrase of which host?", header: "PUSH · P5 · ROUND 3/15" })).toBeNull();

    const sounds = new Sounds();
    const play = vi.spyOn(sounds, "play");
    const fb = new Feedback(new MapStore(), sounds);
    const cases: [string, number | number[], SoundName][] = [
      ["Correct! +4 · now P2", [30, 40, 30], "correct"],
      ["Wrong answer. No change · P5", 120, "wrong"],
      ["No answer. −1 · now P7", 120, "wrong"],
      ["Spun out! −2 · pit limiter next round · now P5", [60, 30, 60, 30, 120], "spin"],
    ];
    for (const [message, pattern, sound] of cases) {
      vibrateSpy.mockClear();
      play.mockClear();
      fb.onView(wait("Answer locked. Eyes on the TV!", `locked-${message}`), "redline");
      fb.onView(wait(message), "redline");
      expect(vibrateSpy.mock.calls).toEqual([[pattern]]);
      expect(play.mock.calls).toEqual([[sound]]);
    }

    // The same view again (a rejoin re-sends it) does not replay; nor does a re-minted copy of the same result.
    vibrateSpy.mockClear();
    fb.onView(wait("Spun out! −2 · pit limiter next round · now P5"), "redline");
    fb.onView(wait("Spun out! −2 · pit limiter next round · now P5", "other-id"), "redline");
    expect(vibrateSpy).not.toHaveBeenCalled();

    // Other games get only the generic tap feedback: no result cues, even for the same wording.
    fb.onView(wait("Correct! +4 · now P2", "counter-1"), "counter");
    fb.onView(wait("Correct! +4 · now P2", "nogame-1"), null);
    expect(vibrateSpy).not.toHaveBeenCalled();
  });

  it("muted suppresses both and persists", () => {
    const store = new MapStore();
    const fb = new Feedback(store);
    expect(fb.muted).toBe(false);
    fb.unlock();
    fb.setMuted(true);
    expect(store.getItem(MUTE_KEY)).toBe("1");
    expect(MUTE_KEY).toBe("arcbox:mute");

    const before = ctx().sounds.length;
    fb.onSend();
    fb.onView(wait("Correct! +4 · now P2"), "redline");
    fb.onView(buttons("b1"), "redline");
    fb.onCountdown(2);
    expect(vibrateSpy).not.toHaveBeenCalled();
    expect(ctx().sounds.length).toBe(before);

    expect(new Feedback(store).muted).toBe(true); // survives a reload
    fb.setMuted(false);
    expect(store.getItem(MUTE_KEY)).toBe("0");
    expect(new Feedback(store).muted).toBe(false);

    // The toggle: in the status bar, labelled by what it will do, and it persists.
    const view = wait("Answer locked. Eyes on the TV!", "w9");
    const { container } = render(h(FeedbackContext.Provider, { value: fb }, h(ControllerScreen, { view, send: () => {}, me: "A", game: "redline" })));
    const toggle = container.querySelector(".pstatus button.mute-toggle") as HTMLButtonElement;
    expect(toggle.getAttribute("aria-label")).toBe("Mute sounds");
    fireEvent.click(toggle);
    expect(toggle.getAttribute("aria-label")).toBe("Unmute sounds");
    expect(store.getItem(MUTE_KEY)).toBe("1");
    expect(fb.muted).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Unmute sounds" }));
    expect(fb.muted).toBe(false);
    expect(vibrateSpy).toHaveBeenLastCalledWith(15); // unmuting confirms with a tap

    // Two toggles on one feedback stay in step; storage that throws is ignored.
    cleanup();
    render(h(FeedbackContext.Provider, { value: fb }, h("div", null, h(MuteToggle, null), h(MuteToggle, null))));
    fireEvent.click(screen.getAllByRole("button", { name: "Mute sounds" })[0]!);
    expect(screen.getAllByRole("button", { name: "Unmute sounds" })).toHaveLength(2);
    const broken: KeyValueStore = { getItem: () => { throw new Error("blocked"); }, setItem: () => { throw new Error("blocked"); }, removeItem: () => {} };
    const fb2 = new Feedback(broken);
    expect(fb2.muted).toBe(false);
    expect(() => fb2.setMuted(true)).not.toThrow();
    expect(fb2.muted).toBe(true);

    // Every screen after joining has it: the lobby and the results too.
    cleanup();
    const lobby = structuredClone(lobbyFixture) as LobbyState;
    const results: GameResults = { scores: [{ playerId: "p1", name: "Ann", score: 3 }], message: null };
    const { container: screens } = render(h(FeedbackContext.Provider, { value: fb }, h("div", null,
      h(Lobby, { lobby, me: lobby.players[0]!.id, onSelectGame: () => {}, onStart: () => {} }),
      h(Results, { results, me: "p1", onBack: () => {} }))));
    expect(screens.querySelector(".lobby-head .mute-toggle")).not.toBeNull();
    expect(screens.querySelector(".results-head .mute-toggle")).not.toBeNull();

    // Without feedback (unit renders, the dev board) there is no toggle.
    cleanup();
    const { container: bare } = render(h(ControllerScreen, { view, send: () => {}, me: "A", game: "redline" }));
    expect(bare.querySelector(".mute-toggle")).toBeNull();
  });

  it("no AudioContext before unlock and no throw", async () => {
    const fb = new Feedback(new MapStore());
    expect(() => {
      fb.onSend();
      fb.onView(wait("Correct! +4 · now P2"), "redline");
      fb.onView(buttons("b1"), "redline");
      fb.onCountdown(3);
    }).not.toThrow();
    expect(FakeAudioContext.instances).toHaveLength(0); // iOS: no context outside a gesture
    expect(vibrateSpy).toHaveBeenCalled(); // haptics need no unlock

    // The Join tap creates and resumes the context; sounds work from then on.
    const onJoin = vi.fn();
    render(h(FeedbackContext.Provider, { value: fb }, h(Join, { code: "ABCD", name: "Ann", onJoin })));
    fireEvent.click(screen.getByRole("button", { name: "Join" }));
    expect(onJoin).toHaveBeenCalled();
    expect(FakeAudioContext.instances).toHaveLength(1);
    expect(ctx().resume).toHaveBeenCalled();
    await Promise.resolve();
    expect(ctx().state).toBe("running");
    fb.onSend();
    expect(ctx().sounds.length).toBeGreaterThan(0);
    fb.unlock(); // further gestures reuse the one context
    expect(FakeAudioContext.instances).toHaveLength(1);

    // A rejoin after a reload has no Join tap: any tap on the screen unlocks.
    const rejoined = new Feedback(new MapStore());
    const target = new EventTarget();
    const remove = rejoined.installGestureUnlock(target);
    expect(FakeAudioContext.instances).toHaveLength(1);
    target.dispatchEvent(new Event("touchend"));
    expect(FakeAudioContext.instances).toHaveLength(2);
    remove();

    // No WebAudio at all, a constructor that throws, or a resume that rejects: never an exception.
    vi.stubGlobal("AudioContext", undefined);
    const none = new Feedback(new MapStore());
    expect(() => { none.unlock(); none.onSend(); none.onView(wait("Spun out! x"), "redline"); }).not.toThrow();
    vi.stubGlobal("AudioContext", function Broken() { throw new Error("NotAllowedError"); });
    const broken = new Feedback(new MapStore());
    expect(() => { broken.unlock(); broken.onSend(); }).not.toThrow();
    vi.stubGlobal("AudioContext", class extends FakeAudioContext {
      override resume = vi.fn(() => Promise.reject(new Error("not allowed")));
    });
    const rejecting = new Feedback(new MapStore());
    expect(() => { rejecting.unlock(); rejecting.onSend(); }).not.toThrow();
    await Promise.resolve();
  });

  it("countdown ticks only for last 3 s of input views", () => {
    const sounds = new Sounds();
    const play = vi.spyOn(sounds, "play");
    const fb = new Feedback(new MapStore(), sounds);
    const ticks = () => play.mock.calls.filter(([s]) => s === "tick").length;

    fb.onView(buttons("b1"), "redline");
    for (const s of [10, 5, 4, 3, 3, 2, 1, 1, 0]) fb.onCountdown(s);
    expect(ticks()).toBe(3); // 3, 2, 1: once each, never at 0

    play.mockClear();
    fb.onView(wait("Locked in: PUSH. Waiting for the other drivers…"), "redline");
    for (const s of [3, 2, 1]) fb.onCountdown(s);
    expect(ticks()).toBe(0); // a wait view has nothing to hurry

    fb.onView(buttons("b2"), "counter");
    fb.onSend(); // locked in: no need to hurry this player any more
    for (const s of [3, 2, 1]) fb.onCountdown(s);
    expect(ticks()).toBe(0);

    fb.onView(buttons("b3"), "counter");
    fb.onCountdown(2);
    expect(ticks()).toBe(1); // a fresh input view ticks again
    expect(vibrateSpy).toHaveBeenCalledTimes(1); // only the send vibrated: ticks are sound only

    // Wired through the screen: the Countdown's first second already sees the new view.
    play.mockClear();
    fb.onView(wait("Get ready…", "w3"), "counter"); // the view before: nothing to tick
    const soon = () => new Date(Date.now() + 2500).toISOString();
    const { rerender } = render(h(FeedbackContext.Provider, { value: fb }, h(ControllerScreen, { view: { ...buttons("b4"), deadlineUtc: soon() }, send: () => {}, me: "A", game: "counter" })));
    expect(ticks()).toBe(1);
    rerender(h(FeedbackContext.Provider, { value: fb }, h(ControllerScreen, { view: { ...wait("Waiting…", "w4"), deadlineUtc: soon() }, send: () => {}, me: "A", game: "counter" })));
    expect(ticks()).toBe(1);
  });

  it("vibrate missing is a no-op", () => {
    delete (navigator as { vibrate?: unknown }).vibrate;
    expect(vibrate(PATTERNS.spin)).toBe(false);
    const fb = new Feedback(new MapStore());
    fb.unlock();
    expect(() => {
      fb.onSend();
      fb.onView(wait("Spun out! −2 · pit limiter next round · now P5"), "redline");
    }).not.toThrow();
    expect(ctx().sounds.length).toBeGreaterThan(0); // sounds still play

    Object.defineProperty(navigator, "vibrate", { value: () => { throw new Error("blocked"); }, configurable: true });
    expect(vibrate(PATTERNS.tap)).toBe(false);
    expect(() => fb.onSend()).not.toThrow();

    // Before the first tap Chrome refuses (and logs): don't ask.
    Object.defineProperty(navigator, "vibrate", { value: vibrateSpy, configurable: true });
    Object.defineProperty(navigator, "userActivation", { value: { hasBeenActive: false }, configurable: true });
    try {
      expect(vibrate(PATTERNS.tap)).toBe(false);
      expect(vibrateSpy).not.toHaveBeenCalled();
    } finally {
      delete (navigator as { userActivation?: unknown }).userActivation;
    }
  });

  it("sounds are short and quiet", () => {
    const limits: Record<SoundName, number> = { tap: 0.3, tick: 0.3, wrong: 0.3, spin: 0.3, correct: 0.6, fizz: 0.3, pop: 1 };
    for (const [name, limit] of Object.entries(limits) as [SoundName, number][]) {
      const sounds = new Sounds();
      sounds.unlock();
      const c = ctx();
      c.currentTime = 5;
      sounds.play(name);
      expect(c.sounds.length, name).toBeGreaterThan(0);
      const start = Math.min(...c.sounds.map((s) => s.startAt));
      const end = Math.max(...c.sounds.map((s) => s.stopAt));
      expect(start, name).toBeGreaterThanOrEqual(5);
      expect(end - start, name).toBeLessThanOrEqual(limit);
      const gains = c.gains.flatMap((g) => g.values);
      expect(gains.length, name).toBeGreaterThan(0);
      expect(Math.max(...gains), name).toBeLessThanOrEqual(MAX_GAIN);
    }
    expect(MAX_GAIN).toBeLessThanOrEqual(0.25);
  });
});
