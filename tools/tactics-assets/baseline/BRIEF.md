# Asset polish pass: shared rules for every workstream

The owner wants the whole Tactics art suite polished to a high standard. It will run on a strong gaming PC: HD-2D-style 3D tiles
with pixel sprites, shown at about 3–4× on a TV. Five workstreams run **at the same time in the same checkout**. Read
`tools/tactics-assets/README.md` and `STYLE.md` first. They are the contract, and STYLE.md's look rules still hold:
- light from the top-left;
- hue-shifted 6-shade ramps;
- `#140f1a` selective outlines;
- clusters, not noise;
- nearest-neighbour only.

## What "polished" means here (from genre references: HD-2D games, Slynyrd's pixelblog on melee attacks)
- **Silhouette first.** Every unit should read at 1× from its shape alone, and be distinct from its neighbours at TV distance.
- **Clean shading, not salt-and-pepper.** Big, readable light and shadow masses, with clustered texture. Lone noisy pixels on fur, scales or
  stone read as grime. Use `tex.despeckle`, or draw texture as small clusters.
- **Animation.**
  - Anticipation, then a short action frame, then a held impact, then recovery.
  - One smear frame for swings. No smear on thrusts.
  - Hurt is short; magic gathers for longer.
  - Secondary motion: hair, capes, tails and ears lag the body by a frame and settle.
  - Idles breathe with a 1 px rise.
  - More frames are welcome where they buy smoothness on a fast PC (walk 6–8, attacks 6–8), but every frame must earn its place.
- **Effects for a strong GPU.**
  - Layered: a bright core, a coloured body, then sparks and particles, then a fading residue.
  - Additive glow where the manifest says `add`.
  - Readable frame by frame.
  - More frames and bigger frames are fine.
- **Consistent scale and anchors** with the existing sets. Check with dioramas and the showcase pattern.

## Hard contract (breaking these breaks the game or other workstreams)
1. **Never remove or rename an art key.** The rules pack references keys built from every manifest
   (`set.asset` and `set.asset.variant`, see `dev_keys.py`). After building, run:
   `python3 tools/tactics-assets/dev_keys.py check tools/tactics-assets/baseline/art-keys.json`. It must report 0 gone.
   - Adding keys is fine.
   - Changing frame counts or sizes is fine **if the manifest describes them**.
2. **A brand-new beast or boss creature** (a new top-level entry in `beasts` → `beasts` or `bosses` → `monsters`) fails a C# test
   unless the rules pack has a stat block for it. Don't add stat blocks.
   - New *variants* of existing creatures are fine.
   - Brand-new creature art goes in a new set, `bestiary` (see workstream B).
3. **Only edit the files your workstream owns** (listed in your prompt). Other agents are editing other files right now.
   - `arcpix/*`: additive changes only (new functions or new modules). Never change an existing function's behaviour, because
     every other set depends on them. Re-read a file just before editing it, and keep edits small.
   - `build.py`: only workstream A may edit it, and only its character functions. If you add a new set module, don't
     register it in `build.py`. Tell the integrator in your final report. Build it directly:
     `cd tools/tactics-assets && python3 -c "import sys; sys.path.insert(0,'.'); from parts import NAME; NAME.build()"`
   - `README.md` and `STYLE.md`: don't edit them. Put the documentation text for your changes in your final report, and the
     integrator merges it.
4. **Build only your own sets.** `python3 tools/tactics-assets/build.py SETNAME ...`. A full build takes 18 minutes and
   collides with the others. **Don't** build `showcase` or `gallery`; the integrator does that at the end.
5. Output must stay **deterministic**: seed every random source. Running your build twice must give byte-identical files.
   Check with `git status`/`md5sum` on one or two outputs after a second build.
6. **Don't run `dotnet`**. Concurrent builds lock each other; the integrator runs the C# tests. **Don't commit or push**;
   the integrator commits each workstream's paths.
7. Python 3.13, Pillow and numpy only. Lines ≤ 120 chars. Docstrings say *why*. Match the surrounding code.

## Judging your own work
Look at what you make. Use the Read tool on preview PNGs (they render as images), and render zoomed crops with
`dev_zoom.py` or your own throwaway script. Put throwaway scripts and renders in
`/tmp/claude-0/-home-user-ArcBox/96209855-791c-56f7-b997-7ec0e92b7063/scratchpad/<your-workstream>/`.
- Compare before and after side by side. The `git show HEAD:path > file` gives you the old one.
- Iterate until it's clearly better. If a change isn't clearly better, revert it.
- Regenerate your preview sheets (`previews/NN-*.png`, `.gif`) so a reviewer can see the result.

## Final report (your last message, at most ~60 lines)
- What changed, per set, and why it's better. Name the before/after preview files.
- New assets and new keys. New set modules that need registering in `build.py`, and where in the order.
- Any manifest contract changes (frame counts, new fields) that the game or other sets must know about.
- Documentation text for the README (your section, ready to paste).
- What you'd do next with more time.
- The output of the `dev_keys.py check` line.
