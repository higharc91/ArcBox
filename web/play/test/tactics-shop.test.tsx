import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { Shop, tradeId } from "../src/games/tactics/shop";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it("creates trade UUIDs on HTTP LAN phones without crypto.randomUUID", () => {
  vi.stubGlobal("crypto", { getRandomValues: (bytes: Uint8Array) => bytes.fill(12) });
  expect(tradeId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
});
const data = {
  shop: { id: "herbalist", name: "Wenna's Remedies", keeper: "Wenna", greeting: "Coin first." }, gold: 100,
  items: [{ item: "potion", name: "Potion of Healing", price: 50, count: 2, can: true, detail: "Heal 2d4 + 2 HP." }],
  sell: [{ member: "me", item: "potion", name: "Potion of Healing", price: 25, count: 1 }],
  you: { member: "me", canBuy: true }, leader: true, feed: [],
};
it("sends a unique trade id and waits for server confirmation without changing the purse", () => {
  const send = vi.fn();
  render(<Shop data={data} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "Buy Potion of Healing" }));
  expect(send).toHaveBeenCalledWith("shopBuy", expect.objectContaining({ shop: "herbalist", item: "potion", count: 1, trade: expect.any(String) }));
  expect(screen.getByText("Your purse")).toBeTruthy();
  expect(screen.getByText("100 gold")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Buy Potion of Healing" }).hasAttribute("disabled")).toBe(true);
});
it("only offers the server's own-bag sell rows and sends the member", () => {
  const send = vi.fn();
  render(<Shop data={data} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("tab", { name: "Sell" }));
  fireEvent.click(screen.getByRole("button", { name: "Sell Potion of Healing" }));
  expect(send).toHaveBeenCalledWith("shopSell", expect.objectContaining({ member: "me", count: 1 }));
});
it("disables purchases for spectators and keeps leaving with the leader", () => {
  const send = vi.fn();
  render(<Shop data={{ ...data, leader: false, you: { canBuy: false }, items: [{ ...data.items[0], can: false }] }} send={send} stream={send} viewId="one" />);
  expect(screen.getByRole("button", { name: "Buy Potion of Healing" }).hasAttribute("disabled")).toBe(true);
  expect(screen.queryByRole("button", { name: "Leave shop" })).toBeNull();
});
it("lets the VIP end a shop session through the existing confirmation menu", () => {
  const send = vi.fn();
  render(<Shop data={{ ...data, vip: true }} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "End session" }));
  expect(send).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Yes, end it" }));
  expect(send).toHaveBeenCalledWith("endSession");
});
it("lets the leader haggle once a visit, and shows what came of it with the server's prices", () => {
  const send = vi.fn();
  const haggle = { can: true, dc: 15, result: null };
  const { rerender } = render(<Shop data={{ ...data, haggle }} send={send} stream={send} viewId="one" />);
  fireEvent.click(screen.getByRole("button", { name: "Haggle · Persuasion DC 15" }));
  expect(send).toHaveBeenLastCalledWith("shopHaggle", { shop: "herbalist" });
  const passed = { can: false, dc: 15, result: { member: "me", name: "Ana", die: 14, bonus: 3, total: 17, dc: 12, passed: true } };   // the DC it was rolled against
  rerender(<Shop data={{ ...data, haggle: passed, items: [{ ...data.items[0]!, price: 45 }] }} send={send} stream={send} viewId="two" />);
  expect(screen.queryByRole("button", { name: /Haggle/ })).toBeNull();
  expect(screen.getByText(/Ana rolled 14 \+ 3 = 17 vs DC 12/)).toBeTruthy();
  expect(screen.getByText("45 gold each · 2 in stock")).toBeTruthy();
  rerender(<Shop data={{ ...data, leader: false, haggle: { ...haggle, can: false } }} send={send} stream={send} viewId="three" />);
  expect(screen.getByText("The leader can haggle once this visit (Persuasion DC 15).")).toBeTruthy();
});
it("has no haggling at an inn or a temple", () => {
  render(<Shop data={data} send={vi.fn()} stream={vi.fn()} viewId="one" />);
  expect(screen.queryByText(/haggle/i)).toBeNull();
});
