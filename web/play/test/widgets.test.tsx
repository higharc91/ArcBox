import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { ButtonsView, ChoiceView, ControllerView, DrawView, TextEntryView } from "../src/protocol";
import { Buttons } from "../src/widgets/Buttons";
import { Choice } from "../src/widgets/Choice";
import { TextEntry } from "../src/widgets/TextEntry";
import { Draw } from "../src/widgets/Draw";
import { Countdown, remainingSeconds } from "../src/widgets/Countdown";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { syncWakeLock } from "../src/widgets/wakeLock";

const fixtures = import.meta.glob<{ default: ControllerView }>("./fixtures/*.json", { eager: true });
const fixture = <T extends ControllerView>(kind: string) => structuredClone(fixtures[`./fixtures/${kind}.json`]!.default) as T;

afterEach(cleanup);

const buttons = (viewId = "v1"): ButtonsView => ({
  kind: "buttons", viewId, prompt: "Go?", buttons: [{ id: "a", label: "A" }, { id: "b", label: "B" }],
});
const choice = (over: Partial<ChoiceView> = {}): ChoiceView => ({
  kind: "choice", viewId: "v1", prompt: "Pick",
  options: [{ id: "a", label: "Alpha" }, { id: "b", label: "Bravo" }, { id: "c", label: "Charlie" }],
  allowMultiple: false, ...over,
});

describe("Buttons", () => {
  it("Buttons sends once per viewId", () => {
    const send = vi.fn();
    const { rerender } = render(<Buttons view={buttons("v1")} send={send} />);
    fireEvent.click(screen.getByRole("button", { name: /A/ }));
    fireEvent.click(screen.getByRole("button", { name: /A/ }));
    fireEvent.click(screen.getByRole("button", { name: /B/ }));
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith({ pressed: "a" });
    expect((screen.getByRole("button", { name: /B/ }) as HTMLButtonElement).disabled).toBe(true);

    rerender(<Buttons view={buttons("v2")} send={send} />);
    expect((screen.getByRole("button", { name: /B/ }) as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: /B/ }));
    expect(send).toHaveBeenLastCalledWith({ pressed: "b" });
    expect(send).toHaveBeenCalledTimes(2);
  });

  it("a lone button is a mash button: every tap sends (Counter keeps one view for the whole game)", () => {
    const send = vi.fn();
    render(<Buttons view={{ kind: "buttons", viewId: "v1", prompt: "Mash it!", buttons: [{ id: "press", label: "+1" }] }} send={send} />);
    for (let i = 0; i < 5; i++) fireEvent.click(screen.getByRole("button", { name: "+1" }));
    expect(send).toHaveBeenCalledTimes(5);
    expect(send).toHaveBeenCalledWith({ pressed: "press" });
  });

  it("marks the pressed button", () => {
    render(<Buttons view={buttons()} send={() => {}} />);
    fireEvent.click(screen.getByRole("button", { name: /B/ }));
    expect(screen.getByRole("button", { name: /B/ }).classList.contains("picked")).toBe(true);
  });
});

describe("Choice", () => {
  it("Choice single sends selected id", () => {
    const send = vi.fn();
    render(<Choice view={choice()} send={send} />);
    fireEvent.click(screen.getByRole("button", { name: /Bravo/ }));
    fireEvent.click(screen.getByRole("button", { name: /Charlie/ }));
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith({ selected: ["b"] });
  });

  it("Choice multiple respects maxSelections and sends on Lock in", () => {
    const send = vi.fn();
    render(<Choice view={choice({ allowMultiple: true, maxSelections: 2 })} send={send} />);
    const lock = () => screen.getByRole("button", { name: /Lock in/ }) as HTMLButtonElement;
    expect(lock().disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: /Alpha/ }));
    fireEvent.click(screen.getByRole("button", { name: /Bravo/ }));
    fireEvent.click(screen.getByRole("button", { name: /Charlie/ })); // over the cap: ignored
    expect(screen.getByRole("button", { name: /Charlie/ }).getAttribute("aria-pressed")).toBe("false");
    fireEvent.click(screen.getByRole("button", { name: /Alpha/ })); // toggle off
    fireEvent.click(screen.getByRole("button", { name: /Charlie/ }));
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(lock());
    fireEvent.click(lock());
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith({ selected: ["b", "c"] });
  });

  it("Option hint renders as second line", () => {
    render(<Choice view={fixture<ChoiceView>("choice")} send={() => {}} />);
    const option = screen.getByRole("button", { name: /Oscar Piastri/ });
    const lines = option.querySelectorAll(".opt-label, .opt-hint");
    expect([...lines].map((l) => l.textContent)).toEqual(["#81 Oscar Piastri", "McLaren · MCL40"]);
    expect(screen.getByRole("button", { name: /No hint/ }).querySelector(".opt-hint")).toBeNull();
  });
});

describe("TextEntry", () => {
  const view: TextEntryView = { kind: "textEntry", viewId: "v1", prompt: "Say it", maxLength: 5, placeholder: "here" };

  it("TextEntry trims and refuses empty / over maxLength", () => {
    const send = vi.fn();
    render(<TextEntry view={view} send={send} />);
    const input = screen.getByPlaceholderText("here") as HTMLInputElement;
    const submit = screen.getByRole("button", { name: /Send/ }) as HTMLButtonElement;
    expect(input.maxLength).toBe(5);

    fireEvent.input(input, { target: { value: "   " } });
    expect(submit.disabled).toBe(true);
    fireEvent.submit(input.form!);
    fireEvent.input(input, { target: { value: "toolong" } });
    expect(submit.disabled).toBe(true);
    fireEvent.submit(input.form!);
    expect(send).not.toHaveBeenCalled();

    fireEvent.input(input, { target: { value: "  hi  " } });
    expect(submit.disabled).toBe(false);
    fireEvent.click(submit);
    fireEvent.submit(input.form!);
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith({ text: "hi" });
  });

  it("uses a 16px+ font so iOS doesn't zoom", () => {
    render(<TextEntry view={view} send={() => {}} />);
    expect(screen.getByPlaceholderText("here").classList.contains("text-input")).toBe(true);
  });
});

describe("Draw", () => {
  it("Draw submit sends normalised strokes", () => {
    const send = vi.fn();
    const view: DrawView = { kind: "draw", viewId: "v1", prompt: "Draw", colors: ["#111111", "#e10600"] };
    render(<Draw view={view} send={send} />);
    const canvas = document.querySelector("canvas")!;
    canvas.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 100, right: 100, bottom: 100, x: 0, y: 0, toJSON() {} });
    const submit = screen.getByRole("button", { name: /Submit/ }) as HTMLButtonElement;
    expect(submit.disabled).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: /#e10600/ }));
    fireEvent.pointerDown(canvas, { clientX: 10, clientY: 20, pointerId: 1 });
    fireEvent.pointerMove(canvas, { clientX: 50, clientY: 50, pointerId: 1 });
    fireEvent.pointerMove(canvas, { clientX: 150, clientY: -5, pointerId: 1 }); // clamped into 0..1
    fireEvent.pointerUp(canvas, { clientX: 150, clientY: -5, pointerId: 1 });

    fireEvent.pointerDown(canvas, { clientX: 90, clientY: 90, pointerId: 1 });
    fireEvent.pointerUp(canvas, { clientX: 90, clientY: 90, pointerId: 1 });
    fireEvent.click(screen.getByRole("button", { name: /Undo/ }));

    fireEvent.click(submit);
    fireEvent.click(submit);
    expect(send).toHaveBeenCalledTimes(1);
    const payload = send.mock.calls[0]![0] as { strokes: { color: string; width: number; points: number[] }[] };
    expect(payload.strokes).toHaveLength(1);
    expect(payload.strokes[0]!.color).toBe("#e10600");
    expect(payload.strokes[0]!.points).toEqual([0.1, 0.2, 0.5, 0.5, 1, 0]);
    expect(payload.strokes[0]!.width).toBeGreaterThan(0);
    expect(payload.strokes[0]!.width).toBeLessThan(1);
  });

  it("defaults to a single #111 colour", () => {
    render(<Draw view={{ kind: "draw", viewId: "v1", prompt: "Draw" }} send={() => {}} />);
    expect(screen.queryAllByRole("button", { name: /^Colour/ })).toHaveLength(1);
  });
});

describe("Countdown", () => {
  const now = Date.parse("2026-01-01T00:00:00Z");

  it("Countdown clamps a deadline far in the future/past", () => {
    expect(remainingSeconds("2026-01-01T00:00:12.200Z", now)).toBe(13);
    expect(remainingSeconds("2026-01-01T00:00:00+00:00", now)).toBe(0);
    expect(remainingSeconds("2026-01-01T05:00:00Z", now)).toBe(60);
    expect(remainingSeconds("2025-12-31T23:00:00Z", now)).toBe(0);
    expect(remainingSeconds("garbage", now)).toBe(0);
  });

  it("renders m:ss", () => {
    vi.useFakeTimers();
    vi.setSystemTime(now);
    render(<Countdown deadlineUtc="2026-01-01T00:00:09Z" />);
    expect(screen.getByText("0:09")).toBeTruthy();
    vi.useRealTimers();
  });
});

describe("ControllerScreen", () => {
  it.each([
    ["wait", ".w-wait"],
    ["textEntry", ".w-text"],
    ["choice", ".w-choice"],
    ["buttons", ".w-buttons"],
    ["draw", ".w-draw"],
    ["shake", ".w-shake"],
  ])("ControllerScreen renders header and picks widget by kind (%s)", (kind, selector) => {
    const view = fixture(kind);
    const { container } = render(<ControllerScreen view={view} send={() => {}} me="ALEX" />);
    expect(container.querySelector(selector)).not.toBeNull();
    expect(container.querySelector(".pstatus .pos")!.textContent).toBe("H");
    expect(container.querySelector(".pstatus .me")!.textContent).toBe("ALEX");
    expect(container.querySelector(".ptimer")).not.toBeNull(); // the fixtures carry a deadline
  });

  it("omits header and countdown when the view has none", () => {
    const { container } = render(<ControllerScreen view={{ kind: "wait", viewId: "v1", message: "Hold on", header: null }} send={() => {}} me="ALEX" />);
    expect(container.querySelector(".pstatus .pos")).toBeNull();
    expect(container.querySelector(".ptimer")).toBeNull();
    expect(screen.getByText("Hold on")).toBeTruthy();
  });

  it("shows the shake widget in the view's colour and sends nothing on its own", () => {
    const send = vi.fn();
    const { container } = render(<ControllerScreen view={fixture("shake")} send={send} me="ALEX" game="redline" />);
    const widget = container.querySelector<HTMLElement>(".controller.k-shake .w-shake")!;
    expect(widget).not.toBeNull();
    expect(widget.style.getPropertyValue("--accent")).toBe("#ff8000");
    expect(container.textContent).toContain("Pop the cork!");
    expect(send).not.toHaveBeenCalled();
  });

  it("forwards input to send", () => {
    const send = vi.fn();
    render(<ControllerScreen view={fixture("buttons")} send={send} me="ALEX" />);
    fireEvent.click(screen.getByRole("button", { name: /PUSH/ }));
    expect(send).toHaveBeenCalledWith({ pressed: "push" });
  });
});

describe("wakeLock", () => {
  it("requests while active, releases on wait, and swallows failures", async () => {
    const release = vi.fn(async () => {});
    const request = vi.fn(async () => ({ release, released: false, addEventListener() {} }));
    Object.defineProperty(navigator, "wakeLock", { value: { request }, configurable: true });
    await syncWakeLock(true);
    await syncWakeLock(true);
    expect(request).toHaveBeenCalledTimes(1);
    await syncWakeLock(false);
    expect(release).toHaveBeenCalledTimes(1);

    request.mockRejectedValueOnce(new Error("NotAllowedError"));
    await expect(syncWakeLock(true)).resolves.toBeUndefined();
    await syncWakeLock(false);
    Object.defineProperty(navigator, "wakeLock", { value: undefined, configurable: true });
    await expect(syncWakeLock(true)).resolves.toBeUndefined();
  });
});
