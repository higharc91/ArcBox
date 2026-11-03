# Ashmaw bounty art

The approved Scenario concepts are under `../../concepts/ashmaw/`. The owner approved both concepts and the initial pixel direction. Actual TV world/battle motion was shown on 2026-11-03; production integration is gated on that separate approval under issue #74 §6.2.

`directional/ashmaw-padded` and `directional/grisk-template` retain the paid eight-direction source images and animation frames. Request/result JSON preserves the character, job and animation-group identifiers. Rejected takes remain available for audit. `curation.json` names the clean frames selected when a generated take duplicated anatomy, invented a long tail or dropped Grisk's pick. Do not mirror directional weapons or recenter each frame around moving bounds.

```sh
python tools/tactics-assets/pixellab/ashmaw/build-preview.py /tmp/hollow-review
ARCBOX_CINDER_ART_ROOT=/tmp/hollow-review godot --path src/ArcBox.Host -- --world-demo=cinder_hollow
```

The builder creates a separate review pack. It removes fixed canvas padding, aligns the original rotation's ground contact and rejects any clipped source frame. Ashmaw uses a 192px frame, anchor 96,171 and scale 0.65; Grisk uses 128px, anchor 64,110 and scale 0.34. Every action has all eight facings. Both have idle, walk, attack, hurt and down; Ashmaw also has roar/shake and Grisk has shout. Portrait sources are 64px and normalize to the existing 40px HUD format.

The owner additionally authorized cave props: candles, animal remains and dried blood. These use the established `human_fighter_world` style reference and explicit `ashen_ford` palette. Original 48px sources and paid receipts are retained here. Candle bodies import at exactly half size, with a small flame-emission mask and the renderer's candle flicker. The generated candle animation was rejected because it added conspicuous white smoke. `cave-props-review.json` records the review and failed provider attempts. Bones and stains are nonblocking and provide no cover; the bones represent old animal remains, not the surviving fisher's boy.

`credit-ledger.json` records the observed account delta separately from historical receipt subtotals. Do not treat an earlier partial ledger or an estimate as final charged usage. No new cave palette was introduced.
