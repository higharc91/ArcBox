# Nineteen completed library appearances — issue #272

[Standalone embedded gallery](index.html) · [All nineteen before/after](all-19-before-after.png) · [Eight-direction walking reel](all-19-walking.mp4)

All nineteen follow-up appearances have complete pixel figures at their approved family/world scale. Every mob has eight genuine facings, directional idle/movement/natural attacks and hurt/down poses. Existing creature abilities select distinct boulder, roar, breath, wingbeat or Life Drain gestures where applicable. No new encounters or combat powers were added.

The 24-second reel shows all nineteen together in eight directions at runtime cadence, with one shared world zoom. The fighter is its actual standing front idle, retained as a party size reference; no additional player facings are invented. The comparison uses historical and refreshed front idle pixels at the same zoom. The gallery embeds both media items and the final integrated battle, overworld and twelve-contact inspection images and links the seven full action/native scene reviews. It works offline when downloaded; each batch gallery is independently self-contained.

The project tracker now records 60 refreshed designs across 26 batches: 29 combat designs and all 31 formerly older library appearances. The 26 established eight-direction library looks and historical 98-look baseline are preserved. Player feedback from the TV/phone session remains open on [issue #272](https://github.com/higharc91/ArcBox/issues/272).

The follow-up is isolated on `mob-library-refresh-272`. Its final integration also preserves the separately merged combat VFX and phone-map work from main `0d808be20ccb00e3fe3891983c40c60470e29cbc` (PR #277). Verification details are in `verification.md` and the machine-readable receipts beside this file. Main has not been changed by this follow-up.

Reproduce from the repository root:

```sh
python tools/tactics-assets/imagegen/library-refresh-272/completion/compose.py
node tools/tactics-assets/imagegen/library-refresh-272/completion/verify.cjs
python tools/tactics-assets/imagegen/library-refresh-272/completion/verify-integration.py
python tools/tactics-assets/imagegen/library-refresh-272/completion/verify-provenance.py
```

Use the repository's Playwright dependency and browser environment for Node commands. Composition only crops/scales existing whole figures uniformly with nearest-neighbour sampling; it never paints, deforms or invents anatomy. All creative source figures, exact prompts, rejected drafts and receipts are preserved in each batch's authoring directory.

Per-batch scope receipts describe each batch's recorded art checkpoint. To reproduce those historical checks, use the corresponding commit before later batches/main integration. The combined `verify-integration.py` audit applies to this final tree and checks all nineteen additions against current main's preserved content.

To repeat native replay capture after building/importing the host, set `DISPLAY` and optionally `GODOT`, then run `python tools/tactics-assets/imagegen/library-refresh-272/completion/record-native.py` followed by `collect-native.py` from the same directory. Rebuild the combined gallery with `compose.py --reuse-video` when only the native stills change.
