"""Store each byte-identical layer sheet once, keeping explicit relative-path aliases."""
import hashlib
from pathlib import Path


def dedupe_pngs(root: Path) -> dict[str, str]:
    aliases = {}
    kept = {}
    # Prefer the original build path; feminine silhouettes retain their distinct body/clothing sheets.
    for path in sorted(root.rglob("*.png"), key=lambda p: ("_f/" in p.relative_to(root).as_posix(), p.relative_to(root).as_posix())):
        relative = path.relative_to(root).as_posix()
        digest = hashlib.sha256(path.read_bytes()).digest()
        if digest in kept:
            aliases[relative] = kept[digest]
            path.unlink()
        else:
            kept[digest] = relative
    return dict(sorted(aliases.items()))
