import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { Journal, QuestLog, WalkJournal, type JournalData } from "../src/games/tactics/quests";
afterEach(cleanup);
const data: JournalData = { canPin:true, selected:"grain", reading:"Mara", quests:[{
  id:"grain", name:"Where the Grain Went", status:"active", giver:"Halden", objective:"Inspect the sacks",
  history:[{log:"The mayor asked for help.",done:false,objective:"Inspect the sacks"}],
}, {id:"forge",name:"A Cold Forge",status:"active",giver:"Tobin",objective:"Find fuel",history:[]}] };
it("mirrors leader selections and sends pin and unpin requests", () => {
 const send=vi.fn(); const { rerender }=render(<Journal journal={data} send={send} shared />);
 fireEvent.click(screen.getByRole("button",{name:"Pin quest"}));
 expect(send).toHaveBeenLastCalledWith("questLog",{action:"pin",quest:"grain"});
 rerender(<Journal journal={{...data,pinned:"grain"}} send={send} shared />);
 fireEvent.click(screen.getByRole("button",{name:"Unpin quest"}));
 expect(send).toHaveBeenLastCalledWith("questLog",{action:"unpin",quest:"grain"});
 fireEvent.click(screen.getByRole("button",{name:/A Cold Forge/}));
 expect(send).toHaveBeenLastCalledWith("questLog",{action:"select",quest:"forge"});
 fireEvent.click(screen.getByRole("button",{name:"Close"}));
 expect(send).toHaveBeenLastCalledWith("questLog",{action:"close"});
});
it("private follower browsing does not send selections or offer a pin", () => {
 const send=vi.fn(); render(<Journal journal={{...data,canPin:false}} send={send} />);
 fireEvent.click(screen.getByRole("button",{name:/A Cold Forge/}));
 expect(send).not.toHaveBeenCalled(); expect(screen.queryByRole("button",{name:"Pin quest"})).toBeNull();
 expect(screen.getByRole("article",{name:"A Cold Forge"})).toBeTruthy();
});
it("allows a follower to read privately while the leader reads on TV", () => {
 const send=vi.fn(); render(<QuestLog data={{...data,canPin:false}} send={send} stream={send} viewId="q" />);
 fireEvent.click(screen.getByRole("button",{name:"Read privately"}));
 fireEvent.click(screen.getByRole("button",{name:/A Cold Forge/}));
 expect(send).not.toHaveBeenCalled(); expect(screen.getByRole("article",{name:"A Cold Forge"})).toBeTruthy();
});
it("opens a pinned quest privately and releases the walking pad", () => {
 const send=vi.fn(),stop=vi.fn(); render(<WalkJournal journal={data} pinned={{id:"grain",name:"Where the Grain Went",objective:"Inspect the sacks"}} send={send} stop={stop} />);
 fireEvent.click(screen.getByRole("button",{name:/Where the Grain Went/}));
 expect(stop).toHaveBeenCalledOnce(); expect(screen.getByRole("dialog")).toBeTruthy(); expect(send).not.toHaveBeenCalled();
});
it("keeps completed quests read-only and shows received rewards", () => {
 const send=vi.fn(); render(<Journal journal={{...data,quests:[{...data.quests[0]!,status:"done",rewards:{gold:30,xp:100,items:[]}}]}} send={send} />);
 expect(screen.queryByRole("button",{name:"Pin quest"})).toBeNull(); expect(screen.getByText(/100 XP per member/)).toBeTruthy();
});
it("a long press pins an active quest banner once without opening private details", () => {
 vi.useFakeTimers();const send=vi.fn();
 render(<WalkJournal journal={data} send={send} stop={()=>{}} notice={{id:"grain",name:"Where the Grain Went",kind:"start",objective:"Inspect the sacks",canPin:true}} />);
 const banner=screen.getByRole("button",{name:/Hold to pin/});
 fireEvent.pointerDown(banner);vi.advanceTimersByTime(610);fireEvent.pointerUp(banner);fireEvent.click(banner);
 expect(send).toHaveBeenCalledExactlyOnceWith("questLog",{action:"pin",quest:"grain"});expect(screen.queryByRole("dialog")).toBeNull();
 vi.useRealTimers();
});
it("completion banners cannot be pinned", () => {
 vi.useFakeTimers();const send=vi.fn();
 render(<WalkJournal journal={data} send={send} stop={()=>{}} notice={{id:"grain",name:"Where the Grain Went",kind:"complete",objective:"Quest complete",canPin:false}} />);
 const banner=screen.getByRole("button",{name:/Quest complete/});fireEvent.pointerDown(banner);vi.advanceTimersByTime(900);fireEvent.pointerUp(banner);
 expect(send).not.toHaveBeenCalled();vi.useRealTimers();
});
it("presents only this phone's trinket and opens its effects without pinning a completed bounty", () => {
 const send=vi.fn(), stop=vi.fn();const notice={
  id:"sq_ashmaw",name:"The Cinder Hollow Bounty",kind:"complete",objective:"Quest complete",canPin:false,
  trinket:{id:"ember_claw_gauntlet",name:"Ember-Claw Gauntlet",text:"Your attacks gain +1.",rarity:"rare",class:"fighter",className:"Fighter",active:true},
 };const {rerender}=render(<WalkJournal journal={{...data,quests:[]}} send={send} stop={stop} notice={notice} />);
 expect(screen.getByText("Presented to you")).toBeTruthy();
 expect(screen.getByRole("dialog")).toBeTruthy();
 expect(screen.getByText("Your attacks gain +1.")).toBeTruthy();
 // A snapshot refresh must not reopen a dismissed gift or stop walking again.
 fireEvent.click(screen.getByRole("button",{name:"Close"}));
 rerender(<WalkJournal journal={{...data,quests:[]}} send={send} stop={stop} notice={{...notice}} />);
 expect(screen.queryByRole("dialog")).toBeNull();
 expect(stop).toHaveBeenCalledOnce();
 fireEvent.click(screen.getByRole("button",{name:/Trinket: Ember-Claw Gauntlet/}));
 expect(screen.getByRole("dialog")).toBeTruthy();
 expect(send).not.toHaveBeenCalled();
});

it("lists the party's keys for everyone, with where each was found and whether it has been used", () => {
 const send=vi.fn();
 render(<Journal journal={{...data,canPin:false,keys:[{id:"brass_key",name:"brass key",found:"The Vault",used:false},{id:"reeve",name:"reeve's key",found:null,used:true}]}} send={send} />);
 const ring=screen.getByRole("region",{name:"Keys"});
 expect(ring.textContent).toContain("brass key"); expect(ring.textContent).toContain("Found: The Vault · Not used yet");
 expect(ring.textContent).toContain("reeve's key"); expect(ring.textContent).toContain("Used");
 expect(send).not.toHaveBeenCalled();
});
it("has no key ring while the party holds no key", () => {
 render(<Journal journal={data} send={vi.fn()} />);
 expect(screen.queryByRole("region",{name:"Keys"})).toBeNull();
});
it("keeps a shared leader journal open while reading and stops its heartbeat on close", () => {
 vi.useFakeTimers();
 try {
  const send=vi.fn(); const {unmount}=render(<Journal journal={data} send={send} shared />);
  vi.advanceTimersByTime(2500);
  expect(send).toHaveBeenLastCalledWith("questLog",{action:"keepOpen"});
  unmount(); send.mockClear(); vi.advanceTimersByTime(5000);
  expect(send).not.toHaveBeenCalled();
 } finally { vi.useRealTimers(); }
});
