"""Offline source/caption/import checks; paid ASR receipts are inputs, not new requests."""
import array
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import wave


def main():
    batch = Path(__file__).resolve().parent
    root = next(p for p in batch.parents if (p / 'tools/tactics-lore/voices.py').is_file())
    spec = importlib.util.spec_from_file_location('voices', root / 'tools/tactics-lore/voices.py')
    voices = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(voices)
    current = {j['id']: j for j in voices.jobs()}
    rows = json.loads((batch / 'lines.json').read_text())
    manifest = json.loads((voices.AUDIO / 'manifest.json').read_text())['lines']
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    words = lambda s: re.findall("[a-z']+", s.lower().replace('’', "'").replace('neighbor', 'neighbour'))
    results = []
    for j in rows:
        for key in ('text', 'renderText', 'raw', 'file', 'preset', 'pitch', 'filter'):
            assert j[key] == current[j['id']][key], (j['line'], key)
        source = batch / 'raw' / (j['raw'] + '.wav')
        with wave.open(str(source)) as wav:
            assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 24000)
            seconds = wav.getnframes() / wav.getframerate()
            samples = array.array('h', wav.readframes(wav.getnframes()))
        assert .3 < seconds < 30, (j['line'], seconds)
        clipped = sum(x in (-32768, 32767) for x in samples)
        assert clipped == 0, (j['line'], clipped)
        clip = voices.AUDIO / j['file']
        assert manifest[j['line']][j['cast']] == j['file']
        for file in (source, clip):
            subprocess.run(['ffmpeg', '-v', 'error', '-nostdin', '-i', str(file), '-f', 'null', '-'], check=True, capture_output=True)
        transcript = next((batch / folder / 'status' / (j['raw'] + '.json') for folder in
                           ('transcripts-replacements', 'transcripts') if (batch / folder / 'status' / (j['raw'] + '.json')).is_file()))
        job = json.loads(transcript.read_text())['job']
        assert job['status'] == 'success'
        text = ' '.join(s['text'].strip() for s in job['metadata']['output']['segments'])
        actual, expected = words(text), words(j['text'])
        filler = actual[0] if actual and actual[0] in ('ah', 'oh') and actual[1:] == expected else None
        assert actual == expected or filler, (j['line'], j['text'], text)
        results.append(dict(line=j['line'], raw=j['raw'], seconds=seconds, clippedSamples=clipped,
                            sourceSha256=sha(source), clipSha256=sha(clip), decode='pass',
                            caption=j['text'], transcript=text, transcriptJob=job['jobId'],
                            transcriptReceipt=str(transcript.relative_to(root)),
                            captionCheck='words_match' if not filler else 'words_match_with_leading_interjection',
                            leadingInterjection=filler))
    approved = batch.parent / 'audition-gacrux-source.wav'
    town = next(r for r in results if r['line'] == 'bark_old_maud_town_1')
    assert town['sourceSha256'] == sha(approved)
    for old in json.loads((batch / 'previous-clips.json').read_text()):
        assert sha(voices.AUDIO / old['file']) == old['sha256']
    output = dict(lines=len(rows), approvedAuditionReusedExactly=True, oldClipsUnchanged=24,
                  note='ASR is a wording check, not an independent listening judgment. Punctuation, neighbour spelling and listed leading ah/oh interjections accepted; no other word changes allowed.',
                  results=results)
    (batch / 'validation.json').write_text(json.dumps(output, indent=2) + '\n')
    print(f"{len(rows)} source/clip/caption checks pass; approved source reused; old24 files intact.")


if __name__ == '__main__':
    main()
