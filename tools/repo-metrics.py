#!/usr/bin/env python3
"""Report current tracked pack/source sizes and optional executed test counts as JSON."""
from __future__ import annotations
import argparse
import gzip
import json
from pathlib import Path
import re
import subprocess


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checks-log", type=Path, help="a successful tools/check-all.sh --full log")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    tracked = subprocess.check_output(["git", "-C", str(root), "ls-files", "-z"], text=True).split("\0")
    pack = [root / name for name in tracked if name.startswith("content/tactics/base/") and (root / name).is_file()]
    text = [p for p in (root / "web/play/dist").rglob("*") if p.is_file() and p.suffix in {".html", ".js", ".css"}]
    result = {
        "scope": "tracked base content; built phone text includes all entries, not a first-load estimate",
        "base_content_files": len(pack),
        "base_content_bytes": sum(p.stat().st_size for p in pack),
        "base_content_png_files": sum(p.suffix == ".png" for p in pack),
        "base_content_json_files": sum(p.suffix == ".json" for p in pack),
        "csharp_test_source_files": sum(name.endswith(".cs") and ".Tests/" in name for name in tracked),
        "phone_test_source_files": sum(name.startswith("web/play/test/") and bool(re.search(r"\.test\.tsx?$", name)) for name in tracked),
        "phone_build_present": bool(text),
        "built_phone_text_files": len(text),
        "built_phone_text_bytes": sum(p.stat().st_size for p in text) if text else None,
        "built_phone_text_gzip_bytes": sum(len(gzip.compress(p.read_bytes(), mtime=0)) for p in text) if text else None,
    }
    if args.checks_log:
        log = args.checks_log.read_text(encoding="utf-8")
        summary = log.rsplit("== Summary (--full) ==", 1)
        if len(summary) != 2 or "FAIL" in summary[1] or summary[1].count("PASS") < 5:
            parser.error("--checks-log must contain a complete successful --full summary")
        def last(pattern: str) -> int:
            matches = re.findall(pattern, log)
            if not matches:
                parser.error(f"test count missing for {pattern}")
            return int(matches[-1])
        python_counts = re.findall(r"Ran (\d+) tests? in", log)
        if len(python_counts) != 3:
            parser.error("expected asset, audio and lore unittest summaries")
        result["executed_tests_from_log"] = {
            "dotnet": last(r"total:\s+(\d+)"),
            "phone": last(r"Tests\s+(\d+)\s+passed"),
            "node_tools": last(r"ℹ tests (\d+)"),
            **dict(zip(("assets", "audio", "lore"), map(int, python_counts))),
        }
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
