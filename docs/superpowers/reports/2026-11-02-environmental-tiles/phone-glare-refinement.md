# Phone terrain glare refinement

The user found the initial phone terrain animation too subtle and requested stronger effects, suggesting glare.
Runtime/style/tests: **`42ae4abe`**.

Each current terrain now adds a visible foreground rim, slow lighting breath and travelling edge reflection. Fire is amber,
ice is blue-white with frosted corners, water is cyan, oil has an iridescent edge, webs have pale corner strands, and scorched
ground has a smoky brown rim. Larger, brighter background embers, crystals and ripples support the new glare.

The reflection stays at the edges with a clear centre. Decorative layers have no pointer events and no screen-reader content;
the existing readable terrain label remains. At most 24 particles are used, with one extra DOM layer for the primary terrain's
two CSS pseudo-elements. Only opacity and transforms animate; static gradients/masks supply the highlights. Reduced-motion
mode stops both the particles and the new glare animations, retaining a static rim. All effects clear on ordinary terrain.
No server contract, forecast, movement, gameplay, or encounter setting changed.

[Updated six-terrain animation preview](phone-glare-preview.mp4) · [Before and after](phone-glare-comparison.png)

The preview uses the same real Chromium controller and development surface-look overrides documented in F, with the actual
pack art. The comparison pairs F's original burning-ground screenshot with the enhanced look. New browser checks verify that
the foreground glare cannot intercept taps, its pseudo-elements stop under reduced motion, and it clears with the old ambient
layer. Existing narrow/landscape, risk, spread/cursor and page-error checks also pass. The existing lifecycle tests now cover
both decorative layers. Final validation and source fingerprints are in [phone-glare-validation.json](phone-glare-validation.json).

Final validation: **5,864 .NET tests** and **483 web tests** pass. TypeScript checking and the production build pass, with
the same pre-existing flyover chunk-size advisory recorded in F. All phone fixture bytes and all six golden replay/event
files are unchanged. [Solution log](phone-glare-tests.txt) · [Web tests](phone-glare-web.txt) · [Build](phone-glare-build.txt).
