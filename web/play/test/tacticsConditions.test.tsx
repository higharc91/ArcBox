import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/preact";
import { ConditionChip } from "../src/games/tactics/art";

// The pack as the server serves it: an index naming the base pack's hash, then its icon manifest.
const icons = {
  icons: { hidden: { atlas: "status.png", rect: [73, 19, 16, 16], category: "status" } },
  abilityIcons: {},
};
const fetchMock = vi.fn(async (url: string) => {
  if (url === "/content/tactics/index.json") return new Response(JSON.stringify([{ packId: "base", hash: "h" }]));
  if (url === "/content/tactics/base/h/icons/manifest.json") return new Response(JSON.stringify(icons));
  return new Response("", { status: 404 });
});

beforeEach(() => vi.stubGlobal("fetch", fetchMock));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("condition chips", () => {
  it("a rogue's Hiding (#108) wears the Hidden status icon", async () => {
    const { container } = render(<ConditionChip id="hiding" />);
    expect(screen.getByText("hiding")).toBeTruthy();
    await waitFor(() => expect(container.querySelector(".tx-cond-icon img")?.getAttribute("src")).toBe("/content/tactics/base/h/icons/status.png"));
  });
});
