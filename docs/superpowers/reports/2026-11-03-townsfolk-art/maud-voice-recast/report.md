# Old Maud — approved ancient voice installed

The owner approved the Gacrux audition: “perfect, let's go with that”. All24
Maud recordings now use its ancient, papery, unhurried but lucid performance
brief, pitch0, no filter. The exact approved town audition source is reused;
23 selected new recordings cover the remaining lines. The normal draft voice
sample now plays the installed town recording. A three-line listening reel is
under tools/tactics-assets/imagegen/townsfolk/old-maud/voice-recast/.

Only Maud's24 audio manifest mappings changed. Captions, other speakers' planned
requests/mappings, rules, balance, phone contracts and authored-recordings-only
playback remain unchanged. All old24 production clips, rejected old sample and
six rejected new takes remain available with hashes and provenance. Artwork
appearance approval is still pending before motions/portraits; full-set/TV
approval remains separate. No art or texture assets were generated or changed.

## Rendering and repair

Live schema, cap checks and quotes preceded paid jobs. Scenario's seven-job
concurrency cap was respected by waiting/retrying rejected submissions; successful
job IDs were cached and not resubmitted. No credits or plan upgrades purchased.

Three paid takes narrated the long direction notes instead of applying them.
These were rejected; two were successive attempts at the same line. Diagnostic
ASR confirmed the leakage. All24 selected takes then received unbiased small.en
transcription checks. Three other takes had small word substitutions reported by
both small.en and medium.en and were replaced conservatively. Four final lines
use a shorter age/texture cue. All final caption words match after punctuation,
British/American neighbour spelling and explicitly listed leading ah/oh
interjections; no other word substitutions accepted. These are automated wording
checks, not a claim of independently hearing every performance.

Casting approvedRenderText records preserve the exact approved town request and
four final repair requests. Guards on caption, preset, character direction and
line delivery prevent stale request reuse after edits; regression tests cover
these changes. Source and encoded hashes describe the actual request, not a
renamed substitute. Active selected provenance is in voiceRecasts; six rejected
paid takes are separately recorded in voiceRecastSuperseded.

## Validation

- 24 sources and installed clips decode; mono24kHz16bit sources,5.6–17.52seconds,
 zero clipped samples. Exact approved town source reused; old24 files unchanged.
- Full1443/1443 clip coverage, zero pending renders. No pitch/brightness outliers
 flagged by the full cached voice checker (voice-check.json).
- 23 Python lore/tooling tests pass, including approved-request invalidation.
- 71 focused logic tests and52 story/bark presentation tests pass; both test-project
 Debug builds have zero warnings/errors. Logs are beside this report.
- The Chapter1 longest-clip assertion used an obsolete11-second measurement;
 the new longest is14.3seconds. It now checks the existing30-second budget rather
 than the historical snapshot. The independent fixed Ogg-duration decoder check
 and per-main-story-line bounds remain intact.
- Offline reimport/validation pass using committed batch tools; no service calls.

Production cold pack:210288327bytes /4953files, inventory SHA256
87870feadf6bf4355fc9187ff04997e7fa505215927bc29f938ef0a5424f96c4.
The previous four-party texture peaks328.99MiB /192.37MiB remain the last native
measurement below512MiB. They were not rerun for audio-only changes; texture
content is unchanged. No full-solution/native/phone test run is claimed here.

## Cost and continuation

61 unique successful jobs total274CU:30 speech jobs150CU (including earlier5CU
audition) and31 transcription jobs124CU. This continuation spent269CU; selected
new speech112CU, rejected speech33CU, transcription124CU. Quotes for an unused
large ASR model are not billed. Deduplicated job accounting and receipts are in
batch/cost-summary.json. Team consumption5551→5820 agrees with the269CU delta.
No member/project cap applies; this does not establish remaining plan credits.
No PixelLab calls, credits or purchases.

Board/handoff updated. Rebase before pushing townsfolk-art. No PR, merge, issue
closure or environment publication. Continue with Maud's artwork appearance
approval, then native/staff-contact QA and motions/portraits, then separate
finished-set/TV approval. Do not regenerate completed concepts/art.
