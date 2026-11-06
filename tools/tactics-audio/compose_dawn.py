"""Render ArcBox's original sunrise cue from its score and original synthesized instruments.

No samples, soundfonts, copied melody or external service. NumPy + ffmpeg are the only dependencies.
Run: python3 tools/tactics-audio/compose_dawn.py /tmp/camp_dawn.wav
Then import using audio.encode (sting target) and audio.write_manifests.
"""
from pathlib import Path
import sys
import wave
import numpy as np

RATE = 48000
BEAT = 60 / 96
DURATION = 6.8
# Original G-major phrase, written for a brief, hopeful sunrise rather than a loop.
MELODY = [(0, 71, .4), (.5, 74, .6), (1.25, 79, .7), (2.25, 76, .4), (2.75, 81, .6),
          (3.5, 79, .4), (4, 78, .4), (4.5, 74, .4), (5, 76, .4), (5.5, 78, .4),
          (6, 83, .5), (6.75, 79, 1.5)]


def frequency(note):
    return 440 * 2 ** ((note - 69) / 12)


def tone(note, seconds, kind):
    t = np.arange(int((seconds + .45) * RATE)) / RATE
    f = frequency(note)
    if kind == "flute":
        phase = 2 * np.pi * f * (t + .0015 / (2 * np.pi * 5) * np.sin(2 * np.pi * 5 * t))
        sound = np.sin(phase) + .12 * np.sin(2 * phase) + .025 * np.sin(3 * phase)
        env = np.minimum(t / .05, 1) * np.exp(-np.maximum(0, t - seconds) / .12)
    elif kind == "harp":
        sound = sum(np.sin(2 * np.pi * f * n * t) * np.exp(-t * (1.9 + .22 * n)) / n ** 1.7 for n in range(1, 13))
        env = np.minimum(t / .007, 1) * np.minimum(np.maximum(0, seconds + .4 - t) / .15, 1)
    elif kind == "bell":
        sound = sum(a * np.sin(2 * np.pi * f * r * t) * np.exp(-t * decay)
                    for r, a, decay in [(1, 1, 2), (2, .4, 3), (3, .12, 5), (4.2, .06, 6)])
        env = np.minimum(t / .003, 1) * np.minimum(np.maximum(0, seconds + .4 - t) / .2, 1)
    else:
        sound = np.sin(2 * np.pi * f * t) + .18 * np.sin(2 * np.pi * f * 2.002 * t)
        env = np.minimum(t / .22, 1) * np.exp(-np.maximum(0, t - seconds) / .18)
    return sound * env


def render(path):
    output = np.zeros((int(DURATION * RATE), 2), dtype=np.float64)

    def add(beat, note, length, kind, gain, pan=0):
        start = int((.12 + beat * BEAT) * RATE)
        samples = tone(note, length * BEAT, kind) * gain
        count = min(len(samples), len(output) - start)
        if count <= 0:
            return
        output[start:start + count, 0] += samples[:count] * np.sqrt((1 - pan) / 2)
        output[start:start + count, 1] += samples[:count] * np.sqrt((1 + pan) / 2)

    for beat, note, length in MELODY:
        add(beat, note, length, "flute", .24, .1)
    for beat, chord in [(0, [55, 59, 62, 67]), (2, [48, 55, 60, 64]), (4, [50, 57, 60, 66]), (6, [43, 55, 59, 67])]:
        for i, note in enumerate(chord):
            add(beat + i * .18, note, 2, "harp", .13, -.35 + i * .15)
        add(beat, chord[0] - 12, 1.7, "pad", .065, -.15)
    for beat, note in [(0, 91), (2.75, 93), (6, 95), (6.75, 91)]:
        add(beat, note, 1.6, "bell", .025, .4)
    # Small room reflections preserve the stereo arrangement and gently blend the instruments.
    dry = output.copy()
    for delay, gain in [(.113, .13), (.239, .09), (.371, .06), (.527, .035)]:
        offset = int(delay * RATE)
        output[offset:] += dry[:-offset, ::-1] * gain
    tail = int(.55 * RATE)
    output[-tail:] *= np.linspace(1, 0, tail)[:, None]
    output *= .78 / max(.001, np.max(np.abs(output)))
    pcm = np.round(np.clip(output, -1, 1) * 32767).astype('<i2')
    with wave.open(str(path), 'wb') as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(pcm.tobytes())


if __name__ == '__main__':
    render(Path(sys.argv[1]))
