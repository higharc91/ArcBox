import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { Walk } from "../src/games/tactics/walk";
import fixture from "./fixtures/tactics/walk-with-gold.json";
afterEach(() => { cleanup(); vi.useRealTimers(); });
it("gives a potion and twenty gold to an away hero and ignores a double tap", () => {
  vi.useFakeTimers();
  const send = vi.fn();
  const data = { ...fixture.data, give: { gold: 100, items: [{ id: "potion_of_healing", name: "Potion of Healing", count: 3 }], members: [{ id: "away", name: "Away hero", away: true }] } };
  render(<Walk data={data} send={send} stream={vi.fn()} viewId="gift-view" />);
  fireEvent.click(screen.getByText("Give items or gold"));
  fireEvent.change(screen.getByLabelText("Give to"), { target: { value: "away" } });
  expect(screen.getByText("Away hero (away)")).toBeTruthy();
  fireEvent.change(screen.getByLabelText("Item"), { target: { value: "potion_of_healing" } });
  fireEvent.input(screen.getByLabelText("Gold to give"), { target: { value: "20" } });
  fireEvent.click(screen.getByRole("button", { name: "Give" }));
  fireEvent.click(screen.getByRole("button", { name: /Give|Sending/ }));
  expect(send).toHaveBeenCalledTimes(1);
  act(() => { vi.advanceTimersByTime(3000); });
  fireEvent.click(screen.getByRole("button", { name: "Give" }));
  expect(send).toHaveBeenCalledTimes(2);
  expect(send.mock.calls[1]).toEqual(send.mock.calls[0]); // retry the same intent if its reply was lost
  expect(send).toHaveBeenCalledWith("give", { member: "away", item: "potion_of_healing", count: 1, gold: 20, trade: expect.any(String) });
});

it("requests gift details only when the player opens their bag", () => {
  const send = vi.fn();
  const { container } = render(<Walk data={{ ...fixture.data, canGive: true }} send={send} stream={vi.fn()} viewId="walk" />);
  expect(screen.queryByLabelText("Give to")).toBeNull();
  const details = container.querySelector<HTMLDetailsElement>(".tx-give")!;
  details.open = true;
  fireEvent(details, new Event("toggle"));
  expect(send).toHaveBeenCalledWith("giveOpen");
});
