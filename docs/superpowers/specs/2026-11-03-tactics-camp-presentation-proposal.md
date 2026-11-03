# Camp presentation follow-up

Status: accepted and implemented after the owner said “go with your vision” and authorized the art tools.
The research below informed the implementation; the final scope and verification are recorded at the end.

## Problem observed

The preview showed the music, gathering, results and dawn, but communicated very little of the party making camp or the night
progressing. Its scene was the demonstration courtyard. The earlier renderer kept the exploration camera at a fixed scale;
`RecoveryShow` supplied gathering positions and night lighting, while job outcomes mostly appeared in the side panel.
The next preview should use an authored Ashen Ford campsite and show the complete camp flow.

## References checked

Reviewed the following public documentation on 2026-11-03. These are descriptions and reference images, not a frame-by-frame
review of gameplay footage. Fandom/wiki.gg pages that rejected direct access were read through the Jina text reader.

| Game | Documented behavior | Design lesson for ArcBox |
|---|---|---|
| [Final Fantasy XV — Havens](https://finalfantasy.fandom.com/wiki/Haven) | Camping includes scenes of the friends together, a meal and an EXP tally; camp scenes are randomized. Meal benefits begin the next morning. | Show people doing things together and give the night a satisfying ending. Short variants can keep repeats fresh. |
| [Sea of Stars — Camping](https://seaofstars.fandom.com/wiki/Camping) | Camp supports party conversations, cooking and resting; Teaks can tell stories. | A compact pixel-art camp can be a place for food and companionship, with optional story content. |
| [Baldur's Gate 3 — Campsite](https://bg3.wiki/wiki/Campsite) | Camp appearance varies between regions/locations and changes with companions and story progress; night camp supports long rests and conversations. | Give the campsite a local identity while keeping a familiar party arrangement and interaction pattern. |
| [Darkest Dungeon — Camping](https://darkestdungeon.wiki.gg/wiki/Camping) | Explicit food and skill phases lead to a final rest. Camping skills visibly affect party state. | Make the sequence legible: players should know which part of the night they are in and whose actions remain. |

These are references for presentation. Existing ArcBox recovery rules, purses, DCs and rewards remain the foundation.

## Accepted direction

A live, close-up camp vignette, with a brief arrival and a sleep-to-dawn ending. The TV stays connected to the actual party and
server state while phones continue to accept choices. Full authored story cinematics can be reserved for exceptional events.

Keep the existing world as the backdrop. Compose the camp around its fire, with seated characters, bedrolls, a pot, logs and packs.
The presentation needs a camp camera focus/scale path: the current world camera's scale is fixed, so this is additional renderer
work rather than an existing zoom flag. Settle on an integer pixel scale so the art remains crisp. Keep the world cached.

### Evening storyboard

| Moment | TV scene | Visible progress and interaction |
|---|---|---|
| Arrival | Ease into the fire over roughly 2 seconds; party settles, packs land, firelight warms the foreground and the region recedes into night. | Phones can choose jobs during the arrival. Show the camp/location title and `3 of 6 jobs chosen`. |
| Camp jobs | Small activity loops: prepare bandages, stir a pot, gesture in conversation, stand watch, or sit and rest. Smoke, sparks and local ambience continue. | Show saved job badges and the real selection deadline. A reserved injury is marked as being tended. An animation before a roll represents an attempt. |
| Resolve the evening | Briefly emphasize each actor and their target. A successful meal produces bowls/steam; a successful mend fades the exact injury icon; failed cooking gives a small puff of smoke and a shrug. | Advance `2 of 6 jobs resolved` from saved results. Show the actual die/result, then a small completion/failure mark beside that player. |
| Fireside story, when eligible | The party settles around the fire; small listening/gesturing loops accompany the existing dialogue and vote. | Existing phone story controls stay active. Skip this phase when there is no eligible talk. |
| Sleep and dawn | A short settle/sleep beat, embers dim, night sound eases into morning birds, light returns, then the camera eases back to walking. | At dawn, visibly apply the saved injury/buff changes and give a compact recap: injuries mended and Well fed gained. |

A small phase strip can read **Settle in → Camp jobs → Fireside → Dawn**. Highlight the real current phase and omit Fireside when
there is no talk. Use actual choice/result counts; a percentage filling on a timer would suggest work the game is not measuring.
A moon/sky change can support the mood, but should follow these stages rather than promise a fixed real-time night length.

### Layout

Make the fire and characters the focal point. During camping, replace the exploration banner/leader furniture with a quiet camp
heading and the phase strip. Move the approved HP/resource bars and injury badges into a compact party ribbon at the bottom.
Let brief result captions sit near the acting characters. Phones retain the compact party cards and treatment details on tap.

### Area identity

Start with the two existing camps, using authored presentation profiles rather than guessing from whatever tile the leader occupies:

| Camp | Local treatment |
|---|---|
| `af_ford_campfire` — old chapel | The authored fire sits beside gravestones, rather than on the riverbank. Preserve that placement, with cooler drifting motes and the chapel title. |
| `af_road_campfire` — road east of town | Grass, roadside packs and logs, leaves moving in the breeze, fireflies and a distant town glow. |

Both share the same job loops and timing. Future authored camps could supply woodland, ruins or snow dressing without replacing
the character behavior. This proposal does not add new camp locations.

The inn can use the same evening progression in a warm room: hearth, candlelight and windows. Paid guests get the room/bed and
breakfast treatment consistent with their saved benefits; there are still no camp jobs at the inn. Temple healing remains a short
individual interaction.

## Behavior and continuity

- Drive job selection, resolution, talk and dawn from the saved camp cursor. The renderer never decides a roll or applies a heal.
- Arrival/settling is presentation layered over the existing choice window; it should not add a wait before phone input works.
- Short sleep/dawn beats should remain skippable through the leader's existing progression controls. Repeated rests should be brisk.
- Reconnects enter the saved current phase and show the current results. They do not replay or reapply completed work.
- An away character can be pictured resting while being tended. They do not gain a job or make a roll through their visual presence.
- Keep the camp music running across job results and conversations, with voice ducking and the normal return to region music at dawn.

## Implemented scope

The TV now uses a live camp vignette driven by the saved camp cursor. The cached authored world zooms to 4× pixel scale,
with a local warm fire light, visible moon/stars, class-specific job poses, shelter, personal bedrolls, animated fire/pot and bowls
after successful cooking. Earlier completed actors sit while the next saved result resolves. Actual selected/resolved counts,
roll captions and success/failure marks accompany a compact six-member HP/resource/injury ribbon. Story dialogue keeps its
existing controls and hides the ribbon while speaking.

A saved, skippable 3-second sleep phase precedes an 8-second dawn. Healing still applies only once at dawn. The scene grades
through warm sunrise into morning, the fire goes cold and the camera returns to walking. Morning remains visible after leaving
camp and after reconnect, derived from the existing saved rest sequence; this is a rest-driven transition, not a general world clock.
The inn uses the same progression with a PixelLab hearth interior, class sprites and morning light at the windows. Purchases and
sponsorship still happen on individual phones. Existing class packs already supplied sit/doze/talk/kneel/item/rise animations.

The original camp loop continues through jobs and sleep. At dawn a new original flute/harp/chime score, `camp_dawn`, plays once
and hands back to regional music without restarting it. It evokes a hopeful adventure-game sunrise without quoting an existing melody.
Its score and synthesized instruments are reproducible in `tools/tactics-audio/compose_dawn.py`; no external samples are used.
Native PixelLab requests, seeds, animation sources and the exact cold-fire edit are in the [art provenance](2026-11-03-tactics-camp-art.json).

Small narrative variants, failed-cook shrug/smoke, morning birds and bespoke away-character cameos remain possible polish;
failed outcomes already show their real saved result and failure mark. No new camp locations, rewards or recovery balance were added.
The preview is a scripted set of host views on the actual map, suitable for reviewing presentation; the six-phone live run verifies gameplay.
