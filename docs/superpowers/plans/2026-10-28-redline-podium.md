# REDLINE Podium Celebration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An F1-style podium after the chequered flag: the winning car's public-domain anthem, then a REDLINE victory theme while the top three players shake their phones and pop champagne that sprays on the TV.

**Architecture:** Two deterministic phases (Anthem, Champagne) in Redline.Logic with a `Podium` record on the host view; a generic `ShakeView` controller widget in the SDK; anthem melodies and the victory theme synthesised in pure C#; the TV builds a podium set and turns spray events into particles.

**Tech Stack:** .NET 10, Godot 4.7.2 .NET (GPUParticles3D), Preact + DeviceMotion + WebAudio.

**Spec:** `docs/superpowers/specs/2026-10-28-redline-podium-design.md` (read it first).

## Global Constraints
- Anthem 16 s, Champagne 16 s, `Celebrate` default true; Chequered unchanged (10 s); `EndGame` after Champagne.
- Podium = top 3 human players by race position; AI-only rooms skip the celebration.
- `ShakeView(Prompt, Color?, CanFire, InitialCharge)` kind `"shake"`; `ShakeInput(Charge, Fire)`. Charge clamped 0..1, quantised to 0.05 in the host view. Spray strength = max(charge, 0.15). ≥ 400 ms between sprays per player.
- Phones send charge ≤ 4×/s and only on change ≥ 0.05.
- Anthems: only the public-domain entries in the spec table have melodies; TH and MX fall back to the theme. Each entry records composer, year and status.
- Synth output: 44.1 kHz mono 16-bit, peak ≤ −1 dBFS.
- .NET tests: `dotnet test --solution ArcBox.slnx` (Microsoft.Testing.Platform: `--project`, `--filter-class`). Web: `cd web/play && npm test && npm run build`.

## Review Focus
1. **A podium player disconnects mid-celebration:** keeps the step, no crash, phases run on timers. Test in Task 1.
2. **Shake inputs flood (40/s from a buggy phone):** host view updates bounded by quantisation; hub rate limit drops excess; no exception. Test in Task 1 (`Charge_updates_are_quantised`).
3. **iOS denies motion permission / desktop browser:** tap fallback charges the bottle. Test in Task 3.
4. **Two players with the same distance at the finish:** podium order follows classification order deterministically. Test in Task 1.
5. **Winner's nationality is a fallback country (PER/ALB):** theme-only, caption still names the country. Test in Task 2 and Task 1.

---

### Task 1: Logic — Anthem and Champagne phases, ShakeView SDK widget, nationality data
**Files:** `src/ArcBox.Sdk/ControllerView.cs`, `src/ArcBox.Sdk/PlayerInput.cs`, `src/ArcBox.Sdk.Tests/*` (incl. `ContractFixtureTests` + new `web/play/test/fixtures/shake.json`), `games/Redline/Redline.Logic/{RedlineRules.cs, RedlineGame.cs, Views/HostView.cs, Views/PlayerViews.cs, Grid/*, Data/grid2026.json}`, `games/Redline/tools/refresh_grid.py`, tests in `games/Redline/Redline.Logic.Tests/` (new `PodiumTests.cs`), `games/Redline/tools/Redline.Sim` (skip or pass through the new phases), `games/Redline/Redline.Presentation/{CameraDirector.cs, Hud/*, Audio/Commentary.cs}` (compile + sensible defaults only: Anthem/Champagne → a new `PodiumShot`; commentary lines per spec §5), web TS mirror `web/play/src/dev/redlineView.ts` (+ `Podium` types; RedlineBoard shows the podium steps, charges and spray counts).
**Interfaces:** per spec §2–3. `Podium.AnthemId` is the anthem key (`"gb"`, `"fr"`, …) or null for fallback; `AnthemCountry` is the display name ("Great Britain", "France", …) from a `Countries` table in Redline.Logic.
- [ ] Tests: `Chequered_is_followed_by_anthem_then_champagne_then_end`; `Celebrate_false_ends_after_chequered`; `Ai_only_room_skips_celebration`; `Podium_is_top_three_players_even_behind_ai`; `Fewer_than_three_players_all_get_steps`; `Tied_players_follow_classification_order`; `Podium_phones_get_shake_others_wait`; `Shake_view_stable_while_charging` (same ViewId after charge inputs); `Champagne_view_carries_initial_charge_and_can_fire`; `Fire_in_anthem_is_ignored`; `Fire_records_spray_and_resets_charge`; `Spray_throttled_to_400ms`; `Charge_updates_are_quantised`; `Malformed_shake_input_ignored`; `Disconnected_podium_player_keeps_step`; `Anthem_follows_best_player_driver_nationality`; `Fallback_nationality_has_null_anthem_id`; nationality present for all 22 drivers; SDK serialisation of `shake`.
- [ ] Commit `feat(redline): podium phases with anthem and champagne, generic shake widget`.

### Task 2: Anthems and the REDLINE victory theme (pure C# synth)
**Files:** `games/Redline/Redline.Presentation/Audio/Synth/{Anthems.cs, AnthemSynth.cs, VictoryTheme.cs}`, tests `Redline.Presentation.Tests/Audio/AnthemTests.cs`.
**Interfaces:** `record AnthemInfo(string Id, string Country, string Title, string Composer, int Year, string Status, IReadOnlyList<Note>? Melody, int Bpm, string Meter)`; `record Note(int Midi, double Beats)` (Midi 0 = rest); `Anthems.Find(string id)`; `AnthemSynth.Render(AnthemInfo, double maxSeconds = 16) → Pcm` (brass-band: melody + simple I–IV–V harmonisation from a per-anthem chord list, timpani roll lead-in, fade-out at the phrase end); `VictoryTheme.Render() → Pcm` (≈ 16 s, anthemic, loop-safe tail) and `VictoryTheme.Sting() → Pcm`.
- Melodies: transcribe the opening phrases from public-domain scores. Write each in the key used by common public-domain editions; add a `// source:` comment (score edition / IMSLP page title). Correctness matters — these are national anthems people know; double-check intervals and rhythm. TH and MX have `Melody = null` with the reason in `Status`.
- [ ] Tests: every PD anthem renders 10–16 s, peak ≤ −1 dBFS, no NaN, starts/ends near silence; fallback entries have null melody; all 15 grid nationalities resolve; `VictoryTheme` length and loop seam; deterministic output.
- [ ] Export WAVs + spectrograms to the scratchpad for listening (not committed).
- [ ] Commit `feat(redline): public-domain anthem synth and REDLINE victory theme`.

### Task 3: Phone — Shake widget
**Files:** `web/play/src/widgets/Shake.tsx`, `web/play/src/feedback/*` (fizz, pop sounds; haptic patterns), `web/play/src/protocol.ts` (`ShakeView`), `ControllerScreen.tsx` (dispatch), `skins/redline.ts` (REDLINE look), tests `web/play/test/shake.test.tsx`.
**Interfaces:** `Shake({view, send})`; `useMotionCharge()` hook with injectable `DeviceMotionEvent` source; `ChargeModel` (pure: `addMotion(mag, dt)`, `addTap()`, `decay(dt)`, `value`).
- [ ] Tests: `motion above gravity charges and stillness decays`; `tap fallback charges` (Review Focus 3); `permission button shown on iOS-style API and requests inside the gesture`; `sends charge at most 4 per second and only on 0.05 change`; `fire sends Fire with current charge and resets`; `cannot fire when CanFire is false`; `initial charge seeds the model`; `muted suppresses fizz/pop/haptics`.
- [ ] Visual check with Playwright at 390×844: anthem state (charging), champagne state (POP!), spraying frame. Commit `feat(web): shake-to-charge champagne widget`.

### Task 4: TV — podium set, champagne particles, HUD, audio, e2e
**Files:** `src/ArcBox.Host/games/redline/{Podium/*, Hud/PodiumHud.cs, Audio/*}`, `RedlineScene.cs`, `Camera/CameraRig.cs`, demo (`DemoRace`) gains Anthem + Champagne steps with scripted sprays, `tools/tv-e2e.sh` + `web/play/e2e/redline.e2e.mjs` (podium bots charge and fire), docs (`docs/redline-tv.md`, `docs/playtest-checklist.md`: listen to the anthems, iOS motion permission over HTTPS).
- [ ] Podium set and bottles built in code; spray particles per new `Seq`; confetti; camera; HUD; anthem → theme playback; cork pops; commentary lines.
- [ ] Demo screenshots at 1920×1080 (anthem, first spray, triple spray, confetti) — review against broadcast quality; e2e to the end of Champagne with AUDIO assertions.
- [ ] Commit `feat(redline): podium ceremony with anthem, champagne spray and confetti`.

## Done criteria
All tests green; demo and e2e screenshots show the podium and sprays; audio log shows anthem → theme → pops; phones charge by motion or taps and fire.
