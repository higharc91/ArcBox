# REDLINE Audio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Question narration in a Google Chirp 3: HD voice (with a built-in fallback), a Reading phase that holds the answer clock, a race commentator using player names, TV sound effects and music, and phone haptics and sounds.

**Architecture:** Game logic gains a deterministic Reading phase and stays audio-free. A pure-C# `Commentary` turns host-view transitions into lines, and a pure-C# synth makes the sound effects. The server owns the Google key behind a host-authenticated, cached `/api/tts` endpoint and prewarms each race's questions. The TV plays everything through a Narrator and an AudioDirector on ducked buses. Phones add WebAudio and vibration.

**Tech Stack:** .NET 10 / ASP.NET Core, Godot 4.7.2 .NET (`AudioStreamMP3`, `AudioStreamWav`, `AudioStreamGenerator`, `DisplayServer.TtsSpeak`), Google Cloud Text-to-Speech REST v1, Preact + WebAudio + Vibration API.

**Spec:** `docs/superpowers/specs/2026-10-26-redline-audio-design.md` (read it first). Base: `docs/superpowers/specs/2026-10-25-redline-design.md`.

## Global Constraints

- Voice default `en-US-Chirp3-HD-Charon`, language `en-US`, encoding `MP3`. Endpoint `POST https://texttospeech.googleapis.com/v1/text:synthesize?key=<ApiKey>`; response field `audioContent` (base64).
- The API key is read only from configuration (`Speech:Google:ApiKey`; env `Speech__Google__ApiKey`; user-secrets in Development). Never log it, never send it to a client, never commit it.
- `/api/tts` limits: text 1–300 chars after trimming; 60 requests/min/room; auth by room code + host token (constant time).
- `ReadingTime(text) = clamp(0.6 s + words × 0.36 s, 2.5 s, 10 s)`; `ReadQuestions` default `true`.
- Reveal commentary: at most 2 lines; players first.
- Ducking: Music −12 dB, Sfx −6 dB while Voice plays; 200 ms attack, 600 ms release.
- Narrator fallback to `DisplayServer.TtsSpeak` when `/api/tts` fails or takes > 2.5 s; 60 s cool-down before retrying Google.
- Synth output peak ≤ −1 dBFS; 44.1 kHz mono 16-bit.
- Phone haptic patterns (ms): tap 15; correct [30,40,30]; wrong 120; spin [60,30,60,30,120]. Mute key `arcbox:mute`.
- `--audio-log` prints `AUDIO <bus> <name>` per play.
- .NET tests: `dotnet test --solution ArcBox.slnx` (Microsoft.Testing.Platform: `--project`, `--filter-class`; no positional paths). Web: `cd web/play && npm test && npm run build`.
- Every commit message ends with the two attribution lines given in the task brief.

## Review Focus

1. **Google unreachable mid-race:** expected: the TV switches to the built-in voice within 2.5 s and the race never waits on audio. Test in Task 6 (`Narrator falls back on timeout and cools down`) and Task 3 (`Tts_returns_503_when_google_fails`).
2. **Same text requested twice at once (question prewarm racing the TV's request):** expected: one Google call. Test in Task 3 (`Concurrent_requests_share_one_synthesis`).
3. **`ReadQuestions = false`:** expected: Throttle goes straight to Question as before; the full existing suite still passes. Test in Task 1.
4. **A disconnected or slow player during Reading:** expected: Reading ends on its timer only (no early advance); inputs during Reading ignored. Test in Task 1.
5. **Phone audio before any user gesture (iOS):** expected: no exceptions; sounds start working after the Join tap. Test in Task 5.

---

### Task 1: Reading phase

**Files:**
- Modify: `games/Redline/Redline.Logic/{RedlineRules.cs, RedlineGame.cs, Views/HostView.cs, Views/PlayerViews.cs}`, tests in `games/Redline/Redline.Logic.Tests/`
- Modify: `games/Redline/Redline.Presentation/CameraDirector.cs` (+ tests), `games/Redline/tools/Redline.Sim/` (bots wait through Reading)
- Modify: `web/play/src/dev/{redlineView.ts, RedlineBoard.tsx}`, `web/play/src/skins/redline.ts`, `web/play/test/*`
- Modify: `docs/superpowers/specs/2026-10-25-redline-design.md` §3.1 table (add the Reading row, pointing at the audio spec)

**Interfaces:**
- Produces: `RacePhase.Reading` (after `Throttle`); `RedlineRules.ReadQuestions` (bool, default true); `TimeSpan RedlineRules.ReadingTime(string text)`; `PlayerViews.Listening(string text, string header, DateTimeOffset? deadline) → WaitView` with `Message = text`.
- HostView in Reading: `question` carries `text` and `answers` (as in Question); `deadlineUtc` is the Reading deadline; `correctIndex` null; throttles visible (as in Question).

- [ ] **Step 1:** Tests: `Reading_follows_throttle_and_shows_the_question_on_phone`; `Reading_lasts_ReadingTime_then_opens_question`; `Inputs_during_reading_are_ignored`; `Reading_does_not_end_early_when_everyone_is_ready`; `ReadQuestions_false_skips_reading`; `ReadingTime_is_clamped` (1 word → 2.5 s; 12 words → 4.92 s; 60 words → 10 s); `HostView_in_reading_shows_text_and_answers_but_no_correct_index`; CameraDirector `Reading_uses_the_question_shot`.
- [ ] **Step 2–4:** fail → implement → green. Update the simulator so bots act only in Throttle/Question and report the new race duration; update the sim report's time table in one sentence.
- [ ] **Step 5:** Web: add `"Reading"` to the TS phase union; RedlineBoard shows the question and answers with "LISTEN" in place of the clock; the REDLINE skin styles a Reading `wait` view (header with throttle) as the question with "Answers open in a moment…". vitest for both.
- [ ] **Step 6:** Commit `feat(redline): Reading phase holds the answer clock while the question is read`.

### Task 2: Commentary (pure C#)

**Files:**
- Create: `games/Redline/Redline.Presentation/Audio/Commentary.cs`, test `games/Redline/Redline.Presentation.Tests/Audio/CommentaryTests.cs`

**Interfaces:**
- Produces: `enum CommentaryKind { Intro, LightsOut, RoundIntro, Answer, Event, Finish }`; `record CommentaryLine(string Text, CommentaryKind Kind, int Priority)`; `static IReadOnlyList<CommentaryLine> Commentary.Lines(RedlineHostView view, RedlineHostView? previous, Grid grid)`. Lines fire only on phase **entry** (`previous?.Phase != view.Phase` or round changed). Copy exactly as spec §3.2. Driver surname = last word of `GridDriver.Name`; team = `GridTeam.Name`.
- Spec §3.2 line for Reading `RacePhase.Reading` is none (Task 1 adds the enum value; if Task 1 hasn't merged yet, code against the enum by adding the value locally is NOT allowed — instead treat "not Throttle/Reveal/Grid/Chequered" as no lines, so it compiles either way).

- [ ] **Step 1:** Tests: `Grid_intro_counts_players`; `First_throttle_says_lights_out_then_round_intro`; `Later_throttles_only_round_intro`; `Reveal_says_answer_then_one_player_event`; `Player_spin_beats_lead_change_beats_gain_beats_near_flag`; `Reveal_without_player_event_uses_ai_lead_change_or_nothing`; `Never_more_than_two_reveal_lines`; `Chequered_player_winner` / `Chequered_ai_winner_names_best_player`; `No_lines_without_phase_change`.
- [ ] **Step 2–4:** fail → implement → green.
- [ ] **Step 5:** Commit `feat(redline): race commentary lines from host-view transitions`.

### Task 3: Server speech service and `/api/tts`

**Files:**
- Create: `src/ArcBox.Server/Speech/{SpeechOptions.cs, ISpeechSynthesizer.cs, GoogleTtsClient.cs, SpeechCache.cs, SpeechEndpoints.cs, NarratedQuestionSource.cs, SpeechServiceCollectionExtensions.cs}`
- Modify: `src/ArcBox.Server/Rooms/Room.cs` (`public bool IsHostToken(string token)`), `Program.cs` (`AddSpeech(configuration)`, `MapSpeech()`), `RedlineServiceCollectionExtensions.cs` (decorate `IQuestionSource` with `NarratedQuestionSource` when speech is enabled), `appsettings.json` (`Speech` section without key), `.gitignore` (`.tts-cache/`)
- Test: `src/ArcBox.Server.Tests/Speech/*`
- Create: `docs/speech-setup.md` (Google Cloud project, enable Text-to-Speech API, billing account, create a restricted API key limited to the Text-to-Speech API, `dotnet user-secrets set Speech:Google:ApiKey …` or env var, voice list link, free-tier note, the character counter log line)

**Interfaces:** per spec §3.1. `ISpeechSynthesizer.SynthesizeAsync(string text, CancellationToken) → Task<byte[]>`; `SpeechCache.GetAsync(string text, CancellationToken) → Task<byte[]>`; `SpeechCache.CharactersSynthesized` (long).

- [ ] **Step 1:** Tests: `Google_request_shape` (URL with key, JSON body voice/encoding, no key in logs); `Decodes_audioContent`; `Cache_hit_skips_google` (incl. across instances via disk); `Concurrent_requests_share_one_synthesis` (Review Focus 2); `Failures_are_not_cached`; endpoint: `Tts_requires_valid_host_token` (401), `Tts_rejects_empty_and_long_text` (400), `Tts_rate_limited_per_room` (429), `Tts_returns_503_when_disabled_or_unconfigured`, `Tts_returns_503_when_google_fails` (Review Focus 1), `Tts_returns_mp3`; `Take_prewarms_question_texts` and `Prewarm_errors_do_not_affect_take`; `Character_counter_counts_only_synthesised_text`.
- [ ] **Step 2–4:** fail → implement → `dotnet test --solution ArcBox.slnx` green. Tests use a fake `HttpMessageHandler`; no real Google calls.
- [ ] **Step 5:** Commit `feat(server): Google Chirp 3 HD speech with cache, prewarm and host-only /api/tts`.

### Task 4: Procedural sound effects (pure C#)

**Files:**
- Create: `games/Redline/Redline.Presentation/Audio/Synth/{Pcm.cs, Sfx.cs, EngineVoice.cs}`, test `games/Redline/Redline.Presentation.Tests/Audio/SynthTests.cs`

**Interfaces:**
- `static class Sfx` returning `Pcm` (`record Pcm(short[] Samples, int SampleRate)`): `StartBeep()`, `LightsOut()`, `Tick()`, `Whoosh()`, `CorrectSting()`, `WrongBuzz()`, `TyreScreech()`, `Crunch()`, `CrowdCheer()`, `CrowdBed(seconds)` (loopable), `Fanfare()`, `LockClick()`, `CountdownTick()`.
- `sealed class EngineVoice(int sampleRate)` — `void Fill(Span<float> buffer, double rpm, double load)`: harmonic sawtooth stack + filtered noise, continuous phase across calls (for `AudioStreamGenerator`).
- [ ] **Step 1:** Tests: every Sfx non-empty, expected duration ±10 %, peak ≤ −1 dBFS, no NaN; `CrowdBed` loops seamlessly (first/last 10 ms RMS within 20 %); `EngineVoice` continuous across calls (no step > 0.2 between consecutive samples at the boundary) and pitch rises with rpm (zero-crossing rate).
- [ ] **Step 2–4:** fail → implement → green. Also write a tiny throwaway exporter to WAV in the scratchpad and listen-check by spectrogram image (no sound card here); describe the sounds in the report.
- [ ] **Step 5:** Commit `feat(redline): procedural sound effects and engine voice`.

### Task 5: Phone haptics and sounds

**Files:**
- Create: `web/play/src/feedback/{haptics.ts, sounds.ts, feedback.ts, MuteToggle.tsx}`, `web/play/test/feedback.test.ts`
- Modify: `web/play/src/session/session.ts` or `App.tsx` (hook sends), `widgets/ControllerScreen.tsx` (status bar toggle, countdown ticks), `screens/Join.tsx` (unlock on Join tap), `skins/redline.ts` (`classifyResult(view) → "correct" | "wrong" | "spin" | null`)

**Interfaces:** `feedback.onSend()`, `feedback.onView(view, game)`, `feedback.onCountdown(secondsLeft)`, `feedback.unlock()`, `feedback.setMuted(bool)`, `feedback.muted`.
- [ ] **Step 1:** Tests (fake `navigator.vibrate` and fake `AudioContext`): `send vibrates tap and clicks`; `redline result views map to patterns`; `muted suppresses both and persists`; `no AudioContext before unlock and no throw` (Review Focus 5); `countdown ticks only for last 3 s of input views`; `vibrate missing is a no-op`.
- [ ] **Step 2–4:** fail → implement → `npm test && npm run build` green.
- [ ] **Step 5:** Commit `feat(web): phone haptics and sounds with mute toggle`.

### Task 6: TV audio — buses, Narrator, AudioDirector, Reading HUD

**Files:**
- Create: `src/ArcBox.Host/Audio/{AudioBuses.cs, Narrator.cs, TtsFetcher.cs, AudioLog.cs, Ducking.cs}`, `src/ArcBox.Host/games/redline/Audio/{AudioDirector.cs, EnginePlayer.cs, SfxBank.cs, Music.cs}`
- Modify: `src/ArcBox.Host/Main.cs` (create the Narrator with the session's server/code/token; `M` mute), `Shell/HostSettings` (volumes), `Shell/AppArgs.cs` (`--audio-log`), `games/redline/RedlineScene.cs` (wire AudioDirector + Commentary → Narrator; narrate the question on Reading entry, or on Question entry when Reading is off), `games/redline/Hud/*` (Reading: question + answers + "LISTEN"), F3 overlay narrator state
- Pure logic that can live in `ArcBox.Host.Core` (with tests): `NarrationQueue` (priority, preemption, staleness drop 4 s), `NarratorFallbackPolicy` (2.5 s timeout, 60 s cool-down), `TtsRequest` builder.

**Interfaces:** `Narrator.Say(string text, NarrationPriority priority)`; `Narrator.State` (`Google | Fallback | Off`); `AudioDirector.OnView(RedlineHostView view, RedlineHostView? previous)`, `AudioDirector.OnSpin(string code)`, `AudioDirector.OnLight(int index)`.
- [ ] **Step 1:** Core tests: `Question_preempts_commentary`; `Stale_commentary_dropped`; `Narrator falls back on timeout and cools down` (Review Focus 1); `Fallback_retries_google_after_cooldown`.
- [ ] **Step 2–4:** fail → implement → green; `dotnet build src/ArcBox.Host` clean.
- [ ] **Step 5:** Run `--redline-demo --audio-log` under Xvfb and assert the log contains, in order: start beeps ×5, lights-out, round intro narration, question narration (fallback provider here, since no key and no sound card), Reveal sting, commentary event, fanfare. Look at the Reading-phase screenshots.
- [ ] **Step 6:** Commit `feat(redline): TV narration, commentary, sound effects and music`.

### Task 7: End-to-end and docs

**Files:** `tools/tv-e2e.sh` (pass `--audio-log`, assert the AUDIO lines above), `docs/redline-tv.md` (audio section, volumes, mute, fallback), `docs/playtest-checklist.md` (listen-checks: voice clarity, ducking, commentary timing, phone haptics on Android, iOS silent-switch behaviour), `docs/speech-setup.md` cross-links.
- [ ] **Step 1:** Run `tools/tv-e2e.sh` to the first Reveal with `/api/tts` unconfigured (503 → fallback path) and confirm the audio assertions pass and the Reading phase appears on TV and phones.
- [ ] **Step 2:** Commit `test: audio assertions in the TV end-to-end run`.

## Done criteria
- All tests green (.NET and web).
- e2e shows the Reading phase on TV and phones, and the audio log proves narration, commentary and SFX fire in order.
- With a real key, the user hears Chirp 3: HD (cannot be verified in the sandbox: Google is not called without a key, and there is no sound card).
