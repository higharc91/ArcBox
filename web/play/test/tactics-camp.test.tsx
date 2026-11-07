import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { Camp } from "../src/games/tactics/camp";
import { Shop } from "../src/games/tactics/shop";
import { Dialogue, Vote } from "../src/games/tactics/story";
import dialogue from "./fixtures/tactics/dialogue.json";
import vote from "./fixtures/tactics/vote.json";

afterEach(cleanup);
const camp = { stage: "jobs", canPick: true, leader: true, vip: false, you: "ana", job: null,
  jobs: [{ id: "tend", name: "Tend wounds", skill: "medicine", dc: 12 }, { id: "cook", name: "Cook", skill: "survival", dc: 10 }],
  targets: [], party: [{ member: "ana", name: "Ana" }] };
it("lets each player pick their own camp job without nominating another roller", () => {
  const send = vi.fn(); render(<Camp data={camp} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: /Cook/ }));
  expect(send).toHaveBeenCalledWith("campJob", { job: "cook" });
  expect(screen.queryByText("Who rolls?")).toBeNull();
});
it("shows the exact injury target and refuses injuries reserved by another tender", () => {
  const send = vi.fn(); render(<Camp data={{ ...camp, job: "tend", party: [{ member: "bo", name: "Bo" }, { member: "cy", name: "Cy" }], targets: [
    { member: "bo", name: "Bo", injury: "ribs", label: "Cracked ribs", detail: "Maximum HP −5", can: true, selected: false },
    { member: "cy", name: "Cy", injury: "ribs", label: "Cracked ribs", detail: "Maximum HP −5", can: false, selected: false },
  ] }} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "Cy: Cracked ribs" }));
  expect(screen.getByRole("button", { name: "Being tended" }).hasAttribute("disabled")).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Close" }));
  fireEvent.click(screen.getByRole("button", { name: "Bo: Cracked ribs" }));
  fireEvent.click(screen.getByRole("button", { name: "Help Bo" }));
  expect(send).toHaveBeenCalledWith("campJob", { job: "tend", target: "bo", injury: "ribs" });
});
const service = { shop: { id: "chapel", name: "Chapel", keeper: "Priest", greeting: "Welcome" }, gold: 50,
  items: [], sell: [], you: { member: "ana", canBuy: true }, leader: false, feed: [],
  services: [{ id: "heal", kind: "cure", name: "Healing", price: 25 }],
  guests: [{ member: "bo", name: "Bo", room: false, injuries: [{ id: "ribs", name: "Cracked ribs", kind: "body", detail: "HP −5" }] }] };
it("pays for another character's chosen injury with a unique service trade id", () => {
  const send = vi.fn(); render(<Shop data={service} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "Bo: Cracked ribs" }));
  fireEvent.click(screen.getByRole("button", { name: "Heal Bo's Cracked ribs · 25 gold" }));
  expect(send).toHaveBeenCalledWith("shopService", expect.objectContaining({ shop: "chapel", service: "heal", member: "bo", injury: "ribs", trade: expect.any(String) }));
  expect(screen.getByText("50 gold")).toBeTruthy();
});
it("disables a cure when the viewer's own purse cannot afford it", () => {
  const send = vi.fn(); render(<Shop data={{ ...service, gold: 10 }} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "Bo: Cracked ribs" }));
  expect(screen.getByRole("button", { name: "Heal Bo's Cracked ribs · 25 gold" }).hasAttribute("disabled")).toBe(true);
});
it("buys a friend's room once the party's night has been agreed", () => {
  const send = vi.fn(); render(<Shop data={{ ...service, roomsOpen: true, services: [{ id: "room", name: "A room", kind: "room", price: 10 }] }} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "Buy room for Bo · 10 gold" }));
  expect(send).toHaveBeenCalledWith("shopService", expect.objectContaining({ member: "bo", service: "room", trade: expect.any(String) }));
  expect(screen.queryByRole("button", { name: "Leave shop" })).toBeNull();
});

it("shows compact battle-ready bars and keeps injury effects behind a tap", () => {
  const send = vi.fn();
  render(<Shop data={{ ...service, members: [{ member: "bo", name: "Bo", class: "cleric", here: false, hp: 9, maxHp: 9,
    resources: [{ name: "devotion", current: 4, max: 4 }], injuries: service.guests[0]!.injuries, wellFed: false, room: false }] }} send={send} stream={send} viewId="one" />);
  expect(screen.getByRole("progressbar", { name: "Bo HP" }).getAttribute("aria-valuenow")).toBe("9");
  expect(screen.getByRole("progressbar", { name: "Bo Devotion" }).getAttribute("aria-valuemax")).toBe("4");
  expect(screen.getByText("Away")).toBeTruthy();
  expect(screen.queryByText("HP −5")).toBeNull();
  expect(screen.queryByRole("button", { name: "Heal Bo's Cracked ribs · 25 gold" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Bo: Cracked ribs" }));
  expect(screen.getByText("HP −5")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Heal Bo's Cracked ribs · 25 gold" })).toBeTruthy();
});

it("shows each phone its own character and changes the little scene with the real camp stage", () => {
  const send = vi.fn();
  const members = ["ana", "bo"].map((member, i) => ({ member, name: member, class: i ? "cleric" : "fighter", here: true,
    hp: 10, maxHp: 10, resources: [], injuries: [], wellFed: false, room: false }));
  const data = { ...camp, members, party: [{ member: "ana", name: "Ana", job: "cook" }, { member: "bo", name: "Bo" }] };
  const view = render(<Camp data={data} send={send} stream={send} viewId="one" />);
  expect(view.container.querySelectorAll(".camp-person")).toHaveLength(2);
  expect(view.container.querySelectorAll(".camp-heart")).toHaveLength(1);
  expect(view.container.querySelector(".camp-pot")).toBeTruthy();
  view.rerender(<Camp data={{ ...data, stage: "sleep" }} send={send} stream={send} viewId="two" />);
  expect(view.container.querySelectorAll(".camp-zzz")).toHaveLength(2);
  expect(view.container.querySelector(".camp-pot")).toBeNull();
  view.rerender(<Camp data={{ ...data, stage: "dawn" }} send={send} stream={send} viewId="three" />);
  expect(view.container.querySelector(".tx-camp-scene.dawn")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Continue" })).toBeTruthy();
});


it("keeps the little camp on each phone during fireside dialogue and votes", () => {
  const send = vi.fn();
  const campScene = { stage: "talk", you: "ana", members: [{ member: "ana", class: "fighter", here: true }] };
  const view = render(<Dialogue data={{ ...dialogue.data, campScene }} send={send} stream={send} viewId="one" />);
  expect(view.container.querySelector(".camp-heart")).toBeTruthy();
  expect(screen.getByText(/Forty of them/)).toBeTruthy();
  view.rerender(<Vote data={{ ...vote.data, campScene }} send={send} stream={send} viewId="two" />);
  expect(view.container.querySelector(".camp-heart")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: /parley with the chief/ }));
  expect(send).toHaveBeenCalledWith("vote", { option: "parley" });
});

it("explains each job on the button, using the player's actual skill and target", () => {
  const send = vi.fn();
  render(<Camp data={{ ...camp, jobs: [
    ...camp.jobs,
    { id: "rally", name: "Rally", skill: "performance", dc: 14 },
    { id: "watch", name: "Keep watch", skill: "perception", dc: 0 },
    { id: "rest", name: "Rest easy", skill: "survival", dc: 0 },
  ] }} send={send} stream={send} viewId="one" />);
  expect(screen.getByText("Mend one body injury on a successful roll.")).toBeTruthy();
  expect(screen.getByText("Give everyone 3 temporary HP next battle on a successful roll.")).toBeTruthy();
  expect(screen.getByText("Performance · roll 14+")).toBeTruthy();
  expect(screen.getByText("Perception · varies by secret")).toBeTruthy();
  expect(screen.getByText("No roll")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Rally" }).getAttribute("aria-describedby")).toBe("camp-job-rally");
  expect(send).not.toHaveBeenCalled();
});

it("labels Well fed and injuries visibly without relying on hover", () => {
  const send = vi.fn();
  const { container } = render(<Shop data={{ ...service, members: [{ member: "bo", name: "Bo", class: "cleric", here: true,
    hp: 9, maxHp: 9, resources: [], injuries: service.guests[0]!.injuries, wellFed: true, room: false }] }} send={send} stream={send} viewId="one" />);
  expect(screen.getByText("Well fed")).toBeTruthy();
  expect(screen.getByText("3 temporary HP next battle")).toBeTruthy();
  expect(screen.getByText("Cracked ribs")).toBeTruthy();
  expect(container.querySelectorAll("[title]")).toHaveLength(0);
  fireEvent.click(screen.getByRole("button", { name: "Bo: Cracked ribs" }));
  expect(screen.getByText("HP −5")).toBeTruthy();
});

it("explains taking no extra job and rolling twice for a healing spell", () => {
  const send = vi.fn();
  render(<Camp data={camp} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "About jobs" }));
  expect(screen.getByText(/If you know a healing spell, roll twice and keep the higher result/)).toBeTruthy();
  expect(screen.getByText(/Choose Rest, or leave your job unchosen/)).toBeTruthy();
});
