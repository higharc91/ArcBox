import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { ComponentChildren } from "preact";
import type { ShakeView } from "../src/protocol";
import { Shake, FIRE_COOLDOWN_MS, SEND_INTERVAL_MS, SEND_STEP } from "../src/widgets/Shake";
import { ChargeModel, G, SHAKE_THRESHOLD } from "../src/widgets/chargeModel";
import { NO_MOTION_MS, type MotionApi, type MotionSource } from "../src/widgets/motion";
import { SkinContext } from "../src/skins/skin";
import { redlineSkin } from "../src/skins/redline";
import { Feedback, FeedbackContext } from "../src/feedback/feedback";
import { PATTERNS } from "../src/feedback/haptics";
import { Sounds } from "../src/feedback/sounds";
import type { KeyValueStore } from "../src/session/storage";

// ---------------------------------------------------------------- helpers

const shake = (over: Partial<ShakeView> = {}): ShakeView => ({
  kind: "shake", viewId: "v1", prompt: "Pop the cork! Shake again to reload", color: "#ff8000", canFire: true, initialCharge: 0, ...over,
});

/** A fake DeviceMotionEvent source: `api` is the constructor's statics, `target` stands in for window. */
function fakeMotion(api: MotionApi | undefined = {}) {
  const target = new EventTarget();
  const source: MotionSource = { api, target };
  /** One devicemotion sample of `g` (total, gravity included), `lateral` m/s² sideways. */
  const sample = (g: number, lateral = 0) =>
    act(() => {
      target.dispatchEvent(Object.assign(new Event("devicemotion"), {
        acceleration: null,
        accelerationIncludingGravity: { x: lateral, y: Math.sqrt(Math.max(0, (g * G) ** 2 - lateral ** 2)), z: 0 },
        interval: 20,
      }));
    });
  /** `ms` of samples every 20 ms at `g`, with the clock moving between them. */
  const shakeFor = (g: number, ms: number) => {
    for (let t = 0; t < ms; t += 20) {
      sample(g, t % 40 === 0 ? 6 : -6);
      advance(20);
    }
  };
  return { source, sample, shakeFor };
}

const noMotion = (): MotionSource => ({ api: undefined, target: new EventTarget() });

function advance(ms: number) {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
}

const charge = () => Number(screen.getByRole("meter").getAttribute("aria-valuenow")) / 100;
const bottle = () => screen.getByRole("button", { name: /shake the bottle/i });
const pop = () => screen.getByRole("button", { name: /pop/i }) as HTMLButtonElement;
const tapBottle = (times = 1) => {
  for (let i = 0; i < times; i++) fireEvent.pointerDown(bottle());
};

class MapStore implements KeyValueStore {
  map = new Map<string, string>();
  getItem(k: string) { return this.map.get(k) ?? null; }
  setItem(k: string, v: string) { this.map.set(k, v); }
  removeItem(k: string) { this.map.delete(k); }
}

const inRedline = (children: ComponentChildren) => <SkinContext.Provider value={redlineSkin}>{children}</SkinContext.Provider>;

let vibrateSpy: ReturnType<typeof vi.fn>;

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(Date.parse("2026-10-28T12:00:00Z"));
  vibrateSpy = vi.fn(() => true);
  Object.defineProperty(navigator, "vibrate", { value: vibrateSpy, configurable: true, writable: true });
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  delete (navigator as { vibrate?: unknown }).vibrate;
});

// ---------------------------------------------------------------- the pure model

describe("ChargeModel", () => {
  it("motion above gravity charges and stillness decays", () => {
    const m = new ChargeModel();
    for (let i = 0; i < 50; i++) m.addMotion(1, 0.02); // resting: gravity only
    for (let i = 0; i < 50; i++) m.addMotion(SHAKE_THRESHOLD - 0.1, 0.02); // a wave, not a shake
    expect(m.value).toBe(0);

    // Harder shakes charge faster.
    const soft = new ChargeModel();
    const hard = new ChargeModel();
    for (let i = 0; i < 25; i++) {
      soft.addMotion(2, 0.02);
      hard.addMotion(3, 0.02);
    }
    expect(soft.value).toBeGreaterThan(0);
    expect(hard.value).toBeGreaterThan(soft.value * 1.5);

    // A few seconds of hard shaking fills the bottle, never past 1; a long gap between samples counts as a short one.
    for (let i = 0; i < 400; i++) hard.addMotion(3, 0.02);
    expect(hard.value).toBe(1);
    const gap = new ChargeModel();
    gap.addMotion(3, 10);
    expect(gap.value).toBeLessThan(0.2);

    // Still for a moment: no loss. Still for longer: a slow leak, down to empty and no further.
    hard.decay(0.3);
    expect(hard.value).toBe(1);
    hard.decay(1);
    expect(hard.value).toBeLessThan(1);
    expect(hard.value).toBeGreaterThan(0.85);
    for (let i = 0; i < 60; i++) hard.decay(1);
    expect(hard.value).toBe(0);

    // Shaking again stops the leak.
    hard.addMotion(3, 0.02);
    const v = hard.value;
    hard.decay(0.3);
    expect(hard.value).toBe(v);

    // Garbage in: nothing changes.
    const g = new ChargeModel(0.5);
    g.addMotion(Number.NaN, 0.02);
    g.addMotion(3, Number.NaN);
    g.addMotion(Number.POSITIVE_INFINITY, 0.02);
    expect(g.value).toBe(0.5);
    expect(new ChargeModel(7).value).toBe(1);
    expect(new ChargeModel(-1).value).toBe(0);
  });

  it("taps charge, a flick needs charge and a calm moment first", () => {
    const m = new ChargeModel();
    m.addTap();
    expect(m.value).toBeGreaterThan(0.03);
    expect(m.value).toBeLessThan(0.1);
    expect(m.isFlick(5)).toBe(false); // nothing to spray yet
    for (let i = 0; i < 10; i++) m.addTap();
    expect(m.isFlick(5)).toBe(false); // still tapping: no calm moment
    m.decay(0.5);
    expect(m.isFlick(2)).toBe(false); // not sharp enough
    expect(m.isFlick(5)).toBe(true);
    m.reset();
    expect(m.value).toBe(0);
  });
});

// ---------------------------------------------------------------- the widget

describe("Shake", () => {
  it("motion above gravity charges and stillness decays", () => {
    const { source, sample, shakeFor } = fakeMotion();
    render(<Shake view={shake()} send={() => {}} motion={source} />);
    expect(charge()).toBe(0);
    sample(1);
    shakeFor(1, 500);
    expect(charge()).toBe(0);
    shakeFor(2.6, 1500);
    const full = charge();
    expect(full).toBeGreaterThan(0.4);
    advance(300);
    expect(charge()).toBe(full);
    advance(3000);
    expect(charge()).toBeLessThan(full);
    expect(charge()).toBeGreaterThan(0);
    // Motion is live: no fallback hint, no permission button.
    expect(screen.queryByText("Tap the bottle fast to shake it")).toBeNull();
    expect(screen.queryByRole("button", { name: /enable shaking/i })).toBeNull();
  });

  it("tap fallback charges (no motion API, permission denied, or a desktop that never sends motion)", async () => {
    // No DeviceMotionEvent at all.
    render(<Shake view={shake()} send={() => {}} motion={noMotion()} />);
    expect(screen.getByText("Tap the bottle fast to shake it")).toBeTruthy();
    tapBottle(5);
    expect(charge()).toBeGreaterThan(0.2);
    cleanup();

    // iOS, permission denied.
    const denied = fakeMotion({ requestPermission: vi.fn(async () => "denied") });
    render(<Shake view={shake()} send={() => {}} motion={denied.source} />);
    fireEvent.click(screen.getByRole("button", { name: "Tap to enable shaking" }));
    await act(async () => {});
    expect(screen.queryByRole("button", { name: "Tap to enable shaking" })).toBeNull();
    expect(screen.getByText("Tap the bottle fast to shake it")).toBeTruthy();
    tapBottle(5);
    expect(charge()).toBeGreaterThan(0.2);
    cleanup();

    // A desktop browser: the API exists but no sample ever arrives.
    const desktop = fakeMotion({});
    render(<Shake view={shake()} send={() => {}} motion={desktop.source} />);
    expect(screen.queryByText("Tap the bottle fast to shake it")).toBeNull();
    advance(NO_MOTION_MS + 100);
    expect(screen.getByText("Tap the bottle fast to shake it")).toBeTruthy();
    tapBottle(3);
    expect(charge()).toBeGreaterThan(0.1);
  });

  it("permission button shown on iOS-style API and requests inside the gesture", async () => {
    let resolve!: (s: string) => void;
    const requestPermission = vi.fn(() => new Promise<string>((r) => (resolve = r)));
    const { source, shakeFor } = fakeMotion({ requestPermission });
    const listen = vi.spyOn(source.target, "addEventListener");
    render(<Shake view={shake()} send={() => {}} motion={source} />);

    const button = screen.getByRole("button", { name: "Tap to enable shaking" });
    expect(requestPermission).not.toHaveBeenCalled(); // never outside a tap
    expect(listen).not.toHaveBeenCalled();
    fireEvent.click(button);
    expect(requestPermission).toHaveBeenCalledTimes(1); // synchronously, inside the tap

    await act(async () => resolve("granted"));
    expect(listen).toHaveBeenCalledWith("devicemotion", expect.any(Function));
    expect(screen.queryByRole("button", { name: "Tap to enable shaking" })).toBeNull();
    shakeFor(2.6, 1000);
    expect(charge()).toBeGreaterThan(0.2);
    expect(screen.queryByText("Tap the bottle fast to shake it")).toBeNull();
  });

  it("sends charge at most 4 per second and only on 0.05 change", () => {
    const sent: { at: number; payload: { charge: number; fire: boolean } }[] = [];
    const send = vi.fn((payload: object) => sent.push({ at: Date.now(), payload: payload as { charge: number; fire: boolean } }));
    const { source, sample, shakeFor } = fakeMotion();
    render(<Shake view={shake({ canFire: false })} send={send} motion={source} />);

    // A tiny wobble moves the charge less than a step: nothing goes out.
    sample(1);
    sample(SHAKE_THRESHOLD + 1.5);
    sample(SHAKE_THRESHOLD + 1.5);
    expect(charge()).toBeGreaterThan(0);
    expect(charge()).toBeLessThan(SEND_STEP);
    advance(1000);
    expect(send).not.toHaveBeenCalled();

    // Two seconds of frantic shaking (50 samples a second).
    shakeFor(3, 2000);
    advance(400); // the trailing send
    expect(sent.length).toBeGreaterThanOrEqual(3);
    expect(sent.length).toBeLessThanOrEqual(10);
    for (const s of sent) {
      expect(s.payload.fire).toBe(false);
      expect(typeof s.payload.charge).toBe("number");
    }
    for (let i = 1; i < sent.length; i++) {
      expect(sent[i]!.at - sent[i - 1]!.at).toBeGreaterThanOrEqual(SEND_INTERVAL_MS);
      expect(Math.abs(sent[i]!.payload.charge - sent[i - 1]!.payload.charge)).toBeGreaterThanOrEqual(SEND_STEP - 1e-9);
    }
    expect(SEND_INTERVAL_MS).toBe(250);
    expect(SEND_STEP).toBe(0.05);
    // The last send carries the charge the phone shows.
    expect(sent.at(-1)!.payload.charge).toBeCloseTo(charge(), 1);

    // Tap mashing (tap fallback) is held to the same budget.
    cleanup();
    sent.length = 0;
    render(<Shake view={shake({ canFire: false, viewId: "v2" })} send={send} motion={noMotion()} />);
    for (let t = 0; t < 1000; t += 25) {
      tapBottle();
      advance(25);
    }
    expect(sent.length).toBeLessThanOrEqual(4);
    expect(sent.length).toBeGreaterThanOrEqual(2);
  });

  it("fire sends Fire with current charge and resets", () => {
    const send = vi.fn();
    const { container } = render(<Shake view={shake({ initialCharge: 0.35 })} send={send} motion={noMotion()} />);
    expect(pop().disabled).toBe(false);
    fireEvent.click(pop());
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith({ charge: 0.35, fire: true }); // charge is always on the wire
    expect(charge()).toBe(0);
    expect(screen.getByText("Shake again to reload")).toBeTruthy();
    expect(container.querySelector(".spray")).not.toBeNull();
    expect(container.querySelector(".cork.flying")).not.toBeNull();

    // The server ignores fires closer than 400 ms apart; so does the button.
    const fires = () => send.mock.calls.filter(([p]) => (p as { fire: boolean }).fire);
    advance(FIRE_COOLDOWN_MS - 50);
    fireEvent.click(pop());
    expect(fires()).toHaveLength(1);
    advance(60);
    tapBottle(4);
    const c = charge();
    fireEvent.click(pop());
    expect(fires()).toHaveLength(2);
    expect(send).toHaveBeenLastCalledWith({ charge: Number(c.toFixed(2)), fire: true });
    expect(FIRE_COOLDOWN_MS).toBe(400);

    // Reloaded: the prompt goes back to the view's.
    advance(FIRE_COOLDOWN_MS);
    tapBottle(10);
    expect(screen.queryByText("Shake again to reload")).toBeNull();
    expect(screen.getByText("Pop the cork! Shake again to reload")).toBeTruthy();
  });

  it("a sharp flick after charging fires when CanFire", () => {
    const send = vi.fn();
    const { source, sample, shakeFor } = fakeMotion();
    render(<Shake view={shake()} send={send} motion={source} />);
    sample(1);
    shakeFor(2.6, 1500);
    const before = charge();
    expect(before).toBeGreaterThan(0.3);
    send.mockClear();
    advance(600); // a calm moment
    sample(5); // flick
    expect(send).toHaveBeenCalledWith({ charge: expect.closeTo(before, 1), fire: true });
    expect(charge()).toBe(0);
  });

  it("cannot fire when CanFire is false", () => {
    const send = vi.fn();
    const { source, sample, shakeFor } = fakeMotion();
    render(inRedline(<Shake view={shake({ canFire: false, prompt: "Shake to build pressure!" })} send={send} motion={source} />));
    expect(screen.getByText("Hold on — anthem playing. Shake to build pressure!")).toBeTruthy();
    expect(pop().disabled).toBe(true);
    fireEvent.click(pop());

    // Charging still works and is sent; a flick does not fire.
    sample(1);
    shakeFor(2.6, 1500);
    advance(600);
    sample(5);
    advance(300);
    expect(charge()).toBeGreaterThan(0.3);
    expect(send).toHaveBeenCalled();
    expect(send.mock.calls.every(([p]) => (p as { fire: boolean }).fire === false)).toBe(true);
    cleanup();

    // Without the REDLINE skin the hold line is generic.
    render(<Shake view={shake({ canFire: false, prompt: "Shake!" })} send={send} motion={noMotion()} />);
    expect(screen.getByText(/^Hold on — .+ Shake!$/)).toBeTruthy();
  });

  it("initial charge seeds the model", () => {
    const send = vi.fn();
    const { rerender } = render(<Shake view={shake({ canFire: false, initialCharge: 0.6 })} send={send} motion={noMotion()} />);
    expect(charge()).toBeCloseTo(0.6);
    advance(500);
    expect(send).not.toHaveBeenCalled(); // the server already knows it

    // Champagne: a new view seeds the charge the server stored.
    rerender(<Shake view={shake({ viewId: "v2", canFire: true, initialCharge: 0.45 })} send={send} motion={noMotion()} />);
    expect(charge()).toBeCloseTo(0.45);
    fireEvent.click(pop());
    expect(send).toHaveBeenCalledWith({ charge: 0.45, fire: true });

    // Out-of-range or broken seeds are clamped.
    rerender(<Shake view={shake({ viewId: "v3", initialCharge: 3 })} send={send} motion={noMotion()} />);
    expect(charge()).toBe(1);
    rerender(<Shake view={shake({ viewId: "v4", initialCharge: Number.NaN })} send={send} motion={noMotion()} />);
    expect(charge()).toBe(0);
  });

  it("muted suppresses fizz/pop/haptics", () => {
    const sounds = new Sounds();
    const play = vi.spyOn(sounds, "play");
    const store = new MapStore();
    const fb = new Feedback(store, sounds);
    const ui = (viewId: string) => (
      <FeedbackContext.Provider value={fb}>
        <Shake view={shake({ viewId })} send={() => {}} motion={noMotion()} />
      </FeedbackContext.Provider>
    );

    // Unmuted: charging fizzes and ticks, the pop pops with a strong buzz.
    const { rerender } = render(ui("v1"));
    tapBottle(6);
    expect(play.mock.calls.some(([s]) => s === "fizz")).toBe(true);
    expect(vibrateSpy).toHaveBeenCalledWith(PATTERNS.charge);
    fireEvent.click(pop());
    expect(play).toHaveBeenCalledWith("pop");
    expect(vibrateSpy).toHaveBeenLastCalledWith(PATTERNS.pop);
    expect(typeof PATTERNS.pop === "number" ? PATTERNS.pop : PATTERNS.pop.reduce((a, b) => a + b, 0)).toBeGreaterThanOrEqual(150);

    // Muted: silence and stillness.
    fb.setMuted(true);
    play.mockClear();
    vibrateSpy.mockClear();
    advance(FIRE_COOLDOWN_MS);
    rerender(ui("v2"));
    tapBottle(6);
    fireEvent.click(pop());
    expect(play).not.toHaveBeenCalled();
    expect(vibrateSpy).not.toHaveBeenCalled();
  });
});
