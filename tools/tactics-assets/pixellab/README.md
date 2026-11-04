# Wardenflame PixelLab profile

Issue #80's NPC and townsfolk concepts are in
[`../concepts/townsfolk/README.md`](../concepts/townsfolk/README.md).
Bree and Mother Skarrow are approved. The other 17 character/preset concept
designs await review; all primary sheets and a child-B body-view supplement are
saved, including recovered existing Halden and Maud concepts. NPC PixelLab
generation has not started. Use the current shipped tactical hero density and anchors for the
production comparison, rather than assuming the older reference PNGs establish
the current hero size.

`wardenflame-style.json` is the reusable **local** generation profile. It contains
the shared art brief, five biome palettes taken from the shipped material ramps,
approved reference-image paths/hashes, intended canvas sizes and animation rules.
It contains no credentials and does not change the live game's assets.

PixelLab's live MCP tools and `search_help` were checked on 2026-11-02:

- Projects/folders organize assets; they do not automatically enforce an art theme.
- No MCP tool creates a project/folder. The account had zero projects when checked.
  A folder can be created through PixelLab's website and its real ID added to the
  profile. Use `add_to_project` to organize completed assets afterwards.
- Supply the same fitting `style_image`, `style_options`, explicit `palette`
  values, palette name and description settings on each compatible request.
  Supported parameters vary by tool and mode; consult its current input schema.
- Pro Flash accepts style references and palette constraints. A reference's native
  dimensions must fit the target canvas in both axes. The small wood reference
  fits a small prop; a full 64×80 character reference does not fit a 32×32 output.
- The included character references are three-quarter views. They are suitable
  **style anchors**, not straight-on south views for automatic character rotation.
  Mislabeling them would offset all generated direction labels.

## Animation

| Use | Supported route | Integration consideration |
|---|---|---|
| Existing loose sprite | `animate_image`, `animate_image_pixminimax` | Supply a native exported frame; inspect output size, timing and loop seam |
| Stored PixelLab character | `animate_character` | Templates have fixed frame counts; custom motion and skeleton-v3 are also available |
| Our authored pose data | `animate_with_skeleton_v3` | Beta; requires all expected joints per pose; suited to comparing motion against our rig |
| Stored environmental object | `animate_object` | v3 is the cheaper default; requested frames may include an additional source frame unless disabled |
| Clothing study across a clip | `transfer_outfit` | Redraws pixels; does not export independent equipment layers or guarantee identical geometry |

`animate_image` returns the input plus the requested generated frames. Character
v3 animation can expand the canvas; templates and other modes have different
limits. Normalize and inspect returned files rather than assuming they already
match the production atlas.

PixelLab supplies candidate flattened art, not our material-indexed body, clothes,
hands and weapon layers or a guaranteed grip contract. Keep the production rig,
equipment sockets and contact/clipping validation. Begin animation trials with one
facing and one short idle/walk or a simple environmental loop. No generation jobs
were started while researching or preparing this profile.

Sources: the authenticated MCP tool schemas, its `get_pro_flash_capabilities`
response and two `search_help` queries; public overview:
<https://api.pixellab.ai/mcp/docs>.
