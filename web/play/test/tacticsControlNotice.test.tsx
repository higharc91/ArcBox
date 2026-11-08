import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import type { CustomView } from "../src/protocol";
import type { ControlNoticeData, TurnData } from "../src/games/tactics/types";
import status from "./fixtures/tactics/status.json";
import turn from "./fixtures/tactics/turn.json";
import react from "./fixtures/tactics/react.json";
import results from "./fixtures/tactics/results.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(cleanup);
const notice: ControlNoticeData = { seq: 1, reason: "turnTimeout", text: "Time ran out. The AI is finishing your turn.", policy: "Timed-out turns don't use items." };

it.each([
  ["turnTimeout", "Time ran out. The AI is finishing your turn."],
  ["reactionTimeout", "Reaction time ran out. The AI chose Shield for you."],
  ["disconnected", "You're away. The AI is playing Aldric. Rejoin to take control."],
] as const)("status explains %s without a duplicate generic AI message", (reason, text) => {
  const view = structuredClone(status) as CustomView;
  view.data = { ...(view.data as object), aiPlaying: true, controlNotice: { ...notice, reason, text } };
  render(<ControllerScreen view={view} send={vi.fn()} me="ALEX" game="tactics" />);
  expect(screen.getByRole("status").textContent).toContain(text);
  expect(screen.getByRole("status").textContent).toContain(notice.policy);
  expect(screen.queryByText("The AI is playing your character")).toBeNull();
});

it.each([turn, react, results])("return confirmation and recap survive every battle screen", fixture => {
  const view = structuredClone(fixture) as CustomView;
  view.data = { ...(view.data as object), controlNotice: { seq: 2, reason: "reconnected", text: "Welcome back, Dave. You're back in control of Aldric.", recap: "While you were away: used Attack." } };
  render(<ControllerScreen view={view} send={vi.fn()} me="ALEX" game="tactics" />);
  expect(screen.getByRole("status").textContent).toContain("Welcome back, Dave");
  expect(screen.getByRole("status").textContent).toContain("used Attack");
});

it("adding and expiring a control notice preserves the player's current aim", () => {
  const view = structuredClone(turn) as CustomView;
  const props = { view, send: vi.fn(), me: "ALEX", game: "tactics" };
  const shown = render(<ControllerScreen {...props} />);
  fireEvent.click(screen.getByRole("button", { name: /^Dash/ }));
  const changed = { ...view, data: { ...(view.data as object), controlNotice: notice } };
  shown.rerender(<ControllerScreen {...props} view={changed} />);
  expect(screen.getByRole("button", { name: "Back" })).toBeTruthy();
  expect(screen.getByRole("status").textContent).toContain(notice.text);
  shown.rerender(<ControllerScreen {...props} view={view} />);
  expect(screen.getByRole("button", { name: "Back" })).toBeTruthy();
  expect((view.data as unknown as TurnData).controlNotice).toBeUndefined();
});
