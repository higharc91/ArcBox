# Eight crew lock-in signatures

Owner-requested addition after the November 20 lobby review: one cute, short sound for each of the eight crew members. These are original **wordless musical/gear chirps**. Existing voices and Tiller are unchanged. Generation spend: **0 CU**.

[Audition the cues](index.html) or [play the sampler](all-crew.mp3). Order: Ines, Tobe, Sable, Anselm, Pell, Juno, Kit, Rhee. Each WAV has an audio player, a Play cue button and a simulated lock-in demonstration. This review is not a server-connected game.

| Crew | Signature |
| --- | --- |
| Ines | Warm toolbox chime |
| Tobe | Round rigging plonk |
| Sable | Upward pilot whistle |
| Anselm | Gentle medical scanner reply |
| Pell | Playful terminal greeting |
| Juno | Cargo-latch boop |
| Kit | Curious scout chirrup |
| Rhee | Sparkling radio hello |

## Phone integration

The common `Feedback.onView` delegates to the game-owned `CrewSelection` observer. After an unchosen Boarding view, only **this phone's confirmed** `crewPick.mine`, or its own `sheet/turn.me` after Boarding, plays a signature. The last picker may skip the chosen roster view; the observer waits through presentation for the actual accepted identity. A timeout auto-assignment uses the confirmed crew, not an attempted choice.

Taken/rejected selections, other players, secret briefings, duplicate IDs, later ordinary turns and already-chosen reconnect baselines do not emit a cue. Other phases discard a missed confirmation so it cannot appear during the story. Leaving the controller resets only selection history, preserving generic feedback. Mute consumes confirmation silently, without catch-up on unmute. Existing user-gesture unlocking, WebKit fallback and unavailable-audio handling are reused. Only the selecting phone plays its signature.

The hook and observer are implemented on main against open game PR #316's current protocol at `b1c8ea985a7e8aaafe0fb97229ae58520ab82caa`. Three unmodified payload fixtures are retained in `web/play/test/fixtures/meridian-selection/`; separate recordings' v1 envelopes get distinct platform IDs in sequence tests. The playable Meridian module remains in #316. Its controller already uses this common feedback hook, so no additional custom-screen patch is needed when it lands. This is not a whole-game/physical-phone playtest.

## Sound and artifacts

The phone synthesizes the cue with WebAudio, requiring no asset download. Three softly attacked and decaying sine/triangle notes last 0.42s. Equivalent exports include an 80ms tail for a total of 0.5s, at 48kHz mono. Source peaks are approximately −16.5 to −17.7 dBFS and RMS −28.6 to −30.5 dBFS. These are quiet phone UI cues, not a claim to complete #338's core −16 LUFS music/SFX delivery.

`content/meridian/base/audio/crew-lock-in/manifest.json` hashes eight immutable Ogg Vorbis files. This supplemental child set leaves the future core `audio/manifest.json` and canonical IDs untouched. OfflineAudioContext renders the exact live recipe. The encoder uses bit-exact container settings; stable signal measurements are rounded to 0.001dB rather than retaining insignificant browser floating-point noise. A same-engine rebuild produces identical manifest bytes and cue hashes. The review bundle is generated from actual runtime source.

## Checks and reproduction

Run `node tools/meridian-assets/build-crew-sounds.cjs`, serve the repository root on 4187, then `node tools/meridian-assets/check-crew-sounds.cjs`. Source the managed activation script for Node/Playwright; system ffmpeg encodes the exports.

Real Chromium at 320×844 and 390×844 checks all eight confirmations, no autoplay, mute/no delayed unmute replay, keyboard operation, loaded portraits and horizontal fit. All Oggs decode with verified hashes, duration, mono channels, non-silence and peak limits. Phone tests cover confirmed/rejected picks, unknown/duplicate data, other games and secrecy, last-picker/auto-assignment, reconnects including a same-id unselected view, mute and real controller lifecycle. Existing short/quiet tests now cover all added cues without weakening old limits. No human listening session, physical TV or physical iPhone test is claimed.

#338 remains open for its separate music, core SFX and Tiller review. Full pre-merge validation is recorded beside this report after the gate finishes.
