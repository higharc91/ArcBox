import actualPick from "./fixtures/meridian-selection/crewPick.json";
import actualSheet from "./fixtures/meridian-selection/sheet.json";
import actualTurn from "./fixtures/meridian-selection/turn.json";
import { h } from "preact";
import { cleanup, render } from "@testing-library/preact";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { FeedbackContext } from "../src/feedback/feedback";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CrewSelection } from "../src/games/meridian/crew-selection";
import { CREW_SIGNATURES } from "../src/feedback/crew-tones";
import { Feedback, MUTE_KEY } from "../src/feedback/feedback";
import { Sounds } from "../src/feedback/sounds";
import type { CustomView } from "../src/protocol";

const view = (screen: string, data: unknown, id = Math.random().toString()): CustomView =>
  ({ kind: "custom", module: "meridian", screen, data, viewId: id });
const pick = (mine?: string) => view("crewPick", { crew: Object.keys(CREW_SIGNATURES).map(id => ({ id, mine: mine === id, taken: false })) });
afterEach(() => { cleanup();vi.restoreAllMocks(); });

describe("confirmed crew lock-in feedback", () => {
  it.each(Object.keys(CREW_SIGNATURES))("plays %s once after the own server confirmation", id => {
    const tracker = new CrewSelection();
    expect(tracker.observe(pick(), "meridian")).toBeUndefined();
    expect(tracker.observe(pick(), "meridian")).toBeUndefined(); // rejected tap / roster refresh
    expect(tracker.observe(pick(id), "meridian")).toBe(`crew.${id}`);
    expect(tracker.observe(pick(id), "meridian")).toBeUndefined();
    expect(tracker.observe(view("sheet", { me: { id } }), "meridian")).toBeUndefined();
  });
  it("does not greet an already-chosen reconnect or a direct mid-game join", () => {
    const t = new CrewSelection();
    expect(t.observe(pick("ines_okafor"), "meridian")).toBeUndefined();
    expect(t.observe(pick("ines_okafor"), "meridian")).toBeUndefined();
    t.reset();
    expect(t.observe(view("sheet", { me: { id: "ines_okafor" } }), "meridian")).toBeUndefined();
  });
  it("waits for the actual last-picker / auto-assigned identity through presentation", () => {
    const t = new CrewSelection();t.observe(pick(), "meridian");
    expect(t.observe(view("watch", { event: "start" }), "meridian")).toBeUndefined();
    expect(t.observe(view("turn", { me: { id: "kit_sato" } }), "meridian")).toBe("crew.kit_sato");
    expect(t.observe(view("sheet", { me: { id: "kit_sato" } }), "meridian")).toBeUndefined();
  });
  it("never cues another player's selection, unknown data, another module or secret briefing", () => {
    const t = new CrewSelection();t.observe(pick(), "meridian");
    expect(t.observe(view("crewPick", { crew: [{ id: "sable_quist", mine: false, taken: true }] }), "meridian")).toBeUndefined();
    expect(t.observe(view("briefing", { you: { role: "mimic" }, me: { id: "sable_quist" } }), "meridian")).toBeUndefined();
    expect(t.observe(view("sheet", { me: { id: "sable_quist" } }), "meridian")).toBeUndefined();
    expect(t.observe(view("crewPick", null), "meridian")).toBeUndefined();
    expect(t.observe(view("crewPick", { crew: "invalid" }), "meridian")).toBeUndefined();
    expect(t.observe(view("sheet", { me: { id: "not-a-crew-member" } }), "meridian")).toBeUndefined();
    t.reset();t.observe(pick(), "meridian");
    expect(t.observe(pick("tobe_raske"), "tactics")).toBeUndefined();
    expect(t.observe({ ...pick("tobe_raske"), module: "tactics" }, "meridian")).toBeUndefined();
  });
  it("can greet a new Boarding session after Debrief", () => {
    const t = new CrewSelection();t.observe(pick(), "meridian");t.observe(pick("ines_okafor"), "meridian");
    t.observe(view("debrief", {}), "meridian");t.observe(pick(), "meridian");
    expect(t.observe(pick("rhee_callender"), "meridian")).toBe("crew.rhee_callender");
  });
  it("the real phone Feedback suppresses repeats, honours mute, and does not catch up after unmute", () => {
    const store = new Map<string, string>();
    const fb = new Feedback({ getItem:k=>store.get(k)??null,setItem:(k,v)=>{store.set(k,v);},removeItem:k=>{store.delete(k);} });
    const sound = vi.spyOn(Sounds.prototype, "play").mockImplementation(()=>{});
    fb.onView(pick(), "meridian");fb.onView(pick("ines_okafor"), "meridian");
    expect(sound).toHaveBeenCalledExactlyOnceWith("crew.ines_okafor");
    fb.onView(pick("ines_okafor"), "meridian");expect(sound).toHaveBeenCalledTimes(1);
    fb.leaveController();fb.onView(pick("ines_okafor"), "meridian");expect(sound).toHaveBeenCalledTimes(1);
    fb.onView(view("debrief", {}), "meridian");fb.onView(pick(), "meridian");fb.setMuted(true);
    expect(store.get(MUTE_KEY)).toBe("1");fb.onView(pick("kit_sato"), "meridian");
    fb.setMuted(false);fb.onView(pick("kit_sato"), "meridian");expect(sound).toHaveBeenCalledTimes(1);
  });
  it("the real controller lifecycle plays confirmation and resets history on unmount", () => {
    const fb = new Feedback({getItem:()=>null,setItem(){},removeItem(){}});
    const sound=vi.spyOn(Sounds.prototype,"play").mockImplementation(()=>{});
    const screen=(v:CustomView)=>h(FeedbackContext.Provider,{value:fb},h(ControllerScreen,{view:v,send:()=>{},me:"A",game:"meridian"}));
    const mounted=render(screen(pick()));mounted.rerender(screen(pick("tobe_raske")));
    expect(sound).toHaveBeenCalledExactlyOnceWith("crew.tobe_raske");mounted.unmount();
    render(screen(pick("tobe_raske")));expect(sound).toHaveBeenCalledTimes(1);
  });

  it("rejoining an unselected same-id view still arms the next real confirmation", () => {
    const fb = new Feedback({getItem:()=>null,setItem(){},removeItem(){}});
    const sound=vi.spyOn(Sounds.prototype,"play").mockImplementation(()=>{});
    const before=pick();fb.onView(before,"meridian");fb.leaveController();fb.onView(before,"meridian");
    fb.onView(pick("sable_quist"),"meridian");expect(sound).toHaveBeenCalledExactlyOnceWith("crew.sable_quist");
  });
  it("does not reinterpret a replayed view id as a new accepted selection", () => {
    const t=new CrewSelection(),before=pick();t.observe(before,"meridian");
    expect(t.observe({...pick("ines_okafor"),viewId:before.viewId},"meridian")).toBeUndefined();
    expect(t.observe(pick("ines_okafor"),"meridian")).toBe("crew.ines_okafor");
  });

  it("accepts the current game branch's unmodified server payloads", () => {
    for(const [accepted,cue] of [[actualSheet,"crew.ines_okafor"],[actualTurn,"crew.sable_quist"]] as const){
      const t=new CrewSelection();expect(t.observe(actualPick as CustomView,"meridian")).toBeUndefined();
      expect(t.observe({...accepted,viewId:"accepted"} as CustomView,"meridian")).toBe(cue);
    }
  });

  it("plays every accepted reselection, including changing back, without repeating a refresh", () => {
    const t=new CrewSelection();t.observe(pick(),"meridian");
    expect(t.observe(pick("ines_okafor"),"meridian")).toBe("crew.ines_okafor");
    expect(t.observe(pick("sable_quist"),"meridian")).toBe("crew.sable_quist");
    expect(t.observe(pick("sable_quist"),"meridian")).toBeUndefined();
    expect(t.observe(pick("ines_okafor"),"meridian")).toBe("crew.ines_okafor");
    expect(t.observe(view("sheet",{me:{id:"ines_okafor"}}),"meridian")).toBeUndefined();
  });
  it("a reconnected chosen baseline can make a fresh accepted change or skip directly to Explore", () => {
    const t=new CrewSelection();expect(t.observe(pick("ines_okafor"),"meridian")).toBeUndefined();
    expect(t.observe(pick("kit_sato"),"meridian")).toBe("crew.kit_sato");
    t.reset();t.observe(pick("ines_okafor"),"meridian");
    expect(t.observe(view("turn",{me:{id:"kit_sato"}}),"meridian")).toBe("crew.kit_sato");
  });

});
