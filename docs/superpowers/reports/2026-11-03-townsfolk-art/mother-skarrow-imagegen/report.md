# Mother Skarrow — completed artwork review

## Latest checkpoint — Mother Skarrow completed animation/portrait/voiced TV review

Owner appearance approval: “she looks good, do the animations”. Reused the existing
approved appearance/concept and all67 installed Kore voice lines. Artwork is now
ready for **separate complete-set/TV approval before production installation**.
Old Maud appearance/voice already approved; her completion artwork stays queued.

Eight real directions,288 native frame slots: six each idle/walk/talk/staff attack/
cast, three each hurt/down. Downed holds the authored lying frame35. Six40×40
portraits. Compact96×96 frames, anchor48/84, pixelScale0.5, two rows/direction,
1728×1536 atlas10.125MiB, shared96-colour palette/binary alpha. Idle body24.5–25.5
world pixels versus human36; no mirrored facings or per-frame recentering.
One fixed transform per direction; maximum vertical foot drift1world pixel.
Staff stays anatomicalLEFT. West duplicate staff repaired in one retained tool
edit. Connected-component frame extraction preserves source art extending into
magenta gutters without importing neighbouring staffs; no redraw or synthetic
pose construction. Native/source geometry checks complement visual owner review.

Review: tools/tactics-assets/imagegen/townsfolk/mother-skarrow/
mother_skarrow-finished-review.mp4 (31.6s; approved installed town2 voice),
mother_skarrow-closeup-review.gif and mother_skarrow-portraits-review.png.
Native TV still under this report’s ashen_ford-shots/world-mother-skarrow.png.
Private review pack /workspace/artifacts/issue80-mother-skarrow-review only.
Named overworld variant, named battle sprite, named lore portrait and registered
art aliases leave generic goblin_shaman and separate Skarrow Stormcaller intact.
Original slain presence gate retained. Gameplay fields and phone contracts unchanged.

Checks:161 focused presentation tests pass; Host and test Debug builds0warnings/
errors. Typed runtime verifies named body/portraits, eight facings, compact atlas
source rectangles, combat/down playback and bark stop/resume. Source/palette/
portrait/hash/unique-frame/production-isolation checks pass. Native software
Vulkan four-party peaks **340.48MiB Ashen Ford /192.37MiB Cinder Hollow**, below
512MiB; open/crossing/party-change/restored and all8human/elf class texture uploads/
releases pass the strict memory checker. These are memory figures, not GPU timings.

11 image-tool calls total: previous appearance1 +completion10 (eight direction
sheets, portraits, west repair). All raw exports/exact requests/input/output hashes
retained; offline rebuild verified. No PixelLab/speech generations, credit purchases
or upgrades. Image tool exposes no price; no monetary estimate inferred.
Production cold pack unchanged215550416bytes /5027files, inventory SHA256
1373f33f5c9b56f62bd26015f3a87fe6bc23c8793fc587e5246d7d0f8fb99a3d.
Installer rejects before any production write without exact finished-set/TV owner
approval;25 review assets and native TV still are hash-bound. Do not infer this
approval from the appearance or voice approvals. Do not regenerate completed art.

Rebase before pushing townsfolk-art; no PR, merge, issue closure or environment
publication. Earlier pending-appearance notes are historical and superseded.

Reproduce offline from the repository root:

```sh
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/prepare-guides.cjs
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/key-sources.cjs directional/south-export.png directional/south-west-export.png directional/west-export.png directional/west-corrected-export.png directional/north-west-export.png directional/north-export.png directional/north-east-export.png directional/east-export.png directional/south-east-export.png portraits-export.png
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/build-review.cjs
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/build-portraits.cjs
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/verify-review.cjs
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/build-closeup.cjs
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/build-review-pack.cjs /workspace/artifacts/issue80-mother-skarrow-review
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/prepare-native-review.cjs /workspace/artifacts/issue80-mother-skarrow-review
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/verify-pack-isolation.cjs /workspace/artifacts/issue80-mother-skarrow-review
node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/verify-receipts.cjs
```

For typed checks, activate `/workspace/.arcbox/activate.sh`, then run
`dotnet run --no-restore tools/tactics-assets/imagegen/townsfolk/mother-skarrow/verify-runtime.cs -- /workspace/artifacts/issue80-mother-skarrow-review`.
First restore requires configured proxy/network access for NuGet vulnerability
metadata. Native launcher `native-review.py` accepts `ashen_ford` or `cinder_hollow`;
activate the SDK first and use the installed Xvfb/software Vulkan environment.
Rebuild the voiced video with `build-video.cjs <native-TV-still>`.


## Latest checkpoint — Mother Skarrow approved and installed

Owner full-set/TV approval: “looks good, I approvee” (2026-11-05).
Installed the exact approved288-frame, eight-direction body atlas and six40×40
portraits after validating all25 review asset hashes and the approved TV still.
No new art or voice generation, credits, purchases or upgrades. All67 approved
Kore voice lines retained. Eleven named townsfolk artwork sets now installed.

Mother uses a placed goblin_shaman_mother_skarrow variant, named battle sprite
characters.mother_skarrow and named lore portrait preset. Generic goblin_shaman
and separate Skarrow Stormcaller remain unchanged. Staff attack/cast/hurt/down
playback included; lying down frame35 is held. Slain presence gate unchanged.
Added two registered cosmetic aliases, seven art files and six cosmetic document
edits only. Installer preserves compact existing JSON formatting. Every other
5021 baseline content file is byte-identical to9a208878. No gameplay, balance,
phone contract, caption or voice changes; all old assets preserved.

170 focused presentation tests pass, including named body/portrait/region preload
and generic shaman separation. All-atlas coverage now covers Mother’s authored
combat clips without treating other civilians as combatants. Typed production
checks verify eight facings/source rectangles/combat/down, six portraits,
bark stop/resume, named battle alias and original slain gate. Host/test Debug
builds have0warnings/errors. Native installed-pack software Vulkan four-party
peaks340.48MiB Ashen Ford /192.37MiB Cinder Hollow, below512MiB; strict memory
checks cover open/crossing/party-change/restored and all8human/elf class texture
uploads/releases. Software renderer figures establish memory, not GPU timing.

Production pack216586388bytes /5034files, inventory SHA256
728704ed27106c3fe7818874f4d3537882c8ba9bb9c1aead254cf0435a4f8147.
`mother-skarrow/verify-installed.cjs` verifies exact approved art, all retained
assets and cosmetic-only differences against the committed pre-install baseline.
It needs local Git subprocess access. Historical private-review isolation
artifacts remain frozen as pre-install evidence, including their old inventory.
Use verify-installed for the current installed checkpoint instead.

Native production screenshots/logs/memory/test/runtime evidence are retained at
docs/superpowers/reports/2026-11-03-townsfolk-art/mother-skarrow-imagegen/production/.
Do not regenerate completed assets or ask for Mother approval again. Old Maud’s
appearance and24-line voice are already approved; her completion animations and
portraits are next queued work, with a separate finished-set/TV gate.

Rebase before pushing townsfolk-art. No PR, merge, closure or environment publish.
Earlier pending-Mother approval/installation notes are historical and superseded.
