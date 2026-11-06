"""Verify the approved townsfolk art and the main-line content at the #80 merge."""
import hashlib
import json
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[5]
ours = '8656e414ad3a7a89d8df942121b2f7640ceb9f0a'
main = '00490381ba6d66a87f297ebd68163cfb566a8581'

def git(*args, data=None):
    return subprocess.check_output(['git', *args], cwd=root, text=True, input=data)

def tree(ref):
    lines = git('ls-tree', '-r', ref, 'content/tactics/base').splitlines()
    return {line.split('\t', 1)[1]: line.split()[2] for line in lines}

base = git('merge-base', ours, main).strip()
left, right, ancestor = tree(ours), tree(main), tree(base)
paths, expected = [], []
for file in sorted(set(left) | set(right)):
    if not file.endswith(('.png', '.ogg', '.wav', '.webp')):
        continue
    a, b, c = left.get(file), right.get(file), ancestor.get(file)
    if a == b:
        want = a
    elif a == c:
        want = b
    elif b == c:
        want = a
    elif a is None:
        want = b
    elif b is None:
        want = a
    else:
        raise AssertionError('Both parents changed binary art/audio: ' + file)
    if want:
        paths.append(file)
        expected.append(want)
assert git('hash-object', '--stdin-paths', data='\n'.join(paths) + '\n').splitlines() == expected

file = 'content/tactics/base/rules/world/ashen_ford.json'
merged, original = json.loads((root / file).read_text()), json.loads(git('show', main + ':' + file))
for region in (merged, original):
    for person in region['people']:
        person.pop('variant', None)
assert merged == original, 'Ashen Ford gameplay differs from current main beyond cosmetic person variants'

folder = Path(__file__).resolve().parent
approval = json.loads((folder / 'approval.json').read_text())
assert approval['status'] == 'approved' and approval['productionInstalled']
for file, digest in approval['sha256'].items():
    assert hashlib.sha256((folder / file).read_bytes()).hexdigest() == digest, file
record = json.loads((folder / 'review-pack.json').read_text())
for file, digest in record['assetHashes'].items():
    assert hashlib.sha256((root / 'content/tactics/base' / file).read_bytes()).hexdigest() == digest, file

print(json.dumps({'result': 'PASS', 'binaryContentFilesPreserved': len(paths), 'mainParent': main,
                 'townsfolkParent': ours, 'mergeBase': base, 'ashenFordChangesBeyondCosmeticVariants': 0,
                 'approvedReviewHashesVerified': len(approval['sha256']), 'approvedInstalledArtFiles': len(record['assetHashes'])}, indent=2))
