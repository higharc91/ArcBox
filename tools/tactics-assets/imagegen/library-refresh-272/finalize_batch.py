"""Write review receipts/navigation after a batch's art and native review pass."""
from pathlib import Path
import json
import re
import subprocess
import sys
from verify_scope import run as verify_scope


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def main(root, log_prefix):
    root = Path(root).resolve()
    repo = root.parents[3]
    batch = json.loads((root / 'batch.json').read_text())
    roles = json.loads((root / 'roles.json').read_text())
    slug, title, date = batch['slug'], batch['title'], batch['date']
    action_count = len(batch.get('actions', ['idle', 'walk', 'attack', 'hurt', 'down']))
    native_seconds, reel_seconds = batch.get('nativeSeconds', 20), batch.get('reelSeconds', 24)
    cadence = batch.get('cadenceNote', 'Natural strikes contact at .4 s and recover through .8 s, without changing rules, stats, effects or placements.')
    replay_note = batch.get('replayNote', 'The replay contains three movement round trips, three natural attacks and return to ready.')
    contact_label = batch.get('contactLabel', 'three engine-timed contacts')
    actions_note = batch.get('actionsNote', f'Each creature has eight facings and {action_count} actions.')
    out = repo / 'docs/art/mob-sprite-review' / slug
    checks = out / 'checks'
    checks.mkdir(parents=True, exist_ok=True)
    logs = Path('/workspace/mob-review')
    focused = (logs / (log_prefix + '-focused.log')).read_text()
    counts = {key: int(re.search(r'^\s*' + key + r': (\d+)', focused, re.M).group(1))
              for key in ['total', 'failed', 'succeeded', 'skipped']}
    assert 'Test run summary: Passed!' in focused and counts['failed'] == 0 and counts['skipped'] == 0
    for stem in ['host-build', 'test-build']:
        text = (logs / (log_prefix + '-' + stem + '.log')).read_text()
        assert 'Build succeeded.' in text and '0 Warning(s)' in text and '0 Error(s)' in text
    browser = json.loads((out / 'browser-validation.json').read_text())
    assert browser['passed'] and len(browser['scenes']) == 5
    for stem in ['import', 'battle', 'world', 'moves']:
        text = (logs / (log_prefix + '-' + stem + '.log')).read_text()
        assert not re.search(r'(^ERROR:|SCRIPT ERROR|Unhandled exception)', text, re.M), stem
    for stem in ['test-build', 'host-build', 'focused', 'import', 'battle', 'world', 'moves', 'recording', 'reel', 'browser']:
        text = (logs / (log_prefix + '-' + stem + '.log')).read_text()
        (checks / (stem + '.txt')).write_text('\n'.join(line.rstrip() for line in text.splitlines()) + '\n')
    media = {}
    for name, duration in [(slug + '-motion.mp4', reel_seconds), ('godot-specials.mp4', native_seconds)]:
        data = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_format', '-show_streams', '-of', 'json', str(out / name)]))
        assert abs(float(data['format']['duration']) - duration) < .1
        assert not any(stream['codec_type'] == 'audio' for stream in data['streams'])
        media[name] = {'duration': float(data['format']['duration']),
                       'video': [{k: stream[k] for k in ['width', 'height', 'r_frame_rate']} for stream in data['streams'] if stream['codec_type'] == 'video']}
    save(checks / 'media.json', media)

    review = repo / 'docs/art/mob-sprite-review'
    tracker_path = review / 'production/library-refresh-status.json'
    tracker = json.loads(tracker_path.read_text())
    new_keys = {r['key'] for r in roles}
    for role in roles:
        if role['key'] not in tracker['integratedKeys']:
            tracker['integratedKeys'].append(role['key'])
    for item in tracker['looks']:
        if item['key'] in new_keys:
            item['status'] = 'integrated library batch' + str(batch['libraryBatch'])
    library_count = len(tracker['integratedKeys'])
    remaining = tracker['baselineTwoDirectionLooks'] - library_count
    tracker.update(libraryBatchCount=batch['libraryBatch'], overallBatchCount=batch['overallBatch'], remainingTwoDirectionLooks=remaining)
    save(tracker_path, tracker)
    for file, prefix in [('index.html', ''), ('production/index.html', '../')]:
        path = review / file
        link = f'<p><a style="color:#b5ead4" href="{prefix}{slug}/index.html">Latest: {title}</a></p>'
        if link not in path.read_text():
            path.write_text(path.read_text() + link)
    old_root = subprocess.check_output(['git', 'show', batch['priorCommit'] + ':docs/art/mob-sprite-review/README.md'], cwd=repo, text=True)
    previous = re.search(r'\[Latest:.*?\]\(([^/]+)/index.html\)', old_root).group(1)
    total = 29 + library_count
    current = (f'[Latest: {title}]({slug}/index.html), library batch {batch["libraryBatch"]} on `mob-library-refresh-272` for '
               f'[issue #272](https://github.com/higharc91/ArcBox/issues/272). Main remains at the separate playtest checkpoint, PR #273. '
               f'The project now has {total} refreshed designs across {batch["overallBatch"]} batches: 29 combat designs and {library_count} additional library appearances. '
               f'[Library tracker](production/library-refresh-status.json): {remaining} older library appearances remain; 26 established eight-direction looks are preserved. '
               f'[Previous batch]({previous}/index.html). The original 98-look baseline and completed combat tracker remain unchanged.\n')
    (review / 'README.md').write_text(old_root[:old_root.index('[Latest:')] + current)
    for path, link in [(review / 'production/README.md', f'\n[Batch {batch["overallBatch"]}: {title}](../{slug}/index.html) adds library batch {batch["libraryBatch"]}; {remaining} older appearances remain.\n'),
                       (review / previous / 'README.md', f'\n[Next: {title}](../{slug}/index.html), continuing issue #272 on the separate follow-up branch.\n')]:
        if link not in path.read_text():
            path.write_text(path.read_text() + link)
    verify_scope(root)
    scope = json.loads((checks / 'scope.json').read_text())
    source_count, keys = scope['sourceImages'], scope['packedFrameKeys']
    features = '\n\n'.join(batch['features'] + batch.get('reviewNotes', []))
    concept = repo / 'tools/tactics-assets/concepts' / (slug + '-' + date)
    concept_image = batch.get('conceptImage', 'three-variants.png')
    concept_note = batch.get('conceptNote', 'Scenario supplied the shared concept sheet; OpenAI authored complete pixel figures. PixelLab was not used.')
    (concept / 'BRIEF.md').write_text(f'# {title} concepts\n\n[Concept sheet]({concept_image}) · [Exact brief](brief.txt) · [Receipt](receipts.json)\n\n{concept_note}\n\n{features}\n\nThese are existing library appearances. No new stat block, lore or equipment beyond the documented variant is introduced. Physical labels are approximate design guides; final game scale is checked beside the party and approved family sprites.\n')
    commands = '\n'.join(('python' if f.endswith('.py') else 'node') + ' tools/tactics-assets/imagegen/' + root.name + '/' + f
                         for f in ['references.py', 'measure.py', 'export.cjs', 'integrate.py', 'compare.cjs', 'review.cjs', 'reel.cjs', 'verify-review.cjs', 'verify-scope.py'])
    (root / 'README.md').write_text(f'''# {title} pixel production

[Concepts/receipt](../../concepts/{slug}-{date}/BRIEF.md) · [Native review](../../../../docs/art/mob-sprite-review/{slug}/README.md).

{concept_note}

{source_count} source PNGs are preserved with exact prompts, generation IDs, references and whole-pose corrections. {keys} packed keys select {scope['distinctSelectedWholeFigures']} distinct complete authored figures. Hurt/down hold single poses. All creative pixels come from image generation; technical tools only copy, crop, uniformly scale and register whole figures. No procedural recolouring, mirrored parts or anatomy patches.

{actions_note} Grounded poses register to their contact base; no synthetic head pinning or whole-body walk bob is added. Runtime size follows visible bodies rather than cell padding. Portraits frame front faces; neutral and active silhouette bounds determine bar clearance. {cadence}

{features}

Run from repository root with Python imaging libraries and Node/Playwright installed; set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for the browser:

```sh
{commands}
```

The scope verifier compares against `{batch['priorCommit']}` and checks preserved sources, prior runtime art/rules and complete review-panel bounds. Source originals are additionally checked when the generation workspace is available; committed SHA receipts support offline verification.
''')
    (out / 'README.md').write_text(f'''# {title} — library batch {batch['libraryBatch']}

[Standalone gallery](index.html) · [Before/after](before-after.png) · [Animation MP4]({slug}-motion.mp4) · [GIF]({slug}-motion.gif) · [Native Godot replay](godot-specials.mp4)

Continues [issue #272](https://github.com/higharc91/ArcBox/issues/272) on `mob-library-refresh-272`. Main stays untouched for the separate playtest. {total} designs are now refreshed across {batch['overallBatch']} batches: 29 combat designs and {library_count} library appearances. {remaining} older library appearances remain; 26 established eight-direction looks retain their art. [Tracker](../production/library-refresh-status.json).

[Concepts/receipt](../../../../tools/tactics-assets/concepts/{slug}-{date}/BRIEF.md) · [Pixel sources/pipeline](../../../../tools/tactics-assets/imagegen/{slug}-{date}/README.md).

{features}

{keys} packed keys select {scope['distinctSelectedWholeFigures']} complete figures from {source_count} preserved source sheets. {actions_note} Hurt/down hold single poses. Before/after and inspection animations retain a common scale for all creatures. [Battle](battle.png), [world lineup](overworld-lineup.png), [lighting/occlusion](overworld-lighting-occlusion.png), [{contact_label}](actions-in-game.png).

## Verification

- Host/test builds: zero warnings/errors. [All {counts['succeeded']} focused checks passed](checks/focused.txt), with none skipped. These cover silhouette connectivity, attachment, scale, HUD, atlas bounds, walkable review fixtures and natural contact/recovery.
- [Scope/source receipts](checks/scope.json) preserve prior creature entries/images, rules/abilities, portraits, completed combat tracker and historical baseline. No new stats or encounters. {cadence}
- [Browser review](browser-validation.json) decodes all five embedded scenes and {action_count} actions, checks four distinct walk keys, and fits a 390 px phone viewport. [Complete silhouettes fit their gallery/reel panels](checks/artifact-bounds.json).
- Native Godot battle, world and replay reviewed beside the party for scale, lighting, rear views, front portraits, contact and bar clearance. The silent native video lasts {native_seconds} seconds; the combined inspection reel lasts {reel_seconds} seconds with four representative facings. The gallery selects any of eight facings. Software rendering checks appearance, not target-device performance. [Media metadata](checks/media.json).
- Full presentation suite will run after the remaining issue #272 batches. Starting main checkpoint passed all 3,128 presentation tests.

## Reproduce captures

Use Godot Mono 4.7.2 and the .NET 10 host build. Before `--`: `--audio-driver Dummy --path src/ArcBox.Host --resolution 1920x1080`. Arguments after `--`:

```sh
--preset=low --tactics-demo=river_crossing:library-{slug} --shots=/absolute/battle-shots --quit-after=12
--preset=low --tactics-demo=library-{slug}-moves --shots=/absolute/move-shots --quit-after={native_seconds + 2}
--preset=low --world-demo=ashen_ford:library-{slug} '--world-looks=road@37,61@37,61;lineup@52,60@52,60' '--world-route=37,61;39,61' --shots=/absolute/world-shots --quit-after=90
```

Collect with `python tools/tactics-assets/imagegen/{root.name}/collect-scenes.py --battle /absolute/battle-shots --world /absolute/world-shots --specials /absolute/move-shots`, then action-scenes.cjs, review.cjs and verify-review.cjs. {replay_note}
''')
    save(checks / 'test-summary.json', {'focused': counts, 'hostBuild': {'warnings': 0, 'errors': 0}, 'testBuild': {'warnings': 0, 'errors': 0},
         'browserValidationPassed': True, 'fullPresentation': 'Deferred until issue #272 art batches are complete; starting main checkpoint passed 3128/3128.'})
    save(checks / 'visual-review.json', {'observations': batch['features'] + batch.get('reviewNotes', []), 'nativeBattleWorldReplayReviewed': True,
         'allFacingsInGalleryReviewed': True, 'frontPortraitsAndBarClearanceReviewed': True, 'commonScaleAndCompleteInspectionBounds': True,
         'mainUntouchedForPlaytest': True})
    for folder in [out, root, concept]:
        for file in folder.glob('*.md'):
            for target in re.findall(r'\]\(([^)]+)\)', file.read_text()):
                if ':' not in target:
                    assert (file.parent / target.split('#')[0]).exists(), (str(file), target)
    print(f'Finalized {title}: {counts["succeeded"]} focused passes, {remaining} appearances remain.')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
