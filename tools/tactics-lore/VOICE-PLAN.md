# Voicing Tactics: plan (on hold until the lore is fleshed out)

**Status:** the pipeline exists (`voices.py`, `voice-casting.json`). Generation is paused because the bark text and the
cast of named characters will change as the lore grows. Any clips already generated are recorded by asset id in
`voice-assets.json`. They persist in the Scenario project, so nothing paid for is lost.

## Rule: one person, one voice, everywhere

A character must sound the same wherever they speak:
- their pre-rendered lines;
- live TTS of a line with no clip (the server's `/api/tts`);
- any later recording.

So casting lives in one place, `voice-casting.json`, and everything reads it.

1. **Presets are shared with live TTS.** The Gemini TTS presets have the same names as the server's Chirp3-HD voices
   (`en-US-Chirp3-HD-<preset>`). Registering each character as a Tactics `VoiceTheme`
   (`tactics.npc.<id-with-hyphens>`) makes live speech fall back to the same voice.
   - `VoiceThemes.IsValidId` forbids underscores, so `halden_varrow` becomes `tactics.npc.halden-varrow`.
   - Set the lore character's `voice.theme` to that id. The schema already has the field.
   - Add a .NET test that every lore `voice.theme` is registered.
2. **No preset is shared between a named character and a player voice set.** A hero must never sound like an NPC.
   - There are 30 presets: 12 for the player casts, which leaves 18 for NPCs.
   - When the cast outgrows that, separate two characters with a **post-process** (a pitch shift, or a filter such as a helm's
     hollow resonance or a lich's chill echo). It's recorded per character in the casting file and applied by `voices.py`.
     These effects can't reach live TTS, so prefer distinct presets for anyone who speaks a lot.
3. **Encounter speakers map to lore characters.** Rules `say` lines name a unit (`warlord`), so the casting file needs
   `aliases` (for example `warlord → vorga`, whose lore sample is that encounter's opening line). Every line, wherever
   it's written, then resolves to one cast.
4. **Drift check.** For each cast, measure median pitch and spectral centroid across its clips, and flag outliers for a
   re-roll. A preset-based TTS voice is stable, but a re-roll is cheap (about 1.3 CU).

## Draft NPC casting (revisit when the lore settles)

| Character | Preset | Notes |
|---|---|---|
| halden_varrow | Charon | warm baritone |
| corvin_ashby | Sadachbia | light, quick |
| tobin_fell | Orus | −2 semitones for the bass |
| rook | Umbriel | dry, unhurried |
| vorga (encounter `warlord`) | Puck | young swagger |
| ivo_rime | Iapetus | soft, precise |
| oswin_hale | Rasalgethi | faint cold echo |
| halvard | Schedar | helm resonance |
| hooded_stranger | Algieba | smooth, ageless |
| lark | Enceladus | breathy, musical |
| bree_tallow | Zephyr | brisk, bright |
| maren_sorrel | Sulafat | warm, persuasive |
| mother_aubrine | Vindemiatrix | measured |
| dagna_holt | Callirrhoe | flat, few words |
| pip_ashdown | Leda +3 semitones | child; shares a preset with `earnest.high`, separated by pitch |
| old_maud | Achernar | papery |
| celandine_marrowby | Autonoe | cultured |
| mother_skarrow | Laomedeia | rasping |
| ysolde | Aoede | soft, unplaceable |
| narrator | a host voice, decided with the TV's narrator | |

## What to voice, once the lore settles

- The player voice sets: 6 sets × 17 barks × 2 casts.
- Each NPC's lines and barks (`barks/<speaker>.json` once written).
- Encounter `say` lines.
- The narrator.

Then import with `voices.py import`. It trims silence, normalises to −16 LUFS, keeps the 24 kHz source rate, gives
byte-identical output, and writes `audio/manifest.json`.
