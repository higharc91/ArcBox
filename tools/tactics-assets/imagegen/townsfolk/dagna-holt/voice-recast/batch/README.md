# Dagna approved veteran recast

66 production line IDs,63 distinct raw WAVs (three captions repeat). Model:
`model_google-gemini-3-8-flash-tts`, voice Gacrux, language en-US, pitch0, no filter.
Each request uses the approved speaker direction plus any authored line direction.
`lines.json` records the plan at submission time; its done flags predate import.
`quote/` contains63 dry-run estimates totaling232 CU. `run/` contains submissions;
`status/` contains63 success receipts totaling232 CU. `download/` records asset
IDs and local source names without temporary signed URLs. `raw/` retains WAVs.
`previous-clips.json` retains old filenames and hashes; those files are preserved.
`clip-validation.json` records decoded output properties and hashes.

Source/cast/line mappings and paid provenance are also in
`tools/tactics-lore/voice-assets.json` under voiceRecasts. The existing two-line
audition cost4 CU separately; cumulative recast work is236 CU,64 paid renders.
No credits purchased. Unlimited member/project caps do not establish plan balance.

To reproduce the targeted import without deleting superseded files:
`python3 tools/tactics-assets/imagegen/townsfolk/dagna-holt/voice-recast/batch/import-recordings.py`
Then run the standard voice checker and focused voice/scene/playback tests.
