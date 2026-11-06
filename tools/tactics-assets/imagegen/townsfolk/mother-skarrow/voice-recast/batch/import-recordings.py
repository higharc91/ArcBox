"""Reproduce this approved import, retaining superseded recordings and unrelated mappings."""
import hashlib
import importlib.util
import json
from pathlib import Path


def main():
    batch = Path(__file__).resolve().parent
    root = next(p for p in batch.parents if (p / "tools/tactics-lore/voices.py").is_file())
    spec = importlib.util.spec_from_file_location("voices", root / "tools/tactics-lore/voices.py")
    voices = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(voices)
    rows = json.loads((batch / "lines.json").read_text())
    current = {j["id"]: j for j in voices.jobs()}
    records = {r["raw"]: r for r in json.loads((root / "tools/tactics-lore/voice-assets.json").read_text())["voiceRecasts"]}
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    previous = json.loads((batch / "previous-clips.json").read_text())
    # Preflight the entire import before changing files or the active manifest.
    for j in rows:
        for key in ("line", "cast", "preset", "pitch", "filter", "text", "renderText", "raw", "file"):
            assert j[key] == current[j["id"]][key], f"stale plan: {j['id']} {key}"
        assert sha(batch / "raw" / (j["raw"] + ".wav")) == records[j["raw"]]["sourceSha256"]
    for old in previous:
        assert sha(voices.AUDIO / old["file"]) == old["sha256"]
    before = json.loads((voices.AUDIO / "manifest.json").read_text())["lines"]
    for j in rows:
        voices.encode(batch / "raw" / (j["raw"] + ".wav"), voices.AUDIO / j["file"], j["pitch"], j["filter"])
    after = voices.write_manifest()["lines"]
    ids = {j["line"] for j in rows}
    assert before.keys() == after.keys()
    assert {k: v for k, v in before.items() if k not in ids} == {k: v for k, v in after.items() if k not in ids}
    for old in previous:
        assert sha(voices.AUDIO / old["file"]) == old["sha256"]
    print(f"Imported {len(rows)} approved lines; previous clips and unrelated mappings preserved.")


if __name__ == "__main__":
    main()
