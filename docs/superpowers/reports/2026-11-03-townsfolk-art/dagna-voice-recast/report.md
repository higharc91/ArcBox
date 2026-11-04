# Dagna Holt — approved veteran voice installed

The owner approved the Gacrux audition: “That's perfect, go ahead and fix her other
voice lines with this same sound”. All66 current Dagna line IDs now resolve to
new recordings using Gacrux, zero pitch shift and no filter. The approved mature,
low, flat Northern captain direction applies to every line, with authored local
direction retained. Captions and line IDs are unchanged. Three repeated captions
share raw audio, so the batch uses63 paid renders.

The casting now stores the approved direction. Planner identities include it so
future changes cannot silently reuse older performances; unmodified speakers keep
their previous hashes. Registered Dagna theme metadata also names Gacrux. Tactics
continues to use authored recordings only. The grim.high player cast is unchanged.

## Cost and retained provenance

63 dry-run estimates: **232 CU**;63 successful job receipts: **232 CU**. The member
ledger increased5314 →5546, confirming232 CU. The previously approved audition
cost4 CU separately: cumulative236 CU across64 paid jobs. No credits purchased.
Member/project caps were unlimited; the tools did not expose plan balance.

All63 source WAVs, exact requests, quotations, job/asset IDs, caption/cast/file
mappings and previous filenames/hashes are committed under
`tools/tactics-assets/imagegen/townsfolk/dagna-holt/voice-recast/batch/`. Paid
provenance is appended to `tools/tactics-lore/voice-assets.json`; previous records
are unchanged. All66 superseded production Ogg files remain intact. The rejected
preview and JSON are archived under `voice-recast/rejected-callirrhoe-*`. The
normal `voice-sample.mp3` now plays the new production town bark.

## Validation

- Full voice inventory:1443/1443 clips present;0 renders outstanding.
- Standard full voice checker:0 clip/speaker flags. Dagna median131.69 Hz;
  approved audition136.17 Hz (−0.58 semitones). This is a coarse register check,
  not a claim of human listening/transcript review of every new line.
- All66 new Ogg clips decode as mono24 kHz; durations0.527–14.0 seconds.
  No source WAV sample clipping. Per-file checks/hashes are in batch/clip-validation.json.
- Targeted importer rerun reproduces all66 encoded hashes exactly, preserving
  all66 previous file hashes and every unrelated active manifest mapping.
- 20 Python lore/planner tests pass, including3 recast/cache identity regressions.
- 71 Tactics Logic voice/scene/load tests pass.
- 52 Presentation story/bark playback tests pass, including recordings-only policy.
- Debug logic test-project build:0 warnings,0 errors. `git diff --check` passes.

Use the direct xUnit runner for these projects:

```sh
source /workspace/.arcbox/activate.sh
python3 -m unittest discover -s tools/tactics-lore -p 'test_*.py' -q
python3 tools/tactics-lore/voices.py status
python3 tools/tactics-lore/voices.py check /tmp/voice-check.json
dotnet run --no-build --project games/Tactics/Tactics.Logic.Tests/Tactics.Logic.Tests.csproj -- -class '*VoiceTests' -class '*BaseSceneTests' -class '*SceneLoadTests' -noColor -noLogo
dotnet run --no-build --project games/Tactics/Tactics.Presentation.Tests/Tactics.Presentation.Tests.csproj -- -class '*StoryTrackTests' -class '*BarkTrackTests' -noColor -noLogo
```

The batch's import-recordings.py reproduces the targeted import without deleting
superseded files and preflights source hashes and current casting first.

## Scope and next gate

No rules, balance, phone contracts, caption text or production textures changed.
No fresh TV/GPU/memory claim: this is a voice change. The last recorded four-party
texture peak remains322.73 MiB Ashen /192.37 MiB Cinder, below512. Dagna's artwork
appearance and full-set/TV gates remain pending; voice approval does not approve
those. Continue on townsfolk-art, rebase before push, no PR or merge.
