# Kobold crew refresh

The owner approved the shared Scenario concepts and PixelLab direction for the knife worker and slinger. Both use Grisk as their PixelLab style reference and retain their existing combat statistics. This replaces `characters.kobold` and `characters.kobold_slinger` everywhere those keys appear.

Each atlas contains eight genuine facings for idle, walk, attack, hurt and death. The slinger also has a fire-pot throw; basic ranged attacks use the sling pose and stone projectile with the sling-release sound. Its fire-pot ability uses the cast cue, mapped to the throw clip. Portraits are cropped directly from the matching front-facing sprites; all previous mood references resolve to the new portrait.

Run `python tools/tactics-assets/pixellab/kobold-refresh/build-atlases.py` to rebuild the atlases and portraits. Source padding is removed by fixed canvas offsets, never by recentering individual moving frames. Missing frames and clipping fail the build. Characters use a 128px canvas and a 0.32 world scale, slightly smaller than Grisk's 0.34.

Paid generation receipts and original directional source are retained in this directory. Worker character: `6ae31756-f31e-447b-8c00-67671cf0efa0`; slinger: `aa983ed4-fd89-4e61-85cd-93b185bd2f40`. Concepts: `tools/tactics-assets/concepts/kobold-refresh`.

The slinger west hurt clip uses `hurt_west_fix`: the template rendered its hood as an oversized blank circle, so the reviewed custom replacement is selected explicitly by the builder. Other original clips remain preserved.
