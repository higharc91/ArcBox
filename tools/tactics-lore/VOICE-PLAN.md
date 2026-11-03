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
| lark | Aoede | +0 | — |
| bree_tallow | Zephyr | +0 | — |
| maren_sorrel | Sulafat | +0 | — |
| mother_aubrine | Vindemiatrix | +0 | — |
| dagna_holt | Callirrhoe | -1 | — |
| pip_ashdown | Leda | +3 | — |
| old_maud | Achernar | -1 | — |
| celandine_marrowby | Autonoe | +0 | — |
| mother_skarrow | Laomedeia | -2 | — |
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

The checker measures silence, clip pitch and spectral centroid, caches by filename/size under ignored `.cache/`, and writes `docs/superpowers/reports/2026-11-03-npcs-quests/voice-check.json`. It flags extreme individual pitch drift (>10 semitones), new-vs-old speaker median drift (>6 semitones), and centroid ratios outside 0.60–1.65. The final pack has zero flags. These are mechanical checks; the report includes old/new listening samples for human review.

Tobin retained Orus −2; natural directions improved spectral consistency. Vorga's existing ridge command had an octave-high take; the directed replacement retains Iapetus +1 and measured median F0 145.45 Hz. These repairs do not change captions or cast identity. All scene/bark lines are covered by rendered clips and duration tests; there are no failed-render caption-only exceptions in this delivery. Quest objectives/history are read silently and are not voiced.
