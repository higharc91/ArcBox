import { describe as suite, expect, it } from "vitest";
import { describe } from "../src/dev/TacticsBoard";

const names = new Map([["hero1", "Ann"], ["goblin.1", "Goblin"]]);
const name = (id: unknown) => (typeof id === "string" ? names.get(id) ?? id : "?");
const line = (e: Record<string, unknown>) => describe({ $type: "?", ...e } as never, name);

suite("tactics dev board log", () => {
  it("describes the events a battle is told by", () => {
    expect(line({ $type: "AbilityUsed", source: "hero1", ability: "wizard.magic_missile", target: { unit: "goblin.1" } }))
      .toBe("Ann uses magic missile on Goblin");
    expect(line({ $type: "AttackResolved", source: "goblin.1", target: "hero1", hit: true, crit: false })).toBe("Goblin hits Ann");
    expect(line({ $type: "Damaged", unit: "hero1", amount: 9, type: "piercing", hpAfter: 0 })).toBe("Ann takes 9 piercing (0 HP left)");
    expect(line({ $type: "Reacted", reactor: "hero1", choice: "pass" })).toBe("Ann lets it go");
    expect(line({ $type: "Reacted", reactor: "hero1", choice: "wizard.shield" })).toBe("Ann reacts: shield");
  });

  it("names conditions in words", () => {
    expect(line({ $type: "ConditionApplied", unit: "hero1", condition: "prone" })).toBe("Ann is prone");
    expect(line({ $type: "ConditionApplied", unit: "hero1", condition: "statMod.wizard.shield" })).toBe("Ann gains shield");
  });

  it("leaves bookkeeping events out", () => {
    expect(line({ $type: "PassiveTriggered", owner: "hero1", passive: "human.resourceful" })).toBeNull();
    expect(line({ $type: "Moved", unit: "hero1", path: [[0, 0], [1, 0]] })).toBeNull();
  });
});
