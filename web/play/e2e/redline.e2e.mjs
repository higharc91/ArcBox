#!/usr/bin/env node
// Browser playthrough against a running ArcBox server: bots tap the phone app like players would.
//
//   node web/play/e2e/redline.e2e.mjs            REDLINE on /dev.html?phones=3, stop after the first Reveal
//   ARCBOX_FULL=1 node …                         race to the chequered flag, the podium (the podium phones tap their
//                                                champagne bottles to charge them and pop the corks), results, and
//                                                back to the lobby
//   ARCBOX_GAME=counter ARCBOX_PHONES=6 node …   a full Counter game
//   ARCBOX_ROOM=ABCD node …                      join a room hosted elsewhere (the TV app) with N phone pages
//
// Environment:
//   ARCBOX_URL      server (default http://localhost:5080); this script starts nothing itself
//   ARCBOX_PHONES   number of phones (default 3)
//   ARCBOX_GAME     redline (default) or counter
//   ARCBOX_FULL     1 = play REDLINE to the end; Counter always plays to the end
//   ARCBOX_ROOM     join this room code instead of hosting on /dev.html; the VIP phone (if ours) picks and starts
//   ARCBOX_START    phone (default: the VIP phone picks the game and starts) or host (the dev page's host panel;
//                   with ARCBOX_ROOM, the TV starts the game, e.g. `--autostart=redline:3`, and the phones wait)
//   ARCBOX_REJOIN   0 to skip the mid-game reload of phone 2 (default on)
//   ARCBOX_SHOTS    screenshot folder (default web/play/e2e/shots, git-ignored)
//   ARCBOX_HEADED   1 to watch it in a visible browser
//   ARCBOX_PACE     multiplier for the bots' reading time before each tap (default 1: 0.25–2.2 s); in qualifying,
//                   bot k answers after (1 + k) s times the pace, capped so the slowest bot still beats the 10 s window
//   ARCBOX_NAME     with ARCBOX_ROOM: name prefix for the phones (default Bot: Bot1, Bot2…)
//
// REDLINE qualifying (before the Grid): bot k answers every lap after (1 + k) s (see ARCBOX_PACE), always right
// except bot 3 on lap 2, using the answer key in the fallback question pack (the Development server has OpenTDB off).
// The run logs the bots' expected grid order as `QUALI order <CODE> <CODE> …` (fastest first) for tools/tv-e2e.sh.
//
// Exit code 0 when the run reached its goal with no page errors; 1 otherwise.

import { createRequire } from "node:module";
import { mkdirSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const { chromium } = await loadPlaywright();

const env = process.env;
const BASE = (env.ARCBOX_URL ?? "http://localhost:5080").replace(/\/$/, "");
const PHONES = clampInt(env.ARCBOX_PHONES, 3, 1, 8);
const GAME = (env.ARCBOX_GAME ?? "redline").toLowerCase();
const FULL = env.ARCBOX_FULL === "1" || process.argv.includes("--full");
const ROOM = env.ARCBOX_ROOM?.trim().toUpperCase() || null;
const START = env.ARCBOX_START === "host" ? "host" : "phone";
const REJOIN = env.ARCBOX_REJOIN !== "0";
const SHOTS = resolve(env.ARCBOX_SHOTS ?? join(here, "shots"));
const HEADED = env.ARCBOX_HEADED === "1";
/** Multiplies the bots' reading time; > 1 keeps each phase on screen longer (a slow TV renderer needs that). */
const PACE = Math.min(10, Math.max(0.1, Number(env.ARCBOX_PACE) || 1));
/** Room mode: phones join as `${NAME}1`, `${NAME}2`… (a second run in the same room needs new names). */
const NAME = (env.ARCBOX_NAME ?? "Bot").replace(/[^A-Za-z]/g, "").slice(0, 8) || "Bot";
/** Plays to the end (results and back to the lobby) rather than stopping at the first Reveal. */
const TO_THE_END = FULL || GAME !== "redline";
const DEADLINE_MS = (TO_THE_END ? 20 : 4) * 60_000;
/** Longest a phone may show the same view before the run counts as stuck (phase timers are ≤ 20 s). */
const STUCK_MS = 75_000;

// ---------------------------------------------------------------- qualifying plan

/** RedlineRules.QualifyingLaps and QualifyingAnswer. */
const QUALI_LAPS = 3;
const QUALI_WINDOW_S = 10;
/**
 * Seconds bot k waits on a qualifying question: (1 + k) × the pace, the pace capped so the slowest bot answers within
 * 70% of the window (with ARCBOX_PACE=4 and 3 phones: 3.5, 5.25 and 7 s).
 */
const QUALI_SCALE = Math.min(PACE, (0.7 * QUALI_WINDOW_S) / (1 + PHONES));
const qualiDelayS = (slot) => (1 + slot) * QUALI_SCALE;
/** The one planned wrong answer: bot 3, lap 2 (its lap is deleted, so it starts behind bots 1 and 2). */
const answersWrong = (slot, lap) => slot === 3 && lap === 2;

/** Question text → correct answer, from the fallback pack the Development server plays with. */
const answerKey = new Map(
  JSON.parse(readFileSync(join(here, "../../../games/Redline/Redline.Logic/Data/fallback-questions.json"), "utf8")).map((q) => [norm(q.text), q.correct]),
);
function norm(text) {
  return String(text).replace(/\s+/g, " ").trim();
}

mkdirSync(SHOTS, { recursive: true });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const rand = (min, max) => min + Math.random() * (max - min);
const pick = (xs) => xs[Math.floor(Math.random() * xs.length)];
const t0 = Date.now();
const log = (...args) => console.log(`[${((Date.now() - t0) / 1000).toFixed(1).padStart(6)}s]`, ...args);

const problems = [];
const fail = (msg) => {
  problems.push(msg);
  log("PROBLEM:", msg);
};

// ---------------------------------------------------------------- phone abstraction

/**
 * A phone is a locator root (an iframe on the dev page, or a page of its own in room mode),
 * plus a way to reload it like a player pulling down to refresh.
 */
function devPhone(page, slot) {
  const frame = page.frameLocator(`iframe[title="Phone ${slot}"]`);
  return {
    slot,
    $: (sel) => frame.locator(sel),
    reload: () => page.getByRole("button", { name: `Reload phone ${slot}` }).click(),
    shot: (name) => page.locator(`iframe[title="Phone ${slot}"]`).screenshot({ path: join(SHOTS, name) }),
  };
}

function roomPhone(page, slot) {
  return {
    slot,
    $: (sel) => page.locator(sel),
    reload: () => page.reload(),
    shot: (name) => page.screenshot({ path: join(SHOTS, name) }),
  };
}

/** What the phone shows right now, read in one round trip. */
async function snapshot(phone) {
  try {
    return await phone.$("body").evaluate(() => {
      const screen = document.querySelector(".screen");
      const cls = screen?.className ?? "";
      const name = ["join", "pick", "connecting", "lobby", "controller", "results", "closed"].find((s) => cls.split(" ").includes(s)) ?? "blank";
      const kind = cls.match(/\bk-(\w+)/)?.[1] ?? null;
      // Text nodes joined by spaces: innerText would apply CSS text-transform, textContent glues "P5" to "ROUND".
      const text = (sel) => {
        const el = document.querySelector(sel);
        if (!el) return "";
        const parts = [];
        const walk = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
        while (walk.nextNode()) parts.push(walk.currentNode.nodeValue);
        return parts.join(" ").replace(/\s+/g, " ").trim();
      };
      return {
        screen: name,
        kind,
        game: screen?.getAttribute("data-game") ?? null,
        header: text(".pstatus .pos"),
        prompt: text(".prompt"),
        message: text(".w-wait"),
        error: text(".form-error"),
        enabled: [...document.querySelectorAll(".widget button[data-id]:not([disabled])")].map((b) => b.dataset.id),
        // Choice options as shown ("Oscar Piastri" for a badged driver): for driver codes and the qualifying answer key.
        options: [...document.querySelectorAll(".w-choice button[data-id]")].map((b) => ({ id: b.dataset.id, label: b.querySelector(".opt-label")?.textContent?.trim() ?? "" })),
        // The champagne bottle (podium): "holding" while the anthem plays, "armed" once the cork can pop.
        shake: document.querySelector(".w-shake")?.classList.contains("armed") ? "armed" : document.querySelector(".w-shake") ? "holding" : null,
        mash: !!document.querySelector(".pbtns.single"),
        vip: !!document.querySelector(".picker"),
        canStart: !!document.querySelector(".picker .start:not([disabled])"),
        picked: (document.querySelector(".game-card.picked .gname") ?? document.querySelector(".next-game b"))?.textContent ?? null,
      };
    }, undefined, { timeout: 3000 });
  } catch {
    return { screen: "unreachable", enabled: [] }; // mid-reload, or the frame is being replaced
  }
}

/** A phase name for logs, screenshots and the run's progress; works from the phone alone (no TV needed). */
function phaseOf(s) {
  if (s.screen !== "controller") return s.screen;
  const m = s.message;
  if (GAME === "counter" || s.game === "counter") return s.kind === "buttons" ? "mash" : `counter-${s.kind}`;
  if (s.kind === "choice" && s.prompt === "Pick your driver") return "driver-select";
  // Qualifying (PlayerViews: QualifyingListening, Answer, LapChecking, LapResult), header "QUALIFYING · LAP n/3".
  if (s.kind === "wait" && /Answers in a moment/.test(m)) return "quali-reading";
  if (s.kind === "choice" && /^QUALIFYING/.test(s.header ?? "")) return "quali-question";
  if (s.kind === "wait" && /^Lap time .* checking/.test(m)) return "quali-answered";
  if (s.kind === "wait" && /^[✔✖]/.test(m)) return "quali-result";
  if (s.kind === "wait" && /Waiting for the grid/.test(m)) return "seated";
  if (s.kind === "wait" && /Lights out/.test(m)) return "grid";
  if (s.kind === "buttons" && s.enabled.includes("cruise")) return "throttle";
  if (s.kind === "buttons") return "throttle-sent";
  if (s.kind === "wait" && /Answers open in a moment/.test(m)) return "reading"; // the question, read out on the TV
  if (s.kind === "wait" && /^(Correct!|Wrong answer|No answer|Spun out!)/.test(m)) return "reveal"; // before "Pit limiter"
  if (s.kind === "wait" && /^Locked in|^Pit limiter/.test(m)) return "throttle-locked";
  if (s.kind === "choice") return "question";
  if (s.kind === "wait" && /Answer locked/.test(m)) return "answered";
  if (s.kind === "wait" && /^You finished P/.test(m)) return "chequered";
  if (s.kind === "shake") return s.shake === "armed" ? "champagne" : "anthem";
  if (s.kind === "wait" && /Watch the podium/.test(m)) return "podium-watch";
  return `controller-${s.kind}`;
}

const roundOf = (s) => Number(/ROUND (\d+)/.exec(s.header ?? "")?.[1] ?? 0);
const lapOf = (s) => Number(/LAP (\d+)/.exec(s.header ?? "")?.[1] ?? 0);

/** Driver name → code from the Driver Select options, and phone slot → its driver's code once a view names it. */
const codeByName = new Map();
const codeOf = new Map();
function learnDriver(slot, s) {
  if (phaseOf(s) === "driver-select") for (const o of s.options ?? []) if (o.label) codeByName.set(o.label, o.id);
  const name = /^You're ([^.,]+)[.,]/.exec(s.message ?? "")?.[1];
  if (name && codeByName.has(name)) codeOf.set(slot, codeByName.get(name));
}

// ---------------------------------------------------------------- the bot

/**
 * Plays one phone until `stop()` says so: taps a random enabled option after a human-ish pause, mashes the
 * Counter button, and occasionally sits a decision out so the "No answer" and timer paths run too.
 * Every action has a short timeout: a view that changes under the bot's finger is simply re-read next tick.
 */
async function bot(phone, stop, onSnapshot) {
  let decidedFor = null;
  let lapSeen = null; // the qualifying question on screen and when this bot first saw it (a retried tap keeps the time)
  while (!stop()) {
    const s = await snapshot(phone);
    onSnapshot(phone, s);
    const viewKey = `${s.kind}|${s.header}|${s.prompt}|${s.message}|${s.enabled.join(",")}`;
    try {
      if (s.screen === "controller" && s.kind === "shake") {
        await shake(phone, s);
        continue;
      }
      if (s.screen === "controller" && s.mash) {
        await phone.$(".pbtns.single button").click({ timeout: 1000 });
        await sleep(rand(60, 220));
        continue;
      }
      if (s.screen === "controller" && phaseOf(s) === "quali-question" && s.enabled.length && decidedFor !== viewKey) {
        decidedFor = viewKey;
        if (lapSeen?.key !== viewKey) lapSeen = { key: viewKey, at: Date.now() };
        await answerLap(phone, s, lapSeen.at);
      } else if (s.screen === "controller" && s.enabled.length && decidedFor !== viewKey) {
        decidedFor = viewKey;
        if (Math.random() >= 0.06) {
          await sleep(rand(250, 2200) * PACE); // reading time
          await act(phone, s);
        } else {
          log(`P${phone.slot} sits out ${phaseOf(s)}: the phase timer decides`);
        }
      } else if (s.screen === "controller" && s.kind === "textEntry" && decidedFor !== viewKey) {
        decidedFor = viewKey;
        await phone.$(".w-text input").fill(pick(["Vroom", "Box box", "Full send", "Lift and coast"]), { timeout: 1000 });
        await phone.$(".w-text button[type=submit]").click({ timeout: 1000 });
      } else if (s.screen === "controller" && s.kind === "draw" && decidedFor !== viewKey) {
        decidedFor = viewKey;
        await scribble(phone);
      }
    } catch (err) {
      // The view moved on before the tap landed (or the tap failed): look again and retry if it's still there.
      decidedFor = null;
      if (s.kind === "shake") log(`P${phone.slot} bottle: ${String(err?.message ?? err).split("\n")[0]}`);
    }
    await sleep(rand(150, 400));
  }
}

/** Per phone: champagne pops made and the strongest charge popped. */
const pops = new Map();

/**
 * The podium's champagne bottle (the Shake widget). Desktop Chromium has `DeviceMotionEvent.requestPermission`, so the
 * widget first offers "Tap to enable shaking"; with no motion samples it falls back to tapping the bottle after 1.5 s.
 * Each phone charges by a different number of taps (P1 most), so the sprays on the TV differ in strength; once the
 * Champagne arms the bottle, POP! fires and the bot charges again for the next one.
 */
async function shake(phone, s) {
  const w = (sel) => phone.$(`.w-shake ${sel}`);
  // The bottle wobbles after every tap, so Playwright would wait for it to keep still: click through (force).
  const press = (sel) => w(sel).click({ timeout: 1000, force: true });
  if (await w(".enable-motion").count()) {
    await press(".enable-motion");
    log(`P${phone.slot} enables shaking (no motion here: the tap fallback follows)`);
    await sleep(1700);
    return;
  }
  const taps = Math.max(4, 18 - 4 * phone.slot + Math.floor(rand(-1, 2)));
  for (let i = 0; i < taps; i++) {
    await press(".bottle-tap");
    await sleep(rand(45, 90));
  }
  if (s.shake !== "armed") {
    await sleep(rand(250, 600));
    return;
  }
  const charge = Number(await phone.$(".w-shake").evaluate((el) => getComputedStyle(el).getPropertyValue("--charge")).catch(() => NaN));
  await press(".pop-btn:not([disabled])");
  const p = pops.get(phone.slot) ?? { count: 0, best: 0 };
  p.count++;
  p.best = Math.max(p.best, Number.isFinite(charge) ? charge : 0);
  pops.set(phone.slot, p);
  log(`P${phone.slot} pops the cork at ${Number.isFinite(charge) ? charge.toFixed(2) : "?"} (pop ${p.count})`);
  await sleep(rand(500, 1100));
}

/**
 * A qualifying lap: wait out this bot's planned time since the question appeared, then tap the right answer (the
 * wrong one for bot 3 on lap 2). The phone's Choice widget measures the time and sends it as elapsedMs.
 */
async function answerLap(phone, s, seenAt) {
  const lap = lapOf(s);
  const correct = answerKey.get(norm(s.prompt));
  const wrong = answersWrong(phone.slot, lap);
  let option = correct === undefined ? undefined : s.options.find((o) => (o.label === correct) !== wrong && s.enabled.includes(o.id));
  if (!option) {
    log(`P${phone.slot} lap ${lap}: "${s.prompt}" is not in the fallback pack; guessing`);
    option = { id: pick(s.enabled), label: "?" };
  }
  await sleep(Math.max(0, seenAt + qualiDelayS(phone.slot) * 1000 - Date.now()));
  await phone.$(`.widget button[data-id="${option.id}"]`).click({ timeout: 1500 });
  log(`P${phone.slot} lap ${lap}: ${wrong ? "WRONG (planned)" : "right"} "${option.label}" after ${((Date.now() - seenAt) / 1000).toFixed(1)} s`);
}

/**
 * The bots' grid order qualifying should produce, from the plan alone: most correct laps, then the least total time
 * (every lap at the bot's delay), fastest first. Codes from the phones' own views; "?" when one never named its driver.
 */
function expectedQualiOrder() {
  return Array.from({ length: PHONES }, (_, i) => i + 1)
    .map((slot) => {
      const laps = Array.from({ length: QUALI_LAPS }, (_, i) => i + 1).filter((lap) => !answersWrong(slot, lap));
      return { slot, correct: laps.length, total: laps.length * qualiDelayS(slot), code: codeOf.get(slot) ?? "?" };
    })
    .sort((a, b) => b.correct - a.correct || a.total - b.total || a.slot - b.slot);
}

async function act(phone, s) {
  const multi = await phone.$(".w-choice .actions .primary").count();
  if (multi) {
    // Multiple choice: toggle one or two, then lock in.
    for (const id of [pick(s.enabled), pick(s.enabled)]) {
      await phone.$(`.w-choice .opt[data-id="${id}"]`).click({ timeout: 1000 }).catch(() => {});
    }
    await phone.$(".w-choice .actions .primary").click({ timeout: 1000 });
    return;
  }
  const id = pick(s.enabled);
  await phone.$(`.widget button[data-id="${id}"]`).click({ timeout: 1500 });
}

async function scribble(phone) {
  const box = await phone.$(".w-draw canvas").boundingBox();
  if (!box) return;
  const page = phone.$("body").page();
  await page.mouse.move(box.x + box.width * 0.2, box.y + box.height * 0.3);
  await page.mouse.down();
  for (let i = 1; i <= 8; i++) await page.mouse.move(box.x + box.width * (0.2 + i * 0.07), box.y + box.height * rand(0.2, 0.8));
  await page.mouse.up();
  await phone.$(".w-draw button.primary").click({ timeout: 1000 });
}

// ---------------------------------------------------------------- the run

const browser = await chromium.launch(HEADED ? { headless: false } : undefined);
let exitCode = 1;
try {
  exitCode = await run();
} catch (err) {
  fail(`crashed: ${err?.stack ?? err}`);
} finally {
  await browser.close();
}
console.log(problems.length ? `\nFAILED (${problems.length}):\n- ${problems.join("\n- ")}` : "\nOK");
console.log(`Screenshots: ${SHOTS}`);
process.exit(problems.length ? 1 : exitCode);

async function run() {
  const context = await browser.newContext({ viewport: ROOM ? { width: 390, height: 780 } : { width: 1600, height: 1400 } });
  const pages = [];
  const watchErrors = (page) => {
    page.on("pageerror", (err) => fail(`page error: ${err.message}`));
    page.on("console", (msg) => {
      if (msg.type() === "error") log(`console error (${page.url()}): ${msg.text()}`);
    });
  };

  // -------- open the host (dev page) or the phone pages
  let dev = null;
  let code = ROOM;
  let phones;
  if (ROOM) {
    phones = [];
    for (let slot = 1; slot <= PHONES; slot++) {
      const page = await context.newPage();
      watchErrors(page);
      pages.push(page);
      await page.goto(`${BASE}/?room=${ROOM}&slot=e2e${slot}&devName=${NAME}${slot}`);
      phones.push(roomPhone(page, slot));
    }
  } else {
    dev = await context.newPage();
    watchErrors(dev);
    await dev.goto(`${BASE}/dev.html?phones=${PHONES}`);
    await dev.waitForFunction(() => /[?&]room=[A-Z]{4}/.test(location.search), undefined, { timeout: 15_000 });
    code = new URL(dev.url()).searchParams.get("room");
    phones = Array.from({ length: PHONES }, (_, i) => devPhone(dev, i + 1));
  }
  log(`room ${code}, ${PHONES} phone(s), game ${GAME}${TO_THE_END ? " (to the end)" : " (until the first Reveal)"}`);

  // -------- everyone in the lobby
  await until("all phones in the lobby", async () => {
    const snaps = await Promise.all(phones.map(snapshot));
    const bad = snaps.find((s) => s.screen === "join" && s.error);
    if (bad) throw new Error(`join failed: ${bad.error}`);
    return snaps.every((s) => s.screen === "lobby" || s.screen === "controller");
  }, 20_000);
  await phones[0].shot("lobby.png");
  if (dev) await dev.screenshot({ path: join(SHOTS, "dev-lobby.png"), fullPage: true });

  // -------- pick the game and start
  if (START === "host" && ROOM) {
    log("waiting for the host to start the game");
    await until("the host to start the game", async () => (await Promise.all(phones.map(snapshot))).some((s) => s.screen === "controller"), 5 * 60_000);
  } else if (START === "host") {
    await dev.locator(".host-actions button", { hasText: new RegExp(`^${GAME}$`, "i") }).click();
    await dev.locator(".host-actions .primary:not([disabled])").click({ timeout: 10_000 });
  } else {
    const vip = await until("a VIP phone (or the host starting the game)", async () => {
      for (const p of phones) {
        const s = await snapshot(p);
        if (s.screen === "controller") return "started";
        if (s.vip) return p;
      }
      return null;
    }, ROOM ? 10 * 60_000 : 15_000);
    if (vip !== "started") {
      log(`P${vip.slot} is VIP: picks ${GAME} and starts`);
      await vip.$(`.game-card:has(.gname:text-is("${GAME === "redline" ? "REDLINE" : "Counter"}"))`).click();
      await vip.$(".picker .start:not([disabled])").waitFor({ timeout: 10_000 });
      await vip.shot("lobby-vip.png");
      await vip.$(".picker .start").click();
    }
  }

  // -------- play
  let done = false;
  const stop = () => done || Date.now() - t0 > DEADLINE_MS;
  const seen = new Map(); // slot -> {phase, since, key}
  const shots = new Set();
  const phasesSeen = new Set();
  let reachedReveal = false;
  let lastRound = 0;

  const onSnapshot = (phone, s) => {
    const phase = phaseOf(s);
    learnDriver(phone.slot, s);
    const key = `${phase}|${s.header}|${s.message}|${s.prompt}`;
    const prev = seen.get(phone.slot);
    if (!prev || prev.key !== key) {
      seen.set(phone.slot, { key, phase, since: Date.now() });
      if (phone.slot === 1 && phase !== prev?.phase) log(`P1 ${phase}${s.header ? `  [${s.header}]` : ""}${s.message ? `  "${s.message}"` : ""}`);
    }
    if (phone.slot !== 1 || s.screen === "unreachable") return;
    phasesSeen.add(phase);
    if (phase === "reveal") reachedReveal = true;
    lastRound = Math.max(lastRound, roundOf(s));
  };

  // Screenshots of phone 1 at each phase (first time seen), and the dev board at Reveal and Chequered.
  const shooter = (async () => {
    while (!stop()) {
      const s = await snapshot(phones[0]);
      const phase = phaseOf(s);
      const name = { "driver-select": "driver-select", seated: "seated", grid: "grid", throttle: "throttle", "throttle-locked": "throttle-locked",
        reading: "reading", question: "question", answered: "answered", reveal: "reveal", chequered: "chequered", mash: "mash", results: "results",
        anthem: "anthem", champagne: "champagne", "podium-watch": "podium-watch",
        "quali-reading": `quali-reading-lap${lapOf(s)}`, "quali-question": `quali-question-lap${lapOf(s)}`,
        "quali-answered": `quali-answered-lap${lapOf(s)}`, "quali-result": `quali-result-lap${lapOf(s)}` }[phase];
      if (name && !shots.has(name)) {
        shots.add(name);
        await sleep(250); // let fonts and the countdown settle
        await phones[0].shot(`${name}.png`).catch(() => shots.delete(name));
        if (dev && ["reveal", "chequered", "question", "driver-select", "mash", "anthem", "champagne", "quali-result"].includes(phase)) {
          await dev.screenshot({ path: join(SHOTS, `dev-${name}.png`), fullPage: true }).catch(() => {});
        }
      }
      // Bot 3's planned deleted lap, on its own phone.
      if (phones[2] && !shots.has("quali-deleted-P3")) {
        const s3 = await snapshot(phones[2]);
        if (phaseOf(s3) === "quali-result" && /^✖/.test(s3.message)) {
          shots.add("quali-deleted-P3");
          await sleep(250);
          await phones[2].shot("quali-deleted-P3.png").catch(() => shots.delete("quali-deleted-P3"));
        }
      }
      await sleep(200);
    }
  })();

  // Stuck detector: no phone may sit on one view for longer than any phase timer allows.
  const watchdog = (async () => {
    while (!stop()) {
      await sleep(2000);
      for (const [slot, v] of seen) {
        if (["lobby", "results", "unreachable"].includes(v.phase)) continue;
        if (Date.now() - v.since > STUCK_MS) {
          fail(`P${slot} stuck on ${v.phase} for ${Math.round((Date.now() - v.since) / 1000)}s`);
          await phones[slot - 1].shot(`stuck-P${slot}.png`).catch(() => {});
          v.since = Date.now();
        }
      }
    }
  })();

  const bots = phones.map((p) => bot(p, stop, onSnapshot));

  // -------- rejoin: mid-game, reload phone 2 and expect the current view back
  const rejoin = (async () => {
    if (!REJOIN || PHONES < 2) return;
    const p2 = phones[1];
    const midGame = GAME === "redline" ? ["throttle", "throttle-locked", "question", "answered"] : ["mash"];
    const before = await until("phone 2 mid-game", async () => {
      const s = await snapshot(p2);
      return midGame.includes(phaseOf(s)) ? s : null;
    }, 3 * 60_000);
    log(`rejoin: reloading P2 during ${phaseOf(before)} [${before.header}]`);
    await p2.reload();
    const after = await until("phone 2 back in the game after reload", async () => {
      const s = await snapshot(p2);
      if (s.screen === "join" || s.screen === "closed") throw new Error(`P2 came back on ${s.screen}${s.error ? `: ${s.error}` : ""}`);
      return s.screen === "controller" ? s : null;
    }, 15_000);
    await p2.shot("rejoin-P2.png");
    const sameRound = GAME !== "redline" || roundOf(after) >= roundOf(before);
    log(`rejoin: P2 back on ${phaseOf(after)} [${after.header}]${sameRound ? "" : " (an EARLIER round?)"}`);
    if (!sameRound) fail(`P2 rejoined into round ${roundOf(after)}, was in ${roundOf(before)}`);
  })().catch((err) => fail(`rejoin: ${err.message}`));

  // -------- the goal
  try {
    if (!TO_THE_END) {
      await until("the first Reveal", async () => reachedReveal, DEADLINE_MS);
      await sleep(600);
      if (dev) await dev.screenshot({ path: join(SHOTS, "dev-reveal.png"), fullPage: true });
    } else {
      await until("results on phone 1", async () => (await snapshot(phones[0])).screen === "results", DEADLINE_MS);
      phasesSeen.add("results");
      await sleep(500);
      if (dev) await dev.screenshot({ path: join(SHOTS, "dev-results.png"), fullPage: true });
      const results = await phones[0].$(".scores").innerText().catch(() => "");
      log(`results:\n${results}`);
      await phones[0].$("button:has-text(\"Back to lobby\")").click();
      const lobby = await until("phone 1 back in the lobby", async () => {
        const s = await snapshot(phones[0]);
        return s.screen === "lobby" ? s : null;
      }, 10_000);
      await phones[0].shot("back-in-lobby.png");
      if (GAME === "redline" && !phasesSeen.has("chequered")) fail("never saw the Chequered view on phone 1");
      if (GAME === "redline") {
        // The podium: the top three players (every phone, with three or fewer) shook their bottles and popped them.
        const podium = Math.min(3, PHONES);
        if (PHONES <= 3) for (const phase of ["anthem", "champagne"]) if (!phasesSeen.has(phase)) fail(`phone 1 never showed the ${phase} bottle`);
        if (pops.size < podium) fail(`only ${pops.size} of ${podium} podium phones popped a cork`);
        log(`champagne: ${[...pops].map(([slot, p]) => `P${slot} ${p.count} pop(s), best ${p.best.toFixed(2)}`).join(", ") || "no pops"}`);
      }
      if (GAME === "redline") log(`race over after round ${lastRound}`);
      log(`back in the lobby; ${lobby.picked ? `${lobby.picked} still picked` : "no game picked"}`);
    }
  } finally {
    done = true;
    await Promise.all([...bots, shooter, watchdog, rejoin]);
  }
  if (GAME === "redline") {
    for (const phase of ["driver-select", "quali-reading", "quali-question", "quali-result", "throttle", "reading", "question", "reveal"]) {
      if (!phasesSeen.has(phase)) fail(`phone 1 never showed ${phase}`);
    }
    // For tools/tv-e2e.sh: the bots' grid order qualifying should give, fastest first (compared with the TV's `REDLINE grid`).
    const order = expectedQualiOrder();
    log(`qualifying plan: ${order.map((b) => `P${b.slot} ${b.code} ${b.correct}/${QUALI_LAPS} laps, ${b.total.toFixed(2)} s`).join("; ")}`);
    log(`QUALI order ${order.map((b) => b.code).join(" ")}`);
  }
  log(`phases seen on P1: ${[...phasesSeen].join(", ")}`);
  return 0;
}

/** Polls `test` until it returns something truthy; throws with `what` after `ms`. */
async function until(what, test, ms) {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    const v = await test();
    if (v) return v;
    await sleep(250);
  }
  throw new Error(`timed out waiting for ${what}`);
}

function clampInt(raw, fallback, min, max) {
  const n = parseInt(raw ?? "", 10);
  return Number.isNaN(n) ? fallback : Math.min(max, Math.max(min, n));
}

/** `import "playwright"` from the project, else the global install (NODE_PATH or `npm root -g`). */
async function loadPlaywright() {
  try {
    return await import("playwright");
  } catch {
    const require = createRequire(import.meta.url);
    try {
      return require("playwright"); // honours NODE_PATH
    } catch {
      const { execSync } = await import("node:child_process");
      const globalRoot = execSync("npm root -g", { encoding: "utf8" }).trim();
      return createRequire(join(globalRoot, "noop.js"))("playwright");
    }
  }
}
