# Mother Skarrow — first pixel appearance and existing voice

This draft translates her existing owner-approved concept into eight standing
pixel views. She is the elderly goblin chief who has led her starving clan for
forty winters: wary, dignified and practical, with dry humour. No new lore.

![Appearance review](candidate-1-preview.png)

[Hear her existing recorded voice](voice-sample.mp3): “Forty winters I have held
this clan. Do not ask me if I can hold it one more.” Existing Laomedeia town2
recording, already pitch−2 in production; compatibility MP3 transcode only, no
new speech or additional pitch/filter. Source/text/hashes in voice-sample.json.

Front, southwest, west, northwest; north, northeast, east, southeast. Muted green
skin, long ears/hooked nose, deeply weathered face, greying hair/front braids/back
braid, patched olive/brown hide and wool, modest food pouches and worn boots.
Plain LEFT-hand wooden staff (imageRIGHT front /imageLEFT back), no magic flame.
Short adult goblin proportions: proposed50 native body pixels /25 world, against
human72/36. Scale remains subject to native and TV review, not inferred from
concept painting dimensions. She is an elder, not a human-sized mage or child.

One image-tool call; original opaque magenta export/request/input/output hashes
retained. build-appearance.cjs removes only key pixels, preserves non-key RGB,
clears transparent RGB and produces binary alpha, without redraw/resize/mirroring.
After npm ci --prefix tools/tactics-assets/imagegen/townsfolk, rebuild from root:

    node tools/tactics-assets/imagegen/townsfolk/mother-skarrow/build-appearance.cjs

Eight complete standing-cell bounds and voice decoding pass. This is an enlarged
appearance sheet, not a validated native atlas. Native grid/palette/goblin size/
anchors/ear and staff handedness/boot-contact QA remain before motions. Owner
appearance approval before animations/portraits; separate finished-set/TV approval
before integration. Preserve named-only scope and required battle presentation;
legacy generic goblin art/rules/contracts stay unchanged.

Zero PixelLab calls/credits/purchases and zero new speech. No image-tool monetary
price inferred. Production content, rules, balance and phone contracts unchanged.
Include the existing recorded voice in subsequent reviews; reuse these sources.
