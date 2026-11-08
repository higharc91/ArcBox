import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import type { TurnData } from "../src/games/tactics/types";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import fixture from "./fixtures/tactics/turn.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(() => { cleanup(); vi.useRealTimers(); });

function setup(edit: (d: TurnData) => void = () => {}) {
  const view = structuredClone(fixture) as CustomView;
  const data = view.data as unknown as TurnData;
  edit(data);
  const send = vi.fn();
  const props = { view, send, me: "ALEX", game: "tactics" };
  const result = render(<ControllerScreen {...props} />);
  return { ...result, view, data, send, props };
}
const end = () => fireEvent.click(screen.getByRole("button", { name: "End turn" }));

describe("End turn confirmation", () => {
  it("requires a second tap when a legal action remains", () => {
    const { send } = setup(d => { d.choices.economy.bonus = false; });
    end();
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "End turn? Action unused" }));
    expect(send).toHaveBeenCalledExactlyOnceWith({ action: "endTurn" });
  });

  it("also protects a bonus action with an available target", () => {
    const { send } = setup(d => {
      d.choices.economy.action = false;
      d.choices.economy.bonus = true;
      d.choices.abilities = [{ ...d.choices.abilities[0]!, cost: { bonus: 1 } }];
    });
    end();
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "End turn? Bonus unused" }));
    expect(send).toHaveBeenCalledExactlyOnceWith({ action: "endTurn" });
  });

  it("protects extra actions and spells aimed at bare tiles", () => {
    const { send } = setup(d => {
      d.choices.economy.action = false;
      d.choices.economy.extraActions = 1;
      d.choices.abilities = [{ ...d.choices.abilities[0]!, cost: { action: 1 }, targets: [], tiles: [[1, 1]] }];
    });
    end();
    expect(send).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "End turn? Action unused" })).toBeTruthy();
  });

  it.each(["spent", "no target", "free only"])("ends in one tap when nothing remains: %s", kind => {
    const { send } = setup(d => {
      if (kind === "spent") {
        d.choices.economy.action = false;
        d.choices.economy.bonus = false;
        d.choices.economy.extraActions = 0;
      } else if (kind === "no target") {
        d.choices.abilities = d.choices.abilities.map(a => ({ ...a, targets: [], tiles: [] }));
      } else {
        d.choices.abilities = d.choices.abilities.map(a => ({ ...a, cost: {} }));
      }
    });
    end();
    expect(send).toHaveBeenCalledExactlyOnceWith({ action: "endTurn" });
  });

  it("expires after three seconds without ending the turn", () => {
    vi.useFakeTimers();
    const { send } = setup();
    end();
    act(() => { vi.advanceTimersByTime(3000); });
    end();
    expect(send).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "End turn? Action & Bonus unused" })).toBeTruthy();
  });

  it("clears confirmation when browsing an ability", () => {
    const { send } = setup();
    end();
    fireEvent.click(screen.getByRole("button", { name: /^Dash/ }));
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    end();
    expect(send).not.toHaveBeenCalled();
  });

  it("clears confirmation when a new server view arrives", () => {
    const { data, props, rerender, send } = setup();
    end();
    rerender(<ControllerScreen {...props} view={{ ...props.view, data: { ...data, me: { ...data.me, hp: data.me.hp - 1 } } }} />);
    end();
    expect(send).not.toHaveBeenCalled();
  });
});
