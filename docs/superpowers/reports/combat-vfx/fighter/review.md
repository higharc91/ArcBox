> Status: Fighter recovered after workspace loss. Focused checks and native review are in progress; owner approval remains pending.

# Fighter — #247

The 35 real-engine comparisons cover all 20 abilities, seven visible hooks, ordinary Attack and seven level-10/wounded variants. Five static modifiers remain in the 32-row inventory. Twenty-eight native effect sheets use steel cuts, shield braces, warm recovery and royal command standards. Six four-frame gestures plus a held release append 25 frames on eleven race atlases: 214 frames per facing and nine rows. All 4,158 original frame crops retain their pixels.

Indomitable follows a real failed Fireball save and removes existing poison/fear. Unbreakable follows an actual lethal hit while an ally keeps the party in battle. Studied Attacks follows a miss, Relentless restores an expended superiority die, and Inspiring Presence grants real temporary HP. Wounded Execution Strike exercises its conditional extra damage. Multiattacks keep every real attack and separate miss/damage rows. Vanguard Charge braces, follows its actual dash, then strikes on arrival. Keen Edge lands on the struck enemy, including self-centered Cleave.

Shield effects use a two-frame solid impact (0.1 s), followed by an open outline so the victim and health bar stay readable during the linger. Physical Fighter effects emit no magical light. The incoming Wizard Fireball retains its fire light. Existing sound mappings remain; previously unmapped physical art reuses existing sword, shield and battle clips. No paid generation or new audio is used.

## Recovery and baseline

The earlier environment outage restored an old snapshot and lost the unpushed Fighter work and captures. This checkout reconstructs the recorded work on latest main `b0cc434e`, with approved Ranger dependency `733e36d343735a959a466c0754369e04fc3729ab`. Frozen before: `4d37f2eab86b19a638c57349745678255325dfe7`. Main now includes #270's missing squirrel art-review policy. Earlier test counts are historical; all final checks and measurements are rerun on this recovered source.

Only six neutral fixture/capture files are backported to the detached baseline. Native captures use the real TV BeatPlayer, Xvfb/software GL, low preset, 1920×1080, fixed 60 simulation / 30 capture fps, Dummy audio. Baseline art, renderer, timing and lighting remain frozen. Videos preserve the complete beat, tails and settle. Open `index.html` locally when assembled.

## Verification and budgets

Final full-suite counts, media and performance results are pending.

Source preservation is in `verification.json`: all gameplay/passive data, original frames, other class art, creatures, phone/fixtures, audio clips, workflow files and non-presentation replay events remain unchanged. Only five Warlord Bridge AbilityUsed presentation objects change. Hardware 16 ms frames and physical TV readability remain hardware checks. CI remains manually disabled.

Fighter stays unchecked pending owner approval. Its PR and Barbarian wait for that approval.
