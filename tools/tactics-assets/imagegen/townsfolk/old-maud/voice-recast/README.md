# Old Maud approved ancient-voice recast

Owner approval: “perfect, let's go with that”. All24 Maud lines now use Gacrux
at pitch0 without a filter. The approved town audition is reused byte-for-byte;
23 selected new renders cover the rest. Captions and recordings-only playback
remain unchanged. Artwork appearance was subsequently approved; motions/portraits are queued and
finished-set/TV approval remains separate.

Hear ../voice-sample.mp3 or approved-voice-reel.mp3 (three installed clips).
The original audition/source/request/5CU receipt remain here. Rejected Achernar
sample archived as rejected-achernar-voice-sample.*. All old24 Ogg files remain.

batch/ contains live quotes/cap checks, selected WAVs/receipts, exact planned
requests, six rejected takes with provenance, paid transcript checks and validation.
Long instructions were spoken in three takes; three other takes were replaced
for word substitutions reported by two ASR models. Short vocal cues repaired them.
No rejected take is active. All selected words match after punctuation/spelling
normalisation and explicitly listed leading ah/oh interjections. ASR cannot judge
acting quality. The owner approved the audition's performance.

Total274CU:150 speech including previous5CU audition;124 transcription.
Continuation269CU. Final consumption5820 vs5551 before; no purchases/upgrades.
Member/project have no cap; this is not a remaining-credit balance.

Offline commands from repo root (no service calls or spend):

    python3 tools/tactics-assets/imagegen/townsfolk/old-maud/voice-recast/batch/import-recordings.py
    python3 tools/tactics-assets/imagegen/townsfolk/old-maud/voice-recast/batch/validate-recordings.py

23 Python tooling tests,71 logic and52 story/bark presentation tests pass.
See docs/superpowers/reports/2026-11-03-townsfolk-art/maud-voice-recast/report.md.
