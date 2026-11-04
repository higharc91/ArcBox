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

1. Read the live PixelLab schema and quote the pilot's actual cost. No current
   quote is available: PixelLab MCP is absent and the browser requires sign-in.
   Historical generation costs are not a current quote. No pilot was submitted.
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
