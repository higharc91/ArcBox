import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { Check } from "../src/games/tactics/story";
import pick from "./fixtures/tactics/check-pick.json";
import rolled from "./fixtures/tactics/check-rolled.json";
afterEach(cleanup);
it("lets a proficient hero offer Help", () => {
  const send = vi.fn();
  render(<Check data={{ ...pick.data, help: { can: true, member: null, name: null } }} send={send} stream={vi.fn()} viewId="help" />);
  fireEvent.click(screen.getByRole("button", { name: /Help the roller/ }));
  expect(send).toHaveBeenCalledWith("help");
});
it("shows every group die and the half-or-more verdict", () => {
  render(<Check data={{ ...rolled.data, group: true, roll: { ...rolled.data.roll, success: true, group: [
    { member: "one", name: "Ana", die: 18, bonus: 2, success: true },
    { member: "two", name: "Bo", die: 3, bonus: 0, success: false },
  ] } }} send={vi.fn()} stream={vi.fn()} viewId="group" />);
  expect(screen.getByText(/1 of 2 succeeded/)).toBeTruthy();
  expect(screen.getByText(/Ana: 18/)).toBeTruthy();
  expect(screen.getByText(/Bo: 3/)).toBeTruthy();
});
