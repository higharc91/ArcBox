# Townsfolk sprite pilot — issue #80

All 19 primary concept designs and the child-B body-view supplement were approved
on 2026-11-03: “yes, tthose look good. Proceed”. Exact asset IDs are recorded in
`../../concepts/townsfolk/approvals.json`. Do not regenerate these concepts.

Start with **Bree Tallow**, using her approved concept for identity and
`current-hero-idle.png` for the current shipped pixel density. The latter is an
unmodified first-frame crop, not new generated art. Rebuild it and its source
hashes with `./prepare-pilot.ps1`; `pilot-inputs.json` records manifest dimensions,
anchor, pixel scale and head-to-foot height. The legacy 64×80 references do not
establish the current production scale.

## Submission and review

1. PixelLab authenticated on 2026-11-03 using owner-supplied local MCP configuration.
   The static Pro pilot returned four 160×160 candidates for **15 generations**.
   Exact input and receipt are `bree-static-request.json` and
   `bree-static-result.json`; no credentials are stored here.
2. Generate one full-body facing of Bree: braided grey hair, substantial practical
   clothing, worn apron and boots, closed personal notebook. Preserve the approved
   face, build, clothing and handedness. Use restrained pixel clusters, selective
   coloured outlines, top-left light and clean transparent background. Keep her
   anatomy distinct from the armoured hero; compare their world scale at a shared
   foot baseline. Use the Ashen Ford palette from `../wardenflame-style.json`.
3. Inspect static silhouette, face, density, ground contact and camera angle before
   spending on a short idle or walk. Do not mislabel a three-quarter sprite as a
   straight south view for automatic rotation. Consult the live direction schema.
4. Inspect every motion frame at native size and enlarged: planted feet, sensible
   weight transfer, stable clothes/notebook, no limb swaps or loop jump. Normalize
   with one fixed canvas/offset across the clip; never recenter individual frames.
5. Show the owner pixels and motion beside the current party on the real TV before
   integration or expansion to the remaining characters. Keep receipts, exact
   inputs, output hashes, direction/frame metadata and approval evidence.

Named gestures, complete directions, portraits and paired generic variants follow
the approved pilot. Barmaid/farmer/merchant reference sheets have incomplete angles
per variant; check those explicitly. The child-B supplement is authoritative for
that variant's body. Preserve species/age proportions for Skarrow and children.

No live manifest or gameplay contract has changed. OW-05's used-only loading,
512 MB texture limit, Ashen Ford/Cinder Hollow memory and cold-download measurements
remain required before final integration.

## Pilot results

![Four candidates beside the shipped hero](bree-normalized-review.png)

![Hero beside Bree's idle](bree-idle-review.gif)

The Pro model ignored the requested 72-pixel body height: raw candidates are
115–117 pixels tall. `normalize-static.ps1` fits each static candidate once to
the current hero's 36-world-pixel head-to-foot height with nearest-neighbour
sampling and a shared foot anchor. No animation frames are individually moved or
rescaled. Candidate 1 was selected for a four-frame idle trial, costing
**2 generations**, giving **17 generations total** for this pilot.

The animation delivers the unchanged source plus four generated frames. All five
canvases are 160×160 and contain the complete sprite. The lowest opaque row stays
at 123; the top moves by up to two source pixels during breathing. The last frame
differs from the source by 41 pixels, so the pinned ending is close rather than
identical. Source and generated frames, hashes, bounds and changed-pixel counts
are retained. These checks do not establish walking contact or complete directions.

`build-static-review.ps1` shows raw output; add `-Normalized` for the scale review.
`build-idle-review.cjs` builds the contact sheet, GIF and measurements, using
`pngjs` and `gifenc` supplied through `NODE_PATH` without adding game dependencies.
The GIF plays at 300 ms per frame for review; no runtime timing has been chosen.
Owner pixel/animation approval and a real TV review remain open. Nothing is
installed in the game, and the rest of the NPC batch has not been generated.
