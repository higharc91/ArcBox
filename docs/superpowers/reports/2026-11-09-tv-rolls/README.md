# TV attack/save readouts (#164)

Each ability and resolved attack/save overlays the existing beat with its actor, authored ability name, target, kept die(s), modifier, total and AC/DC. The panel states HIT/MISS/CRIT or SAVED/FAILED in text. Pushes and applied/ended conditions name themselves; generated stat-mod IDs resolve to human names. Multiple rolls from an area remain separate rows. No new hold time or motion is added; reduced-effects behavior is retained.

Attack rolls are remembered across a player reaction in a later beat. `AttackResolved.finalAc` is emitted only when a reaction changes the original target number, so Shield can explain a MISS against the final AC. Save events carry their original caster/ability context. Added fields are optional for existing event fixtures. Replay review strips only those two new metadata fields and proves the three changed golden event files otherwise identical; command files and outcomes are unchanged.

Focused validation: six readout cases, 59 playback cases, 28 reaction cases, seven golden cases and the original three real-battle timing cases pass. Fresh native Godot Debug build has zero warnings. The `--tactics-demo=rolls` fixtures render through ordinary playback at 1920×1080, including a miss, save plus push/condition tags, and a reaction-raised AC. These are deterministic UI fixtures (the combined effect sample is synthetic), not a live session or human/physical-TV test. Final native run has no resource/script errors; software OpenGL only reports its known VSync/AA limitations.

`shield-deuteranopia.png` and `shield-protanopia.png` are actual Chromium vision-deficiency simulations of the native screenshot, reviewed for the explicit numeric and outcome labels. Typography is 28 px headings, 30 px rolls and 26 px effects. The final panel is compact and stays clear of the corner cards.

Reproduce: build `src/ArcBox.Host` in Debug, import its Godot resources, then run `godot --path src/ArcBox.Host -- --tactics-demo=rolls --shots=<folder> --res=1920x1080`. Automated moment shots capture the roll and push tags without extending combat timing. Full repository validation is required before merge.
