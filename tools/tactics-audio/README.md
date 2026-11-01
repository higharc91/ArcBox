# Tactics audio

The tactics RPG's sound effects, ambience and music: lists of what to generate, records of what was generated, and an
import script that turns the downloaded clips into the game's `sfx` and `music` art sets.

| File | What it is |
| --- | --- |
| `sfx-list.json` | Every sound effect and ambience loop: prompt, length, variants, group, what triggers it. |
| `music-list.json` | The music cues, same format, plus a fixed `seed` per cue. |
| `sfx-assets.json`, `music-assets.json` | What was generated: model, team, project, and per clip its Scenario asset id and job. |
| `audio.py` | `plan`, `status`, `check` and `import` (needs ffmpeg). |

Output: `content/tactics/base/sfx/` and `content/tactics/base/music/`, each with Ogg files and a `manifest.json`.

## List format

```jsonc
{
  "voices": {                        // sfx-list.json only: one fixed voice per creature family
    "goblin": { "description": "small wiry goblin, nasal raspy high-pitched voice", "seed": 7100, "monsters": "goblin, ..." }
  },
  "swing_sword": {
    "prompt": "Steel sword swinging fast through the air, sharp metallic whoosh",
    "negative": "music, speech, voice, reverb tail, background noise",
    "duration": 0.4,                 // seconds, sent on every call
    "variants": 3,                   // clips to generate (numOutputs); pick one at random in game
    "group": "weapon",               // battle, impact, weapon, spell, creature, footstep, ui, ambience | music, sting
    "loop": false,
    "use": "Swing for longsword, shortsword, ..."
  },
  "goblin_hurt": { "...": "...", "voice": "goblin", "seed": 7100 }
}
```

Creature prompts are the family's `voices[family].description` word for word plus an action ("short pained yelp when
struck"), all generated with the family's seed, so a goblin's attack, hurt and death cries sound like the same goblin.
To add a creature sound, reuse that prefix and seed. A sound with `variants: 1` has one clip, `<id>`; with n it has
`<id>.1` .. `<id>.n`. Those clip ids key the assets files and name the downloaded and encoded files.

## Pipeline

```bash
python3 tools/tactics-audio/audio.py plan                # clips listed but not yet in *-assets.json (JSON jobs)
# generate each job with Scenario (SFX: model_mirelo-sfx-2-0-text-to-audio, wav; music: model_elevenlabs-music-v2-5),
# record the asset ids in *-assets.json, download each clip to <dir>/<clip id>.wav (or .mp3)
python3 tools/tactics-audio/audio.py check  <dir>        # length, loudness, peak; flags silent, clipped, off-length clips
python3 tools/tactics-audio/audio.py import <dir>        # encode everything found, drop stale files, write manifests
python3 tools/tactics-audio/audio.py status [<dir>]
```

`import` trims silence from one-shots (with 5 ms edge fades) and leaves loops at full length, crossfading each loop's
tail into its head (1 s for ambience, 2 s for music) so it repeats without a seam. It then gains each clip to its
target: SFX -18 LUFS, UI -21, ambience -24, music -20, stings -19. The sample peak is capped at -1 dBFS, with at most
6 dB of limiting, so very spiky clips (footsteps, clicks, dice) can end up a few LU under target. `import` lists those.
Output is 48 kHz Ogg Vorbis: SFX mono at q4, music stereo at q5. Encoding is bit-exact, so a re-import of the same
input gives byte-identical files.

## Manifests

```json
{ "about": "...", "model": "...",
  "sounds": { "swing_sword": { "files": ["swing_sword.1.ogg", "swing_sword.2.ogg", "swing_sword.3.ogg"],
                               "loop": false, "group": "weapon", "use": "..." } } }
```

The music manifest uses `tracks` in place of `sounds`. The art loader turns every entry of a top-level object into a
key `<set>.<id>`, so `sfx.swing_sword` and `music.battle` are valid presentation keys. Keep any other top-level value a
string, or it creates stray keys.
