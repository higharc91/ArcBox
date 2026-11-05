# Wardenflame: rest and recovery (#71)

Status: implemented and verified on `rest-and-recovery`; no PR requested. This extends the merged shops foundation at f4a28fc.

## Owner decisions

HP and spell slots reset every fight. Injuries carry over. Battles ending in victory or defeat and completed story beats grant one
non-stacking long rest; interactions and camp talks do not. A camp or inn night spends it, and temple healing needs none.
The leader proposes a night; present players vote for 20 seconds, with unanswered votes counting as Rest. A strict majority is required.
A tied vote leaves the party walking. Existing 5d vote screens, presence holds and narrative machinery are reused.

| USER DECISION | Default implemented |
|---|---|
| R1 | Black eye mends with rest; bruised pride/shaken are spirit; all other existing injuries are body. |
| R2 | Tend Medicine DC 12; Rally better of Performance/Persuasion DC 12; Cook Survival DC 10. |
| R3 | A known rules-pack healing spell gives advantage on Tend. |
| R4 | Successful cooking gives every character Well fed: 3 temporary HP at the next battle start, removed at its end. |
| R5 | Keep watch rolls Perception and reveals unfound hidden things within 6 tiles of the fire that its total meets. |
| R6 | An inn room costs 10 gold per character. |
| R7 | Temple healing costs 25 gold per chosen injury. |
| R8 | The inn needs and spends the party's rest. |
| R9 | A player can pay from their own purse for another character, including an away character. |
| R10 | Camp only at two authored Ashen Ford campfires. |

The owner also requested cozy campfire music. Use the existing acoustic `camp` loop under `amb_campfire` during jobs, results and talks; the inn uses the same music without outdoor ambience. The later owner-approved presentation follow-up adds an original synthesized flute/harp/chime sunrise sting, then returns to regional music. See the [accepted camp presentation](2026-11-03-tactics-camp-presentation-proposal.md).

These are the issue's recommended defaults, open to owner override; no additional economy values are introduced.

**Issue #83 (owner decisions of 2026-11-03):** three more rest-kind injuries, Scraped hands (attack −1), Singed (no effect) and Twisted
knee (move −1), are a trap's minor injury out of battle; they carry `"battle": false`, so a fallen hero's table is unchanged, and any camp
or inn dawn mends them as it does a black eye. Each long rest (`RestSeq`) also reopens the failed lock ways and searches of the
[interactables design](2026-11-02-tactics-interactables-design.md) §3.2.

## Save and transaction design

WorldSave adds optional RestReady, Camp, RestSeq and seen camp scenes; MemberRecord adds optional WellFed. Defaults are omitted,
with no version bump. Camp preserves any paused story cursor. Job choices, targets, rolled dice and applied results are saved.
Results resolve in profile order and each roll/heal is one save with its result line. A restart replays presentation, never the effect.
Jobs and inn purchases have 30 seconds. Unchosen jobs become Rest easy. Everyone's rest-kind injuries heal at dawn, including away members.
Inn rooms heal every injury on the guest and give that guest Well fed. Temple services immediately heal one exact chosen injury.
Shop services share the existing trade-ID window and atomic purse/save path. RestSeq drives per-line rest restocking without resetting
chapter/never stock. Camp talks play at most one eligible unseen scene each night, preserving the normal 5d line and vote cursor.

## Implementation checklist

- [x] Rules, validation, injury kinds, campfire, shop services, Well fed passive, camp lore tags and parity checks.
- [x] Save compatibility and one-rest earning.
- [x] Camp vote/jobs/targets/rolls/watch/talk/sleep/dawn with restart and disconnect coverage.
- [x] Inn and temple services, sponsorship and atomic replay protection.
- [x] Phone screens and seven new fixtures; existing fixtures unchanged.
- [x] TV gathering, night/dawn, results, effects, sounds and camp demo.
- [x] Two campfires, inn/chapel, Chapter 1 talk and deterministic headless export.
- [x] Art prompts, handoff and TV documentation.
- [x] Live camp/inn and Chapter 1 force e2e; final validation report.


## Owner follow-up: compact recovery and injury art

The owner requested a cleaner inn/rest interface, HP/resource bars, and PixelLab injury icons. Recovery now shares compact party
cards across camp, inn and temple on phones and TV. Cards show battle-ready HP (including injury penalties) and each class resource.
Injuries appear beside the character as tappable debuff icons; the effect and treatment action open in a detail sheet. Room booking
is one action per character. Explanations and session controls use disclosure, and roll screens can expand the party list.

This follow-up authorizes art generation for the eight existing injury icons, replacing the issue's placeholder-only constraint for
those icons. PixelLab Pixen generated native 32×32 PNGs; no image edits or audio generation. The stable injury id resolves through
`content/tactics/base/injury_icons/manifest.json`. `InjuryIcon` and the recovery card components support future screens, and the TV
reads the same manifest. Prompts, model, seed and job provenance are in [the icon brief](2026-11-03-tactics-injury-icons.json).

Four-character recovery menus were checked at 390×844, and at 320px width with no horizontal overflow. A six-member TV inn panel
was rendered in the camp demo. The [report](../reports/2026-11-03-rest/README.md) records checks and screenshots.


The owner subsequently authorized the full cozy camp presentation and new art. The TV now focuses on the live campsite with
job poses, PixelLab props/loops, honest progress and compact party bars. Saved sleep (3 seconds) precedes sunrise (8 seconds),
both skippable. Recovery applies once at dawn; morning continues after leaving camp. The inn shares the night progression with
its own hearth interior. [The follow-up report](../reports/2026-11-03-camp-presentation/README.md) records the final preview and checks.
