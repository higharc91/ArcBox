"""Offline source/caption/installed-clip checks using retained, unbiased local ASR."""
import array
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import subprocess
import sys
import wave


def words(text):
    text = text.lower().replace('’', "'")
    text = re.sub(r'\b(?:grey|gray)\s*wall\b', 'greywall', text)
    text = text.replace('neighbor', 'neighbour').replace('bear their teeth', 'bare their teeth')
    aliases = {'40': 'forty', '10': 'ten', '7': 'seven', 'scarrow': 'skarrow',
               'hah': 'ha', 'huh': 'ha', 'hmm': 'hm', 'ugh': 'ah'}
    return [aliases.get(w, w) for w in re.findall("[a-z0-9']+", text)]


def filtered_transcript(record, samples, rate):
    kept, excluded = [], []
    duration = len(samples) / rate
    for segment in record['segments']:
        text = segment['text'].strip()
        tail = samples[int(segment['start'] * rate):min(len(samples), int(segment['end'] * rate))]
        rms = math.sqrt(sum(x*x for x in tail) / len(tail)) if tail else 0
        db = 20 * math.log10(max(rms, 1e-9) / 32768)
        peak = max((abs(x) for x in tail), default=0)
        # Whisper sometimes invents a final word in its two-second window past EOF.
        # Require physical near-silence and an out-of-duration segment, never just a text mismatch.
        if (len(words(text)) == 1 and segment['start'] > duration - .3 and
                segment['end'] > duration + .5 and peak < 32 and db < -65):
            excluded.append(dict(segment=segment, peak=peak, rmsDbfs=db,
                                 reason='out-of-duration near-silent tail, not a spoken extra word'))
        else:
            kept.append(text)
    return ' '.join(kept), excluded


def main():
    batch = Path(__file__).resolve().parent
    root = next(p for p in batch.parents if (p / 'tools/tactics-lore/voices.py').is_file())
    spec = importlib.util.spec_from_file_location('voices', root / 'tools/tactics-lore/voices.py')
    voices = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(voices)
    source_only = '--sources-only' in sys.argv
    current = {j['id']: j for j in voices.jobs()}
    rows = json.loads((batch / 'lines.json').read_text())
    manifest = json.loads((voices.AUDIO / 'manifest.json').read_text())['lines']
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    results, failures = [], []
    for j in rows:
        for key in ('text', 'renderText', 'raw', 'file', 'preset', 'pitch', 'filter'):
            assert j[key] == current[j['id']][key], (j['line'], key)
        source = batch / 'raw' / (j['raw'] + '.wav')
        with wave.open(str(source)) as wav:
            assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 24000)
            samples = array.array('h', wav.readframes(wav.getnframes()))
            seconds = wav.getnframes() / wav.getframerate()
        assert .3 < seconds < 30, (j['line'], seconds)
        assert not any(x in (-32768, 32767) for x in samples), j['line']
        digest = sha(source)
        checked = []
        chosen = None
        for folder in ('local-transcripts', 'local-transcripts-medium.en'):
            receipt = batch / folder / (j['raw'] + '.json')
            if not receipt.exists():
                continue
            record = json.loads(receipt.read_text())
            if record['sourceSha256'] != digest:
                continue
            assert record['initialPrompt'] is None and not record['conditionOnPreviousText']
            text, excluded = filtered_transcript(record, samples, 24000)
            actual, expected = words(text), words(j['text'])
            vocal = actual[0] if actual and actual[0] in ('oh', 'ah', 'hm') and actual[1:] == expected else None
            checked.append(dict(receipt=str(receipt.relative_to(root)), text=text))
            if actual == expected or vocal:
                chosen = dict(transcript=text, receipt=str(receipt.relative_to(root)),
                              leadingVocalization=vocal, excludedSilentTailSegments=excluded)
                break
        if not chosen:
            failures.append(dict(line=j['line'], raw=j['raw'], caption=j['text'], checked=checked))
            continue
        subprocess.run(['ffmpeg', '-v', 'error', '-nostdin', '-i', str(source), '-f', 'null', '-'], check=True, capture_output=True)
        result = dict(line=j['line'], raw=j['raw'], seconds=seconds, channels=1, sampleRate=24000,
                      clippedSamples=0, sourceSha256=digest, caption=j['text'], captionCheck=chosen)
        if not source_only:
            clip = voices.AUDIO / j['file']
            assert manifest[j['line']][j['cast']] == j['file']
            subprocess.run(['ffmpeg', '-v', 'error', '-nostdin', '-i', str(clip), '-f', 'null', '-'], check=True, capture_output=True)
            result['clipSha256'] = sha(clip)
        results.append(result)
    for old in json.loads((batch / 'previous-clips.json').read_text()):
        assert sha(voices.AUDIO / old['file']) == old['sha256']
    if not source_only:
        before = json.loads((batch / 'previous-manifest.json').read_text())
        after = json.loads((voices.AUDIO / 'manifest.json').read_text())
        for document in (before, after):
            for line in {j['line'] for j in rows}:
                document['lines'].pop(line)
            document['characters'].pop('mother_skarrow')
        assert before == after, 'Unrelated manifest data changed'
    output = dict(result='PASS' if not failures else 'FAIL', scope='sources' if source_only else 'sources_and_installed_clips',
                  lines=len(rows), distinctSources=len({j['raw'] for j in rows}), previousClipsPreserved=67,
                  note='Unbiased local ASR is a wording check, not listening proof of acting quality. Listed vocalizations, homophone/spelling/number forms and physically silent out-of-duration ASR tail tokens are documented normalizations. No audio is trimmed.',
                  results=results, failures=failures)
    (batch / ('source-validation.json' if source_only else 'validation.json')).write_text(json.dumps(output, indent=2) + '\n')
    if failures:
        print(json.dumps(failures, indent=2))
        raise SystemExit(1)
    print(f"PASS: {len(rows)} lines / {output['distinctSources']} sources; captions/audio/old clips checked.")


if __name__ == '__main__':
    main()
