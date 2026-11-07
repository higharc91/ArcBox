> Status: Wizard preview complete and local checks pass; awaiting owner approval before the Wizard PR or Sorcerer.

# Wizard pilot — #247

The gallery covers 49 real-engine samples: all 47 Wizard/class-subclass abilities, Empowered Evocation's visible proc, and ordinary Attack. The inventory also lists the two static modifiers; they have no standalone proc.

Each damaging spell has a separate native sheet and identity. Fire fronts, ice shards, lightning forks, poison droplets, force facets, necrotic claws, wards, time glyphs and earth spires follow the shared element palette. Five Wizard casting motions add 34 frames for every race. Other class atlases are unchanged; spell learners use their existing fallback poses. Luminous spells illuminate nearby surfaces and figures. Existing sounds are reused without new clips or paid generation.

The native review exposed an inherited Counterspell explosion. Playback now suspends the incoming spell at a real reaction offer. A successful counter cancels its release; a pass or failed check resumes it after the answer. Five real-engine outcomes, including a Wizard countering a counter to their own spell, verify the camera chain, release ordering and final HP/life state. No gameplay event, rule, number or AI was changed.

## How to review

Open `index.html` locally. Every card has a native before/after movie and a full-resolution poster. The before is on the left. Movies include the full cast, impact, lingering effect and camera settle. The capture uses the ordinary TV scene and BeatPlayer at 1920×1080, fixed simulation 60 fps and capture 30 fps, low preset, software GL under Xvfb. Videos are silent because capture uses the required Dummy audio device; the normal playback still triggers the existing sounds.

The frozen before is `15ecd7a16bc0c094c97028099cc5b70aa4682bc3`: current main when Wizard started (`c4e121eb`) plus the approved shared dependency. This isolates Wizard from the shared cut-in/lighting changes. Only the six neutral fixture/entry/capture files are backported. The approved dependency is in PR #251, awaiting the owner merge. The Wizard branch is stacked on it; no Wizard PR is opened before approval.

## Verification

`bash tools/check-all.sh --full` passes: 7,247 .NET tests, 549 phone tests and production build, 24 Node tool tests, 31 asset tests, 9 audio tests, 23 lore tests, and the lore/documentation checks. Build has zero warnings/errors; no tests are skipped or weakened. Final native Debug builds also have zero warnings/errors. The five real Counterspell outcome cases pass. Nested incantations remain suspended until the entire chain resolves.

A second export matches the shipped effect PNGs byte-for-byte. Lossless empty-margin packing halves their decoded storage, saving 18.10 MiB while preserving all original RGBA pixels, anchors, projectile centres and beam-light reach. A second Wizard atlas bake matches byte-for-byte. All races and facing/hand attachment checks pass. The aimed-spell identity regression compares uncoloured geometry/motion, so a palette swap alone cannot satisfy it. The fast capture encoder is opt-in and matches Godot's decoded viewport pixels exactly (`encoder-check.json`).

`verification.json` confirms that all gameplay fields remain identical. Only AbilityUsed.presentation changes in replay goldens: 2 events in river-crossing and 16 in warlord-bridge. Other replay events, audio clips and phone fixtures are unchanged.

## Budgets

Three independent native runs per version give a class median of **20.64 → 21.31 ms (+3.25%)**, within the 10% class median budget. This is the median of all 49 ability medians; all individual values are in `performance-summary.json`. The largest individual scene change is **chronomancer.temporal_stasis: +22.64%** (20.07 → 24.62 ms). Individual slower scenes remain part of the real-hardware check; the class median does not claim every scene improved.

The whole-reel live texture peak is **234.31 → 273.50 MiB (+39.18 MiB)**. All 47 spells across three subclasses are exercised in one process. Wizard cast frames add 9.375 MiB per Wizard race atlas used; other classes' atlases do not grow. Lossless effect packing saves 18.10 MiB of decoded art. The complete reel's shown time is 64.150 → 63.433 s (-0.717 s). Effect strips are 0.4 s for cantrips and 0.6 s for other spells/procs; real presentation scripts also pass the 0.8/1.5/2.5 s tier budgets. Scorching Ray releases its three real attacks after one wind-up, reducing its base presentation from 2.04 s to 1.48 s. Cinematic defaults remain unchanged, including promotion for a critical hit that downs a foe.

Thirty deterministic six-player Chapter I rounds pass the existing tighter limits. Recorded maximum extra time is 3.317 s Full / 3.017 s automatic Short / 0 s Off, under the approved 3.6 s cap. The full 49-sample reel also finishes sooner than the before reel.

The shipped-content increase is 1.09 MiB (`pack-growth.json`). Live texture measurements report the whole class reel, which retains many more spell sheets than an ordinary encounter. Real-hardware 16 ms frames and the physical 3 m TV readability check remain hardware checks; container median measurements are a relative software-render proxy.

Native startup warnings about Xvfb VSync and unsupported compatibility screen-space AA, plus the eight ObjectDB shutdown instances, are also present on the before capture. No native rendering errors occurred. CI remains manually disabled.

# Approval boundary

The Wizard checklist stays unchecked until the owner's explicit OK. Per #247, that OK authorizes the Wizard PR and the next class, Sorcerer. No later class has started.
