# Old Maud approved ancient-voice recast

24 selected line IDs/takes, all installed. The exact approved town1 audition is
reused; reuse.json points to the earlier5CU receipt, with no new reuse charge.
The initial23 jobs cost125CU. Repairs cost20CU; total speech150CU including
original audition. Transcript diagnosis/full-set/confirmation/replacement checks
cost124CU. Total274CU; this continuation269CU. No purchases or upgrades.
Deduplicated receipts/accounting: cost-summary.json.

lines.json identifies selected requests/takes; done flags predate import.
quote/run/status/raw/download hold selected receipts and retained original takes.
repairs/ and superseded/ retain every rejected take and repair receipt; six rejected
speech jobs are listed separately in global voiceRecastSuperseded provenance.
No rejected take is selected in lines.json. Transcripts are retained in transcripts/,
transcripts-confirmation/ and transcripts-replacements/; diagnostics/ contains the
instruction-narration diagnosis. Signed download/SRT URLs are not retained.
previous-clips.json records the24 original production hashes, all preserved.

ApprovedRenderText in voice-casting.json preserves the exact approved town
request and four shorter repair cues while caption, preset, character direction
and authored line delivery match. Changes invalidate reuse. All use Gacrux and
an ancient105-year-old papery/frail/lucid performance brief, no pitch/filter.

Offline commands from repo root, with no service calls/spend:

    python3 tools/tactics-assets/imagegen/townsfolk/old-maud/voice-recast/batch/import-recordings.py
    python3 tools/tactics-assets/imagegen/townsfolk/old-maud/voice-recast/batch/validate-recordings.py

Source/cast/caption mappings are preflighted before import writes. Source hashes
and global paid provenance live in tools/tactics-lore/voice-assets.json.
validation.json accepts only normalised caption words plus documented leading
ah/oh interjections, with neighbour spelling/punctuation differences. It also
checks source/encoded decoding, bounds, clipping, manifest mapping, exact approved
source reuse and preservation of old clips. ASR does not judge acting quality.
