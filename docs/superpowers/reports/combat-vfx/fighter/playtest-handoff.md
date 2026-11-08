# Combat VFX playtest handoff

The owner authorized merging the completed work through Fighter to main for tonight’s playtest. Remaining work is tracked in [#274](https://github.com/higharc91/ArcBox/issues/274); the parent [#247](https://github.com/higharc91/ArcBox/issues/247) remains open.

Included: shared cut-ins, camera/impact/accessibility controls and spell lighting; approved Wizard, Sorcerer, Warlock, Cleric, Druid, Paladin, Bard and Ranger work; Fighter’s 20 abilities, seven visible hooks, native poses and 28 effects. Charge follows actual movement before its strike. Shield fills last two frames, then become outlines that keep units and HP bars readable. Gameplay, balance and AI are unchanged by the presentation work. No new sounds or paid generation were added.

Current main’s #273 mob refresh is integrated. All current-main mob sprite records remain identical; the four approved Druid forms are retained alongside them. Natural creature attacks/specials and class/counterspell/volley timings are combined. The initial focused integration checks pass: 186 tests. The complete gate then caught a Ranger companion pacing conflict: the refreshed hound’s full 0.8 s bite plus separate save/condition holds took 0.96 s. Party-hound save/condition updates now resolve during recovery, preserving every animation frame and rule result inside 0.8 s. The targeted companion, Fighter, creature and timing checks pass: 260 tests.

The integration also exposes 97 retained pre-refresh files (96 original portraits and one drake sheet) that the mob refresh leaves shipped after remapping live art. Their bytes match both the historical and current-main originals. They are explicitly recorded as historical source assets, preserving the strict unknown/unreferenced-asset guard; the seven manifest crosschecks pass.

Final full local gate: pending completion before merge.

Fighter’s full final-art gallery, revised three-run performance matrix, texture/pack report, browser review and physical TV/hardware checks are deferred to #274. Existing partial local media and the earlier +4.44% result belong to intermediate art. They are not final review evidence. For the later comparison, use matching contemporary mob assets on both sides and backport only the neutral fixture harness to the Fighter-before tree.

Next implementation: Barbarian, Monk, Rogue, then common actions and racial abilities, with the existing per-class review process. Tonight’s playtest feedback should be recorded in #274 before continuing.

CI is manually disabled and must remain disabled unless the owner explicitly changes that instruction. Existing source/art checkpoints are 58b322c5 and d6915bba; original Fighter-before reference is 4d37f2ea. The original verification.json is explicitly scoped to d6915bba before the main mob integration.
