# Wardenflame voice production

Current playback policy (issue #76 owner decision): Tactics uses authored recordings and captions only. A missing or unreadable recording is logged and never invokes live TTS; registered voice themes remain for possible future use. Earlier live-fallback verification below is historical.

The NPC/quest slice is rendered and integrated. Casting is authoritative in `voice-casting.json`; paid jobs and asset IDs, including superseded takes, remain in `tools/tactics-lore/voice-assets.json`. See [the delivery report](../../docs/superpowers/reports/2026-11-03-npcs-quests/README.md) and its voice comparisons.

## One person, one cast

Named characters keep their existing preset, pitch and filter across main scenes, barks and quests. Encounter alias `warlord` resolves to Vorga. Host Puck remains outside Wardenflame. Ten `townsfolk_*` entries provide one voice per placed preset, each distinct from named/player casts by preset plus pitch/filter. Future content must resolve through this file; do not choose a new voice per line.

| Speaker | Gemini / Chirp3-HD preset | Post pitch (semitones) | Filter |
|---|---|---:|---|
| narrator | Algieba | +0 | — |
| halden_varrow | Charon | +0 | — |
| corvin_ashby | Sadachbia | +0 | — |
| tobin_fell | Orus | -2 | — |
| rook | Umbriel | +0 | — |
| vorga | Iapetus | +1 | — |
| ivo_rime | Enceladus | +0 | — |
| oswin_hale | Rasalgethi | +0 | echo |
| halvard | Schedar | -2 | helm |
| hooded_stranger | Sadaltager | -2 | echo |
| lark | Callirrhoe | +3 (ch3_guest_right_arrive_3 −2, bark_lark_ally_down_1 0, via `linePitch`) | — |
| bree_tallow | Zephyr | +0 | — |
| maren_sorrel | Sulafat | +0 | — |
| mother_aubrine | Vindemiatrix | +0 | — |
| dagna_holt | Gacrux | +0 | — |
| pip_ashdown | Leda | +3 | — |
| old_maud | Gacrux | +0 | — |
| celandine_marrowby | Autonoe | +0 | — |
| mother_skarrow | Kore | +0 | — |
| ysolde | Despina | +1 | fey |
| townsfolk_farmer | Charon | -2 | — |
| townsfolk_farmhand | Achird | +2 | — |
| townsfolk_fisher | Algenib | -2 | — |
| townsfolk_merchant | Zubenelgenubi | +2 | — |
| townsfolk_guard | Fenrir | -2 | — |
| townsfolk_barmaid | Zephyr | +2 | — |
| townsfolk_child | Laomedeia | +4 | — |
| townsfolk_goblin_archer | Enceladus | -3 | — |
| townsfolk_goblin_cutthroat | Iapetus | -3 | — |
| townsfolk_goblin_spear | Orus | -4 | — |

The 12 player casts remain in `sets`, unchanged. New NPC/town themes are registered as `tactics.npc.<speaker-with-hyphens>`. Story speech is enabled by default, using a rendered clip first, then the speaker's registered theme. Captions are always available. **Provider limit:** Chirp3-HD accepts the same preset but rejects post-pitch/filter settings; these effects apply to rendered audio, while built-in speech has pitch hints. Live cloud fallback therefore matches identity/preset but cannot reproduce all offline processing. Full rendered coverage avoids that difference for the delivered pack.

## Rendering and immutable content

```sh
python3 tools/tactics-lore/voices.py plan > /tmp/voice-jobs.json
# Request Scenario renders outside the script; download each raw WAV by job/asset ID.
python3 tools/tactics-lore/voices.py import /path/to/raw-wavs
python3 tools/tactics-lore/voices.py check
```

Scenario model: `model_google-gemini-3-8-flash-tts`; language en-US. Use the team/project recorded in `voice-assets.json`. Estimate and report cost before generation, batch jobs, and record every paid result. Pass delivery-aware `renderText` to synthesis while retaining exact caption text in lore. Every new conversation/quest line has speaker, mood and direction. Directions specify register and natural delivery, avoiding a different character voice for a shout.

Legacy caption/cast hashes remain unchanged. New `sq_`, town and reactive Pip lines use a versioned delivery hash so changing direction requests a new take without invalidating the old pack. Filenames include the semantic voice/text hash; raw IDs additionally include delivery. Import trims silence, applies declared pitch/filter, normalizes to −16 LUFS and deterministically encodes mono 24 kHz Ogg Vorbis. The manifest maps stable line IDs to cast files. Reused recordings require identical caption and cast settings, with provenance recorded.

## This delivery's budget and quality checks

104 initial renders (96 new conversation/quest/town lines and 8 missing camp lines) cost 196 CU. Eight Tobin refinements cost 21 CU; two Vorga repairs cost 1 and 2 CU. **114 paid renders, 220 CU total**, estimates reported before each batch. Original/replaced assets remain recorded. Nine additional Mother Skarrow/Lark/Dagna town barks reuse existing matching recordings with source IDs and zero generation cost.

The checker measures silence, clip pitch and spectral centroid, caches by filename/size under ignored `.cache/`, and defaults to `.cache/tactics-voice-check.json`; pass a destination after `check` to save a delivery report. The historical NPC delivery remains at `docs/superpowers/reports/2026-11-03-npcs-quests/voice-check.json`. It flags extreme individual pitch drift (>10 semitones), new-vs-old speaker median drift (>6 semitones), and centroid ratios outside 0.60–1.65. The NPC delivery had zero flags. These are mechanical checks; the report includes old/new listening samples for human review.

## Ashmaw bounty (#74)

Grisk uses **Algenib +4 semitones**, with a high nasal, raspy foreman delivery. Existing speakers retain their cast settings. The bounty's paid voice work costs **52 CU**, including the owner's approved “You no take candle!” take. Batch source assets and sample-accurate cuts are recorded under `ashmaw-voice-runs/` and `voice-assets.json`; superseded paid clips are retained. Caption cuts preserve the existing three-row phone limit rather than shrinking text or expanding controls.

The candle line is an ordinary hit bark at weight 1 beside Grisk's weight-6 hit line. Low-health and defeat lines take priority. Its approved encoded file is `bark_grisk_candle_1.f31d8679.ogg`; preserve that exact take. Grisk never borrows Ashmaw's sounds; Ashmaw has creature SFX and no speech. The bounty drift report is `docs/superpowers/reports/2026-11-03-ashmaw-bounty/voice-check.json`, with zero flags at this checkpoint.

Tobin retained Orus −2; natural directions improved spectral consistency. Vorga's existing ridge command had an octave-high take; the directed replacement retains Iapetus +1 and measured median F0 145.45 Hz. These repairs do not change captions or cast identity. All scene/bark lines are covered by rendered clips and duration tests; there are no failed-render caption-only exceptions in this delivery. Quest objectives/history are read silently and are not voiced.

## Dagna veteran recast (#80)

The owner approved the Gacrux audition on 2026-11-04. Keep its mature, low, flat
Northern delivery and no post pitch/filter. Dagna's casting `direction` applies
to every line, including legacy battle barks, and combines with any authored line
direction. Direction enters both raw and encoded hashes so changes request a new
take. Other speakers without a cast direction retain their existing hashes.
Gacrux is also the grim.high player preset; Dagna's approved performance direction
distinguishes her. Do not recast the player. Captions and authored-recordings-only
playback remain unchanged. Retain superseded files and paid provenance.

## Old Maud ancient-voice recast (#80)

The owner approved the papery, unhurried Gacrux audition. Use its105-year-old
performance direction across all24 Maud lines, without post pitch/filter. The
existing town audition is reused exactly. Casting approvedRenderText retains
that paid take's exact request while its caption/preset/character direction/line
delivery remain unchanged; editing any of those requests a new take. Caption text
and authored-recordings-only playback stay unchanged. Preserve older recordings
and paid provenance. Gacrux is shared with Dagna/grim.high; Maud's approved ancient
performance direction is distinct from their deliveries.

Installed all24 lines; full1443/1443 coverage. The approved town request and four
shortened vocal-cue repair requests are recorded as approvedRenderText overrides;
the latter avoid direction narration or repeated lexical substitutions. Their
caption, preset, character direction and authored delivery guards still apply.
Six rejected paid takes are in voiceRecastSuperseded; active voiceRecasts contain
only selected takes. Reproduction and caption-check tools/receipts are in
old-maud/voice-recast/batch. Total274CU including5CU earlier audition; no purchase.


## Mother Skarrow approved elder-voice recast (#80)

Owner approval: “sounds good, use it”. Kore at pitch0 with no filter; keep the
approved low husky elderly goblin-matriarch direction, measured wary authority
and tired warmth across all67 lines. Preserve captions and recordings-only
playback. ApprovedRenderText guards capture actual render requests, including
numeric40 for an ambiguous opening number, the approved cue without Mood prose,
and supported pauses for clarity; they do not alter canonical caption words.

Retain original67 recordings and all superseded paid takes. Sources, live
quotes/cap checks, receipts, source hashes and offline import/local speech-check
tools live in mother-skarrow/voice-recast/batch. Local small.en/medium.en ASR is
unbiased and free of per-clip service charges; it is a wording check, not acting
judgment. Generic goblin art and battle/phone contracts are unchanged. Artwork
approval gates remain separate from the approved voice.

All67 clips are now installed from64 selected takes; full1443/1443 coverage.
Two chapter3 takes use only documented pause tags with the same Kore speaker,
language and zero pitch/filter because descriptive instructions were repeatedly
narrated. Exact alternate requests are guarded and retained; source/caption and
pitch/brightness checks pass. Original67 clips and20 superseded new paid takes
are preserved. Continuation218CU, total224CU including prior6CU audition/ASR;
local full-set transcription carries no per-clip service fee. No purchases.
