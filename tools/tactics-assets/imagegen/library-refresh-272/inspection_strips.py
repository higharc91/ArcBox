"""Lossless review encoding; runtime atlases and creative sources stay unchanged.

Separate facings let a phone decode one compass view instead of eight. Every
WebP strip is checked against the original RGBA pixels before its receipt is saved.
"""
from pathlib import Path
import hashlib
import json
import sys
from PIL import Image


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main(root):
    root = Path(root).resolve()
    folder = root / 'inspection-strips'
    folder.mkdir(exist_ok=True)
    receipt = root / 'inspection-strips.json'
    previous = json.loads(receipt.read_text()) if receipt.exists() else {}
    result = {}
    for mob in json.loads((root / 'packing.json').read_text()):
        path = root / 'export' / mob['id'] / 'atlas.png'
        source_sha = sha(path.read_bytes())
        old = previous.get(mob['id'], {})
        if old.get('sourceAtlasSha256') == source_sha and len(old.get('facings', [])) == 8 and all(
                (root / f['file']).exists() and sha((root / f['file']).read_bytes()) == f['webpSha256']
                for f in old['facings']):
            result[mob['id']] = old
            continue
        image = Image.open(path).convert('RGBA')
        height = mob['size'] * mob['rowsPerDirection']
        facings = []
        for row in range(8):
            strip = image.crop((0, row * height, image.width, (row + 1) * height))
            rgba_sha = sha(strip.tobytes())
            target = folder / f'{mob["id"]}-{row}.webp'
            strip.save(target, format='WEBP', lossless=True, exact=True, method=4)
            decoded = Image.open(target).convert('RGBA')
            assert decoded.size == strip.size and sha(decoded.tobytes()) == rgba_sha, target
            facings.append({'row': row, 'file': target.relative_to(root).as_posix(),
                            'width': strip.width, 'height': strip.height, 'rgbaSha256': rgba_sha,
                            'webpSha256': sha(target.read_bytes()), 'bytes': target.stat().st_size,
                            'pixelsMatchSourceAtlas': True})
        result[mob['id']] = {'sourceAtlasSha256': source_sha, 'sourceBytes': path.stat().st_size,
                              'facings': facings}
    receipt.write_text(json.dumps(result, indent=2) + '\n')
    before = sum(m['sourceBytes'] for m in result.values())
    after = sum(f['bytes'] for m in result.values() for f in m['facings'])
    print(f'Lossless review strips: {before:,} -> {after:,} bytes; every RGBA pixel preserved.')


if __name__ == '__main__':
    main(sys.argv[1])
