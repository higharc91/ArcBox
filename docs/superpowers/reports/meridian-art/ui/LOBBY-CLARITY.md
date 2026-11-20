# Phone lobby clarity / November 20

The owner found the numbered pip shapes, robot/auto tag and tiled background confusing in the #335 lobby review. This revision makes the same six-seat example readable without decoding symbols.

- Existing character portraits replace the abstract pip badges in the phone roster. Portraits are clipped at native 1×, without resampling the artwork. Each row says `Seat 1` through `Seat 6` and keeps the full character name.
- The current phone's row says `You`, with a matching outline. The bot says `Bot`, without an allegiance cue.
- Every seat says `Ready` or `Not ready`; `3 of 6 ready` makes the remaining state explicit. The `I’m ready` button updates your row, the count and the live status together, and can be undone with the keyboard or another tap. The ready-state helper explains how to cancel.
- A quiet background, smaller brand header, separate `Room code` panel and a single clear primary button replace the busy tiled composition.

Screens: [390px](phone-390.png), [320px](phone-320.png), [390px ready](phone-390-ready.png), [320px ready](phone-320-ready.png). The full roster and controls fit inside 320×844 and 390×844 without internal scrolling, horizontal overflow or clipped names in both states. Six portraits, all six seat labels, one `You`, one `Bot`, ready-state consistency and keyboard undo are checked in real Chromium. The existing reduced-motion and five-phase checks still pass.

Actual deuteranopia/protanopia transforms are retained for both widths and both states. Run `check-ui-browser.cjs` then `check-ui-native.py` to reproduce. The five TV captures remain pixel-identical to the existing native Godot review images, and all 164 rendered panel corners still match nearest 2× source pixels. No TV layout or immutable runtime PNG/manifest changes are part of this revision.

This updates the illustrated lobby from the user's screenshot in the art review. It does not wire the mock Ready state to a server or import the game implementation from open #316. #335 remains open for visual review; adoption by the game consumers and the separate #405 platform hook are still tracked. No human or physical-device usability test is claimed.

The eight focused UI tests pass. Complete pre-merge validation is recorded separately in `lobby-clarity-validation.json` and `lobby-clarity-full.txt.gz` after the full gate finishes; the November 18 `validation.json` is historical evidence for #420.
