# Asset storage cleanup (#191)

Production monster rules and Grisk's lore definition now point directly to the accepted creature atlases. The stand-in manifest and its folder are removed, as is the loader for that table. Accepted creature atlases retain compatibility keys for saved battles; those keys resolve to real art, never a bear/kobold stand-in.

165 byte-identical character-layer pairs now use one file and an explicit one-hop manifest alias. The shared hair, beard, marks, headgear, horns, tails, float and identical cape layers retain exactly their prior bytes; distinct feminine silhouettes and clothing remain separate. 773 PNG files remain with no duplicate hashes. The phone composer resolves aliases before fetching the sheet. The character build emits the same alias contract and deletes duplicate outputs.

Validation: isolated character asset build completes, 1 dedupe test and 9 manifest cross-check tests pass, 6 phone look tests pass, 5 native art-resolution tests pass, and the .NET build has zero warnings and the phone production build completes with existing upstream SignalR annotation warnings. The isolated build reproduces all retained layer bytes; it also exposes two existing generator/content differences (`subclasses.png`, `presets/innkeeper.png`). Those generated replacements were not applied, preserving the accepted art.

This branch also supplies explicit existing-icon mappings for #127's new monster actions; no icon atlas pixels are changed. Paired #127 simulation reports are attached: 1,200 fights before and 1,200 after, default four heroes, levels 1/2, seeds 1–100, six Ford/Hollow encounters. Aggregate win-rate rows are identical. This is simulation evidence, not a human playtest.

The global full-check failures recorded under the REDLINE pacing report still block merging; final full-stack verification remains required.
