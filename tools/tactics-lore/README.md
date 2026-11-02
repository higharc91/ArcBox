# Tactics lore

The world bible for the tactics RPG, as data the game ships: `content/tactics/base/lore/`. The format and the
reasons behind it are in [the lore design spec](../../docs/superpowers/specs/2026-10-31-tactics-lore-design.md).

```bash
python3 tools/tactics-lore/check.py                          # the base lore pack
python3 tools/tactics-lore/check.py <base-dir> <chapter-dir> # packs in load order
python3 -m unittest discover -s tools/tactics-lore           # the checker's own tests
```

A battle beat's scenes are its lead-in, played before the fight, unless they're marked `"after": true`: those are its
aftermath, played once the fight is won. Where a scene sits in the file doesn't make it an aftermath; only the flag
does. The checker refuses `after` on any other kind of beat, and a lead-in placed after one of its beat's aftermath
scenes. The server's loader (`LoreBook`) holds the same two rules.

Standard library only. `fixtures/valid` shows the file kinds in use, `fixtures/story` adds arcs, and `fixtures/invalid` has one of each mistake.
