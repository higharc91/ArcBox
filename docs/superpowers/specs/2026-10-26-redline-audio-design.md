# REDLINE Audio — Design Spec

- **Date:** 2026-10-26
- **Status:** Approved in conversation (decisions below were made by the user)
- **Builds on:** [REDLINE design](2026-10-25-redline-design.md), [platform design](2026-10-18-arcbox-platform-design.md)

## 1. Purpose

REDLINE is currently silent. This adds:
1. **Question narration** on the TV in a neural voice.
2. **A race commentator** that calls the race, using players' names.
3. **Race sound effects and music** on the TV.
4. **Phone haptics and sounds.**

### Decisions made by the user
| Topic | Decision |
|---|---|
| Voice provider | **Google Cloud Text-to-Speech, Chirp 3: HD** voices. Godot's built-in TTS is the offline fallback. |
| Reading vs the clock | **The question is read before the answer clock starts**, configurable, default on. Phones show the text, but answering opens only when the clock starts. |
| Commentator | **Yes, with player names** ("Sam takes the lead in the Red Bull!"). |
| Extras | Race SFX and music; phone haptics and sounds. |
| Reuse (added 2026-10-26) | **Text-to-speech is a platform service on the server, usable by every game, with named voice themes.** Each theme is a voice persona (e.g. REDLINE's energetic announcer vs its clear question reader); games declare their own themes. |

### Success criteria
1. With a Google API key configured, every question is read aloud on the TV in a Chirp 3: HD voice, and answering opens when the reading ends.
2. With no key, no internet or a Google error, the game still reads questions with the TV's built-in voice and never stalls or goes silent mid-race.
3. The commentator speaks at most two lines per Reveal, players first, and never talks over the question.
4. A typical game night (10 races) stays inside Google's free tier (1M characters/month), with repeated text never synthesised twice.
5. The Google API key never leaves the server; the TTS endpoint can only be used by a room's host.
6. Phones vibrate (where supported) and play short sounds on lock-in and on the round result, with a mute toggle.

## 2. Rules change: the Reading phase

A new phase between **Throttle** and **Question**:

| Phase | Duration | TV | Phone |
|---|---|---|---|
| **Reading** (new, per round) | `RedlineRules.ReadingTime(question)` when `ReadQuestions` is on; skipped when off | Question text, the four answers, "LISTEN" instead of the clock; the narrator reads the question | `WaitView(Message: question text)` with `Header = "{THROTTLE} · P{p} · ROUND {r}/{max}"`; the REDLINE skin styles it as the question with "Answers open in a moment…" |

- `RedlineRules.ReadQuestions` (bool, default `true`).
- `RedlineRules.ReadingTime(string text)` is deterministic: `clamp(0.6 s + words × 0.36 s, 2.5 s, 10 s)` (Chirp 3: HD reads about 2.7 words/s). The game must not depend on audio, so the estimate, not the real clip length, drives the phase. A clip that runs a little long finishes over the open answers; that is acceptable.
- Only the question text is read, not the answers (they are on screen and on the phones).
- Input during Reading is ignored like any other wrong-phase input.
- `RacePhase` gains `Reading` (inserted after `Throttle`). The phone app's TS mirror, the dev board, the TV HUD, the camera director (Reading uses the Question shot) and the simulator are updated.

## 3. Architecture

```
 Game (Redline.Logic)          Server (ArcBox.Server)                    TV (ArcBox.Host)
 Reading phase, pure  ──view──► Hub ─────────────────────────────────────► AudioDirector (SFX/music from views)
                                POST /api/tts {code, hostToken, text} ◄── Narrator (queue, ducking, fallback)
                                 ├─ SpeechCache (disk, SHA-256 of voice+text)      ▲
                                 └─ GoogleTtsClient (Chirp 3: HD, MP3)             │ lines
                                NarratedQuestionSource (prewarms race questions)  Commentary (pure, Presentation)
```

### 3.1 Platform speech service with voice themes (`src/ArcBox.Server/Speech/`, `src/ArcBox.Sdk/Speech/`)
Speech belongs to the platform, not to REDLINE, so any game can narrate.

- **Voice themes** (SDK, shared by server, games and TV):
  `VoiceTheme(Id, Description, Voice, LanguageCode = "en-US", SpeakingRate?, Pitch?, VolumeGainDb = 0, Fallback?)` and `FallbackVoice(Rate, Pitch)` (hints for the TV's built-in TTS). Ids are namespaced: `arcbox.*` for the platform (`arcbox.host` is the default), `<gameId>.*` for a game.
- **Registry:** games declare their themes in their Logic project (REDLINE: `RedlineVoices` with `redline.announcer` and `redline.questions`) and register them with `services.AddVoiceThemes(...)`. Configuration can override any field or add themes under `Speech:Voices:<id>`. Duplicate ids fail at startup.
- `SpeechOptions` (`Speech` section): `Enabled` (default true), `Provider` (`"google"`), `Google:ApiKey` (secret; env `Speech__Google__ApiKey` or user-secrets), `CacheDir` (default `<content root>/.tts-cache`), `MaxChars` (300), `RequestsPerMinutePerRoom` (60), `Voices` (overrides).
- `ISpeechSynthesizer` → `GoogleTtsClient`: `POST https://texttospeech.googleapis.com/v1/text:synthesize?key=…` with the theme's resolved voice and only the `audioConfig` fields its voice family supports; MP3 out. Typed `HttpClient`, 10 s timeout, no retries inside a request (the TV falls back instead).
- `SpeechCache`: key = SHA-256 of the **resolved theme** (voice, language, rate, pitch, gain) + text, so changing a theme's voice never serves stale audio; single-flight per key; failures not cached.
- `ISpeechService.Prewarm(themeId, texts)`: generic, fire-and-forget, bounded concurrency. REDLINE's `NarratedQuestionSource` uses it with `redline.questions` for each race's questions.
- **Endpoints** (host-authenticated with room code + host token, constant-time via `Room.IsHostToken`):
  - `POST /api/tts` `{code, hostToken, text, voice}` → `audio/mpeg`. `voice` is a theme id (default `arcbox.host`). 400 unknown theme or bad text, 401 bad auth, 429 over the per-room rate, 503 disabled/unconfigured/Google failure.
  - `GET /api/tts/voices` → `[{id, description, fallback}]`; never exposes provider voice names or keys.
- **Cost guard:** characters synthesised, per theme and in total, logged hourly and at shutdown.

### 3.2 Commentary (pure C#, `games/Redline/Redline.Presentation/Audio/Commentary.cs`)
`Commentary.Lines(RedlineHostView view, RedlineHostView? previous) → IReadOnlyList<CommentaryLine>`, where `CommentaryLine(string Text, CommentaryKind Kind, int Priority)`. Triggered on phase entry only:
- **Grid:** "Welcome to the Circuit of the Americas. Twenty-two cars, {n} players. Lights out in a moment."
- **Throttle round 1:** "And it's lights out!" then **every Throttle:** "Round {r}. {Category}, {difficulty}."
- **Reading / Question:** none (the question itself is narrated by the TV).
- **Reveal**, at most two lines, players first:
  1. "The answer is {answer}."
  2. The single most dramatic player event: a player spin-out ("{Name} spins the {Team}!"), a player taking the lead ("{Name} takes the lead!"), a player gaining ≥ 3 places ("{Name} storms up {n} places!"), or a player within 4 of the finish line ("{Name} can see the flag!"). If no player event, an AI lead change ("{Driver} leads the race.") or nothing.
- **Chequered:** "Chequered flag! {Name} wins at the Circuit of the Americas!" (player winner) or "{Driver} takes the win. Best of the players, {Name} in P{p}." (AI winner).
- Names come from the view (`playerName`, driver surname from the grid, team name). Numbers are spelled as digits; Google reads them naturally.

### 3.3 TV audio (`src/ArcBox.Host/Audio/` shared, `games/redline/Audio/` game-specific)
- **Buses:** `Master → Music, Sfx, Voice`. When the Voice bus is active, Music ducks −12 dB and Sfx −6 dB (200 ms attack, 600 ms release, tweened volumes).
- **Narrator** (shared, in the shell, usable by every game's scene): a queue of `Say(text, voiceTheme, priority)`; REDLINE uses `redline.questions` for questions and `redline.announcer` for commentary; plays one clip at a time on the Voice bus; fetches `/api/tts` with the session's code and host token over `HttpClient`, `AudioStreamMP3`. Question narration preempts commentary; commentary older than 4 s is dropped. Fallback when the request fails or takes > 2.5 s: `DisplayServer.TtsSpeak` with the first available English voice and the theme's `FallbackVoice` rate and pitch (from `GET /api/tts/voices`), and a 60 s cool-down before trying Google again.
- **AudioDirector** (REDLINE): maps view transitions and scene events to sounds:
  - start-light beeps (one per light) and the lights-out roar;
  - a tick when a player locks a throttle or answers; countdown ticks for the last 5 s of Throttle and Question;
  - a whoosh when the question appears;
  - Reveal: correct sting or wrong buzz (global), engine roar scaled by the biggest gain, tyre screech and crunch for each spin-out on camera, crowd cheer on a lead change;
  - Chequered: fanfare and crowd;
  - an engine bed: a procedural engine voice on the camera subjects, pitch from `CarNode` speed;
  - music: a lobby loop and a low race bed, ducked by the voice.
- **SFX source:** synthesised in pure C# (`games/Redline/Redline.Presentation/Audio/Synth/`) into PCM buffers that the TV wraps as `AudioStreamWav` at load, so like the cars and the circuit they are made from code with no licensing. **Music** may use CC0 tracks (Kenney or OpenGameArt, recorded in `THIRD_PARTY.md`) if reachable; otherwise a procedural loop.
- **Settings:** volumes (master, music, sfx, voice) and `NarrationVoice` in `user://settings.json`; `M` toggles mute; F3 overlay shows the narrator's state (google / fallback / off).
- **Audio log:** with `--audio-log`, every play prints `AUDIO <bus> <name>`, so the sandbox (no sound card) and the e2e script can assert that sounds fire.

### 3.4 Phones (`web/play/src/feedback/`)
- `haptics.ts`: `navigator.vibrate` patterns (tap 15 ms; correct [30, 40, 30]; wrong 120; spin [60, 30, 60, 30, 120]); no-op when unsupported (iOS).
- `sounds.ts`: tiny WebAudio-synthesised sounds (tap click, correct chime, wrong buzz, countdown tick); the `AudioContext` is created and resumed on the first user gesture (the Join button), as iOS requires.
- Triggers: on any input sent; on a `wait` view whose skin classifies it as a result (REDLINE: "Correct!" / "Wrong answer" / "No answer" / "Spun out!"); last 3 s countdown ticks while an input widget is shown.
- A speaker toggle in the status bar, persisted in `localStorage` (`arcbox:mute`), mutes sounds and haptics.

## 4. Failure handling
| Failure | Behaviour |
|---|---|
| No API key / speech disabled | `/api/tts` 503; TV uses the built-in voice. |
| Google error, quota or timeout | Same; failure not cached; logged once per minute. |
| TV built-in TTS unavailable (no voices) | Narration silently skipped; the Reading phase still runs on its timer. |
| Player name is rude or unpronounceable | Read as typed. Names are already limited to 12 characters. Accept for a private game. |
| Many requests (abuse of a public URL) | Host-token auth, 300-char cap, per-room rate limit, cache. |

## 5. Testing
- **Logic:** Reading phase flow, timer, early inputs ignored, `ReadQuestions=false` skips the phase, `ReadingTime` bounds; host view and phone views in Reading.
- **Commentary:** each line type, players-first priority, ≤ 2 lines in Reveal, AI-only fallbacks, Chequered wording for player and AI winners.
- **Server:** Google request shape and response decoding (fake handler), cache hit/miss and single-flight, endpoint auth/limits/status codes, prewarm on `Take`, character counter.
- **Synth:** buffers non-empty, expected length, peak ≤ −1 dBFS, no NaN.
- **Phones:** vitest for haptic/sound triggers, mute persistence, gesture unlock.
- **TV:** `--audio-log` lines asserted by `tools/tv-e2e.sh` for start lights, question narration (fallback here), Reveal stings and the chequered fanfare. The real mix is judged by the user on the TV PC.

## 6. Out of scope
- Voice selection UI beyond a settings value; multiple languages; SSML styling.
- Lip-sync or subtitles of commentary (the HUD already shows the question).
- Phone-side narration.
