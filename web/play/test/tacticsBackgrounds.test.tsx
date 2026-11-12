import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { withBackgroundSecret } from "../src/games/tactics/backgrounds";
import { Vote } from "../src/games/tactics/story";
import type { CustomScreenProps } from "../src/games/module";

afterEach(cleanup);
const props = (data: unknown): CustomScreenProps => ({ data, send: vi.fn(), viewId: "v1", header: "", deadlineUtc: null }) as unknown as CustomScreenProps;

describe("private backgrounds", () => {
  it("defers a secret while the server's map is open so only one modal owns focus", () => {
    const Screen = withBackgroundSecret(({ data }) => (data as { map?: unknown }).map
      ? <div role="dialog" aria-label="Map">Map content</div> : <p>Game screen</p>);
    const secret = { id: "soldier", background: "Soldier", text: "Private memory" };
    const { rerender } = render(<Screen {...props({ backgroundSecret: secret, map: {} })} />);
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(screen.getByRole("dialog", { name: "Map" })).toBeTruthy();
    expect(screen.queryByText("Private memory")).toBeNull();
    rerender(<Screen {...props({ backgroundSecret: secret })} />);
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(screen.getByRole("dialog", { name: "Soldier · Yours alone" })).toBeTruthy();
  });

  it("shows a focus-managed private secret only when it is present in this phone's packet", () => {
    const Screen = withBackgroundSecret(() => <p>Game screen</p>);
    const own = props({ backgroundSecret: { id: "soldier", background: "Soldier", text: "A secret for Kara." } });
    render(<Screen {...own} />);
    expect(screen.getByRole("dialog", { name: "Soldier · Yours alone" })).toBeTruthy();
    expect(screen.getByText("A secret for Kara.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Remember this" }));
    expect(own.send).toHaveBeenCalledWith("backgroundSecretRead", {});
    cleanup(); render(<Screen {...props({})} />);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByText("A secret for Kara.")).toBeNull();
  });
  it("credits a personal answer and leaves ordinary leader answers disabled for its owner", () => {
    const own = props({ party: false, personal: true, canChoose: true, options: [
      { id: "why", label: "Why?", skill: null, dc: null, can: false },
      { id: "truth", label: "Your guards shot first.", skill: null, dc: null, can: true, credit: "Soldier · Kara" },
    ], mine: null, chosen: null, tie: false, decider: "Ana", away: [], vip: false });
    render(<Vote {...own} />);
    expect(screen.getByText("Soldier · Kara")).toBeTruthy();
    expect(screen.getByText("Choose your personal option")).toBeTruthy();
    expect(screen.getByRole<HTMLButtonElement>("button", { name: /Why/ }).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: /Your guards shot first/ }));
    expect(own.send).toHaveBeenCalledWith("vote", { option: "truth" });
  });
});
