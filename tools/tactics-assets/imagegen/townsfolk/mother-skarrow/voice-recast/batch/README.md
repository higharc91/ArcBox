# Mother Skarrow approved Kore recast sources

Owner approval: “sounds good, use it”.67 line IDs share64 selected source takes.
All canonical caption words are preserved; exact actual render requests are in
lines.json and guarded approvedRenderText entries in voice-casting.json.
Initial paid submission requests are retained separately. Duplicate identical
Forty-winter captions share one verified number-clarified source.

Keep all paid sources and receipts, including superseded takes. The original
approved audition had an ambiguous opening number; production uses a fresh
numeric40 render with unchanged Forty caption. Instruction narration and actual
word substitutions were rejected; some replacements also needed a new take.
No narration is edited out of a bad take. Superseded-takes.json records exact
job/asset/source hashes and reasons; no superseded source is selected in lines.json.
Unused quote files are not billing receipts and must not be counted as spend.

Local caption QA uses unbiased faster-whisper small.en and medium.en on CPU int8.
Requirements and actual package/model fingerprints are retained; downloaded
weights are workspace tools, not content-pack dependencies or committed assets.
No initialPrompt biases the model toward expected captions. Validator accepts
recorded punctuation/spelling/number/homophone and vocalization differences,
plus one-word ASR tail segments only when physical audio is near-silent and the
segment extends past EOF. Original ASR output is retained; no audio is trimmed.
ASR checks wording, not acting maturity. The owner approved the voice performance.

Offline import/validation from repo root:

    python3 tools/tactics-assets/imagegen/townsfolk/mother-skarrow/voice-recast/batch/import-recordings.py
    python3 tools/tactics-assets/imagegen/townsfolk/mother-skarrow/voice-recast/batch/validate-recordings.py

Import requires current plan/source hashes to match global paid provenance;
old67 clips and all other manifest mappings stay intact. Previous-manifest.json
and previous-clips.json capture the original state. Never use the generic
voices.py import command here: its stale-file pruning would remove retained takes.

To reproduce local ASR, install requirements-qa.txt in an external venv and run
transcribe-local.py with batch (small.en) or confirm medium.en. The confirm mode
reads confirmation-lines.json. Optionally set ARCBOX_VOICE_QA_MODELS to an external
public-model cache. Retained ASR receipts allow validation without model download.

No PixelLab calls, credit purchase, plan upgrade, rules/balance/phone or art changes.
Artwork approvals remain separate from the approved voice.
