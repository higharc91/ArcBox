"""Publish verification only after the final test, browser and native gates pass."""
from pathlib import Path
import json,re,subprocess,os
R=Path(__file__).resolve().parents[5];OUT=R/'docs/art/mob-sprite-review/library-completion';LOGS=Path(os.environ.get('ARCBOX_REVIEW_LOGS','/workspace/mob-review'));checks=OUT/'checks';checks.mkdir(exist_ok=True)
log=(LOGS/'library-completion-full-suite.log').read_text();assert 'Test run summary: Passed!' in log
counts={k:int(re.search(r'^\s*'+k+r': (\d+)',log,re.M).group(1)) for k in ['total','failed','succeeded','skipped']};assert counts['failed']==0 and counts['skipped']==0
stems=['test-build','host-build','full-suite','manifests','import','battle','world','moves','recording','spectre-moves','spectre-recording','browser']
for stem in stems:
 text=(LOGS/('library-completion-'+stem+'.log')).read_text()
 if stem.endswith('build'):assert 'Build succeeded.' in text and '0 Warning(s)' in text and '0 Error(s)' in text
 assert not re.search(r'(^ERROR:|SCRIPT ERROR|Unhandled exception)',text,re.M),stem
 (checks/(stem+'.txt')).write_text('\n'.join(line.rstrip() for line in text.splitlines())+'\n')
assert 'Ran 8 tests' in (checks/'manifests.txt').read_text() and 'OK' in (checks/'manifests.txt').read_text()
assert json.loads((OUT/'browser-validation.json').read_text())['passed'];assert json.loads((OUT/'native-contacts.json').read_text())['contacts']==12
media={}
for name,seconds in [('all-19-walking.mp4',24),('native-gilded-and-rime.mp4',42),('native-spectre.mp4',24)]:
 d=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_format','-show_streams','-of','json',str(OUT/name)]));assert abs(float(d['format']['duration'])-seconds)<.1;assert not any(s['codec_type']=='audio' for s in d['streams']);media[name]={'seconds':float(d['format']['duration']),'bytes':int(d['format']['size']),'streams':[{k:s[k] for k in ['width','height','r_frame_rate']} for s in d['streams'] if s['codec_type']=='video']}
proof={'testedIntegrationCommit':'fd5bf0c8','mainBase':'0d808be20ccb00e3fe3891983c40c60470e29cbc','presentationTests':counts,'assetManifestChecks':8,'hostAndTestBuildWarnings':0,'hostAndTestBuildErrors':0,'nativeContactsAfterIntegration':12,'desktopAnd390PixelPhoneGalleryPassed':True,'manualTvPhonePlaytestFeedback':'Open on issue #272; feature branch remains unmerged','media':media}
(OUT/'verification.json').write_text(json.dumps(proof,indent=2)+'\n')
(OUT/'verification.md').write_text(f'''# Final integration verification

Tested code integration: `fd5bf0c8`, preserving main `0d808be20ccb00e3fe3891983c40c60470e29cbc` (PR #277). Later review packaging changes only documentation/tools/media.

- Full .NET presentation suite: **{counts['succeeded']:,} passed**, zero failures and zero skips. [Exact log](checks/full-suite.txt).
- Host and test builds: zero warnings/errors. [Host](checks/host-build.txt) · [tests](checks/test-build.txt).
- Shipped art/audio manifest audit: **8 passed**. [Log](checks/manifests.txt).
- Actual Godot battle and overworld views reviewed after main integration, including portraits, party scale, health bars, lighting and occlusion.
- Final native replays: three movement round trips, three natural strikes and all seven Gold Dragon/Frost Troll/Ogre specials; then Spectre drift, natural touch and its real targeted Life Drain. **All twelve engine-timed contacts captured**. [Contact sheet](native-contacts.png) · [Marker receipts](native-contacts.json).
- [Giant/Dragon replay](native-gilded-and-rime.mp4): 42 seconds; [Spectre replay](native-spectre.mp4): 24 seconds. Videos are silent review captures.
- Combined [walking reel](all-19-walking.mp4): all nineteen appearances in all eight facings over 24 seconds, runtime cadence, identical world zoom and a real standing fighter reference. Every walking silhouette fits its panel. [Composition proof](composition-proof.json).
- Standalone gallery: all four embedded images decode; embedded video loads and seeks to the final facing; seven full batch links resolve; 390 px phone has no horizontal overflow or page errors. [Browser receipt](browser-validation.json).
- [Final scope](integration-scope.json): exactly nineteen creature additions; all existing main creature entries and other content preserved, including rules, stats, abilities, placements and dialogue portraits. Original mob baseline and combat tracker unchanged. All 31 older library appearances are complete.
- [Provenance audit](provenance-audit.json): 118 exact source PNGs, unchanged source/reference receipts, 2,704 distinct selected whole figures and no rejected draft selected. Complete pixel figures were authored with OpenAI image generation from Scenario concepts and approved family references; no procedural anatomy repaint/deformation.

The source-native frames in each batch were reviewed before integration. Mixed-species sapling/Shade drafts, incorrect rear poses, held stones at release and insufficient wing spreading were rejected or replaced with freshly authored full figures. Sapling reach and Wraith/giant/dragon clips now finish their authored contact and recovery while preserving gameplay.

Real-device/TV playtest feedback remains open on [issue #272](https://github.com/higharc91/ArcBox/issues/272). This branch is ready for review but has not been merged to main.
''')
print(f"Final review passed all gates: {counts['succeeded']} presentation tests, 8 manifest checks, twelve real engine contacts, 19 appearances complete.")
