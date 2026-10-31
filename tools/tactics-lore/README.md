# Tactics lore

The world bible for the tactics RPG, as data the game ships: `content/tactics/base/lore/`. The format and the
reasons behind it are in [the lore design spec](../../docs/superpowers/specs/2026-10-31-tactics-lore-design.md).

```bash
python3 tools/tactics-lore/check.py                          # the base lore pack
python3 tools/tactics-lore/check.py <base-dir> <chapter-dir> # packs in load order
python3 -m unittest discover -s tools/tactics-lore           # the checker's own tests
```

Standard library only. `fixtures/valid` shows every file kind in use; `fixtures/invalid` has one of each mistake.
