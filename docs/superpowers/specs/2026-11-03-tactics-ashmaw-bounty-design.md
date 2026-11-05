# The Ashmaw bounty — issue #74

Status: implementation in progress on `ashmaw-bounty`, based on main after #73 / PR #75. This document distinguishes implemented foundations from planned integration. Both characters and portraits are approved and installed in production.

## Owner decisions and balance defaults

The town board posts Dagna Holt's bounty after either Chapter I ford battle is won. The party enters Cinder Hollow from the burned east bank, fights three encounters and Ashmaw, and brings the warm ember-claw back to Dagna. The boy survived and is hiding at Maud's. The fading sliver crumbles; nothing enters the shard economy. Side content never sets main-story flags or exposes the existing NPC secrets.

USER DECISION defaults from the issue:

- 60 gold, split among present characters; 300 XP and one class-specific rare trinket for every campaign member, including away members.
- One worn trinket, retained through a remake and dormant while the class does not match. Replacing an existing trinket requires confirmation.
- The twelve effects in `content/tactics/base/rules/trinkets.json` match the issue's draft exactly. They use existing static modifiers and battle hooks. Balance remains the owner's call after simulation.
- Begin without cubs. Add them only if the required simulations show they are needed; concepts and generation for cubs then derive from approved Ashmaw.
- Ashmaw and Grisk's combat numbers remain draft until the simulation report. Do not silently declare balance approved.

## Implemented foundation: class trinkets

The rules loader merges and validates `trinkets.json` and includes it in the rules hash. `CharacterRecord.Trinket` is optional and omitted from legacy JSON when absent. Unknown IDs are content errors. A matching class contributes a synthetic self passive through the existing modifier and hook engine; maximum HP is applied exactly once during sheet construction. A class mismatch preserves the item without applying its effects.

Campaign remakes preserve the server's worn slot and ignore a client-supplied replacement. The phone character sheet displays the name, rarity, active/dormant state and existing details sheet. A remake preview explains when changing class leaves the keepsake dormant. Reading the details remains available while a session holds the campaign lease.

## Quest plan and implemented travel foundation

Reuse #73's quest flags, journal, pin, conversations and atomic reward ledger. The stages are posted, accepted, entered, lair, slain and done. Reading ordinary notices before the ford is settled must not start the bounty. Only completion of a won ford battle sets `ch1_ford_settled`; losing, fleeing and selecting a dialogue branch do not.

The entrance runtime, named-spawn authoring and actual Hollow/Ford entrances are implemented. An entrance is a passage interactable with `to: { region, spawn }`. Optional named region spawns preserve existing region JSON. The leader proposes travel and the party votes using the existing rest/story travel pattern. World region and party location save together. Travel tests cover restart, ending the session, interrupted saved votes and returning. The loader validates destination regions, named spawns, walkability and a reachable return exit. A live six-player session exercised both real region transfers.

The Hollow is approximately 56×44 tiles with four forced 14×12 battle stamps, in order: Throat, Diggings, Warm Nest and Ashmaw's Bed. Recovery is at the entrance; a campfire is between the Nest and the boss. Use existing Ashen Ford cave/rock/ash materials. Environmental tiles are enabled for the Nest and lair. The cave scene must export byte-identically twice.

The owner's [issue follow-up](https://github.com/higharc91/ArcBox/issues/74#issuecomment-6608923692) reserves two small arenas for future #79 wandering mobs: one between the Throat and Diggings and one between the Diggings and Nest. Keep the Nest-to-campfire route free of wandering arenas. Leave the burned-bank approach available for #81's future claw-mark notice. For #77 compatibility, arrival changes the region and epoch and sends the destination's complete current things; no origin-region cache survives the switch.

The current engine occupies one tile per unit. Ashmaw therefore uses a 1×1 tactical occupancy with a large visual sprite; no 2×2 movement rules are introduced. Its reared pose must be checked beside a party member for the approximate two-to-one height target.

## Art and approval gates

Scenario generated Ashmaw, Grisk and the environment reference for **39 CU**. Both character concepts were explicitly approved on 2026-11-03: “ok, both look good now for concept art.” See [concepts and receipts](../../../tools/tactics-assets/concepts/ashmaw/README.md).

The owner also approved the single-facing pixel direction: “Yes, keep this direction.” PixelLab uses the approved designs, existing Wardenflame style reference and Ashen Ford palette. Single-facing idle/walk pilots precede rotations and full animation sets. A south input must face straight toward the viewer; a three-quarter pose must be corrected before rotation. Check planted feet, handedness and pick-to-hand contact, loop seams and physical scale alongside the party.

The owner must see and approve actual world and battle motion before final character art replaces stand-ins. Every art review question displays the corresponding art. World/battle motion was approved for both characters on 2026-11-03 (“Approve both”), and their dedicated atlases and portraits are installed. The owner subsequently authorized cave environmental art: animal bones, dried blood and scattered practical candles, plus darker lighting and fitting music. Other poster, trinket-icon and terrain requests remain prompts and procedural placeholders.

## Remaining acceptance work

Quest/travel integration, the four forced chambers, class-bound trinket rewards, dialogue coverage, TV reward presentation and the six-player bounty playthrough are implemented. The live playthrough completed Chapter I, accepted the bounty, entered the Hollow, won all four chambers, rested, collected proof, returned and received 60 gold plus private class trinkets. The current campaign reaches this content at level 1; four-player attempts struggled. Do not call the balance accepted.

The 5,520-battle level 1–3 simulation is complete. At level 2, four-player wins were Throat 99.2%, Diggings 96.7%, Nest 78.3% and Ashmaw 100%. Ashmaw is below the desired challenge, despite using its attacks correctly. Solo level-2 Story Ashmaw won 85/100 sampled runs; the solo sampler covers five healer/defender builds. Numbers remain owner decisions under issue §7. Paired trinket comparisons are complete; overall wins rose 84.3% → 86.3%, with level-2 Forest Ruins 37.7% → 45.6%.

The owner confirmed the original level-2 target. The journal states that recommendation without changing post-ford availability or global XP. Cave dressing, repeatable export, all voices/SFX and paired trinket comparisons are complete. Production art passed 287 presentation and 71 content checks. The full production regression passed 6,191 tests. The owner subsequently requested about 60% four-player wins and clarified that only 4+ should become harder. The final party-specific profile yields 59.4% at level 2, 82.7% at level 3 and 97.5% at level 4. Smaller-party replay results are unchanged. Final verification passed: 6,191 .NET tests, 508 web tests, six phone viewports and a zero-warning solution/editor build. The bounty is ready for review on ashmaw-bounty; no PR until requested.
