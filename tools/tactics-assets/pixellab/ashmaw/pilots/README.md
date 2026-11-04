# Ashmaw and Grisk: single-facing pilots

These are generation sources and previews, **not approved production art**. Both Scenario designs were approved before submitting these requests. The unchanged Wardenflame human-fighter style reference and Ashen Ford palette accompany each initial concept-driven request. Design hashes, seeds and quoted costs are in `plan.json`; provider job/asset IDs are in each run receipt.

The two initial `create_image_pro` jobs each returned four candidates for 10 generations. Ashmaw candidate 1 faces south correctly. Every Grisk candidate was angled; a 10-generation edit corrected candidate 1 to face the viewer before animation. The rejected requests with conflicting palette-copy options were validation errors, with no jobs created or credits charged.

The four-frame idle pilots cost 1 generation each. They retain the original and closing frames as raw sources. `normalized-atlas.png` uses a shared 160×160 padded canvas with a fixed offset across frames, avoiding the foot drift caused by recentering each frame's bounding box. The layout records playback and provisional anchors. No pixels are repainted by this normalization.

Ashmaw's first eight-frame walk cost 2 generations. It turns its muzzle near the end and finishes mid-step, so it is retained for provenance and rejected for production. A second walk request pins the closing pose to the initial sprite. Grisk's walk also pins both endpoints and explicitly keeps the pick shaft in his right hand. These two further jobs cost 2 generations each; both are complete. Grisk keeps his pick attached, but the bear still bounces too much; a skeleton-based bear gait pilot is required.

The loose-sprite pilots cost **38 PixelLab generations** total. The owner approved the pixel direction (“Yes, keep this direction”) after viewing the two idle loops together. Eight-direction generation is recorded in `../directional/`; production import remains unapproved. The final review must show actual TV world and battle motion beside the party, with correct physical scale.

No props, icons, posters or terrain art are generated here. Cubs remain out of scope unless encounter simulation requires them.
