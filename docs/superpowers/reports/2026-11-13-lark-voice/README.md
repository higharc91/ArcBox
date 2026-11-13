# Lark's missing voice clip (#339)

The missing `ch3_guest_right_arrive_3` recording is delivered on `fix/lark-voice-339`, based on the existing `claude/issue-218-lark` story branch (f67658c6). The PR targets that branch to keep this sound fix separate from the already-authored story work.

## Recording and provenance

- Existing Scenario job: `job_9dYTM2t3YLu4vMUA3wgQB1GC`.
- Existing asset: `asset_U8LG8WGH76Qqw7Zqz3mRZy8C`, Gemini 3.8 Flash TTS, Aoede.
- Parent WAV: mono 24 kHz PCM, 19.28 seconds. Its three lines are separated by silences.
- Lossless cut boundaries: samples 0, 134397, 266941, 462720. They lie between complete words, at the midpoints of the two inter-line silences.
- The greeting's raw cut is 8.157458 seconds; the established voice encoder trims it, applies the existing unchanged zero-pitch/no-filter Lark cast, targets −16 LUFS and encodes mono 24 kHz Ogg Vorbis. The manifest duration is 7.4 seconds; decoded samples span 7.35525 seconds.
- Encoded measurement: −16.45 LUFS integrated, −1.15 dBTP true peak, LRA 1.30. The lossy encoding has a small true-peak overshoot relative to the normalization target.
- New generation cost: **0 CU**. The earlier batch receipt records its historical 3 CU; that cost was already incurred before this work.

`tools/tactics-lore/voice-runs/2026-11-13-story/` retains the original request, receipt, sample-accurate cuts, parent SHA256 and unbiased transcript. The cut metadata also identifies the two already-rendered town barks for #340; those lines are not authored on the #218 base branch and are not imported by this PR.

`scope-proof.json` confirms exactly one manifest addition, all 1,399 existing line bindings unchanged, and the encoded file hash. No existing audio file, caption, character cast, gameplay rule or artwork is changed.

## Wording and runtime checks

Unprompted local `Systran/faster-whisper-small.en` recognition matches every word of all three source lines and the final encoded greeting. Case and punctuation are ignored; no expected script or initial prompt is supplied to the recognizer. `encoded-transcript.json` retains the output. This is a lexical check, not human listening or acting approval. Reproduction uses `faster-whisper==1.2.1`, `av==16.1.0`, CTranslate2 CPU int8, beam 5, temperature 0, no VAD, no previous-text conditioning.

The added StoryTrack regression passes for all Lark scene lines: the server reads a positive recording duration, and the TV says the correct recorded file while retaining the exact caption. The broader VoiceTests class passes nine of ten checks; its remaining missing-recording failure is in main's #343 inventory. Coverage moves from 1,501/1,515 clips to 1,502/1,515. All thirteen still-missing renders exactly match main's inventory (`missing-before.json`, `missing-after.json`). No baseline exceptions are added to tests.

The actual Godot 4.7.2 OpenGL host decoded and started the imported file through its normal StoryTrack/Narrator path:

```text
AUDIO Voice clip ch3_guest_right_arrive_3.b3a9b0f4.ogg tactics.story: Look who crossed the desert. ...
STORY 2 ch3_guest_right_arrive_3 said lines
```

`native-audio-log.txt` and the two 1920×1080 screenshots retain that scripted replay. It uses an Ashen Ford demo background and the real Lark lore line; it is not a full Chapter III playthrough. The temporary review entry point is retained as `native-review.patch` and was removed before the final repository check. Godot used its Dummy audio driver; this proves native stream loading/playback requests, not audible hardware output or a physical-TV listening test. Release and native builds each report zero warnings.

Real Chromium checks against a Vite production build of the Dialogue component pass at 320×844 and 390×844: exact caption, loaded portrait, no horizontal overflow or page errors, a 44px skip control, and one line-specific action after two taps. Reduced motion is enabled. The component harness uses real branch pack assets and is not a live multiplayer session. `browser.json`, harness sources and screenshots are retained here; temporary app entry points are removed.

## Reproduce the cuts

Download the existing Scenario asset to `job09-aoede.wav`. From the repository root:

```sh
python tools/tactics-lore/voice-runs/2026-11-13-story/cut-job09-aoede.py \
  job09-aoede.wav /tmp/job09-raw \
  tools/tactics-lore/voice-runs/2026-11-13-story/job09-aoede-transcript.json \
  /tmp/job09-cuts.json
```

The encoder's `encode` and `write_manifest` functions are used directly for the one matching job so the importer does not delete unrelated historical recordings. Their output is the normal pipeline format and semantic filename. The input SHA and transcript are checked before cutting; raw PCM is copied without resynthesis.

The parent story also had three stale exact-count assertions. Its 37 added line IDs replace one old archive line, so the loaded scene total is 981, not 982. The portrait and caption checks still used main's old 945 total. All three exact expectations are corrected without changing any authored content or skipping the per-line checks; `parent-line-count-audit.json` records the IDs. The superseded checks exposed these and were replaced by a complete fresh run after all three corrections.

Final `bash tools/check-all.sh --full` at code head `8031b4ca`: 7,728/7,740 .NET tests pass; 12 fail, zero skipped, zero build warnings. The twelve failures exactly match the recorded main baseline: seven missing-recording checks, scene flags, Ashmaw caption size, two Grisk punctuation checks and a stale bounty fixture. All 601 phone tests and the production build pass, with two existing upstream SignalR annotation warnings; all 24 Node and 96 Python tests pass, as do lore validation and documentation drift. `validation.json` and `full-check.txt.gz` retain the result. The passing full gate remains required before merge or issue closure; CI stays manually disabled.
