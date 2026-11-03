# Phone web strands

The user approved the other terrain effects but found the floating square shapes did not resemble spider webs.
Runtime/style/regression test: **`5bcf0d66`**.

Web terrain now uses three corner-anchored webs. Each has radial spokes and curved, sagging cross-strands drawn as open SVG
silk, with no rectangular tile backgrounds or floating web particles. The web hubs stay attached to the corners while their
strands sway gently at different speeds. The centre remains clear, and the pale strands leave controls readable.

The ordinary edge reflection remains; the old radial background pattern and square-particle styling were removed. The webs
also render when web is the secondary visible terrain layer. All silk is decorative, non-interactive and hidden from screen
readers. Reduced motion freezes the webs, and leaving the terrain removes them. Other terrain and all server/gameplay code
are unchanged.

[Updated web animation](phone-web-preview.mp4) · [Phone screenshot](phone-web-strands.png)

The preview uses the same real Chromium controller and explicit development look override documented in F. A new regression
case checks that web terrain has anchored SVG silk, no background particle tiles, and clears on fresh ordinary-terrain data.
Browser checks verify sway, reduced motion, clearing, controls, and the existing narrow/landscape and spread-cursor behavior.
Final results and source fingerprints are in [phone-web-validation.json](phone-web-validation.json).

Final validation: **5,864 .NET tests** and **484 web tests** pass. TypeScript checking and production build pass, retaining
F's existing Three.js flyover chunk-size advisory. All phone fixtures and six replay/event goldens remain byte-identical.
[Solution log](phone-web-tests.txt) · [Web tests](phone-web-client-tests.txt) · [Build](phone-web-build.txt).
