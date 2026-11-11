# #189: verified existing shipped-audio normalization

The implementation was already merged in `91c16b1a`. On current main `cf156c46`, a fresh ffmpeg EBU R128 / true-peak audit measures **305 SFX and 16 music files**. Every registered file meets its group target within ±3 dB and is at or below **−1 dBTP**. Short clips use the existing RMS fallback. [Every measurement](measurements.json), [group ranges](summary.json) and [verification](verification.json) are retained.

The shipped audio files and audio tools are unchanged from the last fully passing main tree `cdb047fe`; the existing ShippedLoudness regression has also passed in the current full check. This verification generates no replacement audio and changes no voices.

Repeat with:

```sh
python3 -m unittest discover -s tools/tactics-audio
```

| Group | Files | Target | Measured min/max | Maximum true peak | RMS fallback |
|---|---:|---:|---:|---:|---:|
| music/music | 12 | -20.0 | -20.60 / -19.90 | -5.5 | 0 |
| music/sting | 4 | -19.0 | -19.10 / -18.90 | -5.9 | 0 |
| sfx/ambience | 16 | -24.0 | -24.80 / -21.50 | -1.0 | 0 |
| sfx/battle | 32 | -18.0 | -20.30 / -16.83 | -1.0 | 4 |
| sfx/creature | 62 | -18.0 | -20.80 / -16.30 | -1.1 | 1 |
| sfx/footstep | 36 | -18.0 | -20.93 / -17.20 | -1.0 | 5 |
| sfx/impact | 26 | -18.0 | -21.00 / -16.60 | -1.0 | 3 |
| sfx/spell | 32 | -18.0 | -20.90 / -17.00 | -1.0 | 0 |
| sfx/ui | 24 | -21.0 | -23.90 / -20.10 | -1.0 | 11 |
| sfx/weapon | 43 | -18.0 | -20.95 / -16.17 | -1.0 | 29 |
| sfx/world | 34 | -18.0 | -20.50 / -16.20 | -1.0 | 2 |

Levels use LUFS, or RMS dBFS for short clips; true peaks use dBTP. Limits and fallback rules are enforced by `tools/tactics-audio/test_audio.py`.
