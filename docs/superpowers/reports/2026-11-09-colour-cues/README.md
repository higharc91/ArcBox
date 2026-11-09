# Colour-independent battle cues — #169

The TV uses **round party rings, diamond enemy rings and triangular neutral rings**, with dark/light outlines that stay visible on grass or stone and a wider white outer line for the active unit. Its legend and unit-card side names reinforce those shapes. HP/max HP, temporary HP, Bloodied and named conditions remain explicit in the cards. The existing numbered phone map/roster is retained, including separate identities for enemies with identical names.

On the phone, round ally/you cells and square enemy cells identify sides; the active hero has an outline. Reach has a dotted edge, legal aim a double edge, risky movement a diagonal hatch, and reaction spread a dashed box. The key and tile descriptions name these meanings without colour words. Numeric tile/unit labels have opaque backplates. Patterns remain static with reduced motion. A regression checks movement risk, legal aim and reaction spread descriptions while browsing without sending a command.

These are **actual Chromium and native Godot screenshots**, transformed with the retained `simulate.py`: Machado et al. (2009) severity 1.0 protanopia/deuteranopia matrices in linear RGB, then converted back to sRGB. Both simulations were visually inspected. The circle/diamond distinction, active outline, HP numbers/Bloodied label and phone patterns remain distinct. This is simulation review, not a claim of human colour-vision testing.

The native capture uses production BoardCanvas, HudLayer and unit cards on the shipped Forest Ruins map with controlled actors/HP/conditions. The phone movement capture uses the real environment fixture. The aiming/roster captures use the real multi-attack fixture with controlled injured HP (hero 6/12 and the first enemy 3/16) to check health readability. Existing art, rules, voices and reduced-motion support are preserved. Captures were checked at 320×844 and 390×844; native TV is 1920×1080. The final #177 audit covers these same phone styles.

| View | Original | Protanopia | Deuteranopia |
| --- | --- | --- | --- |
| Native TV | [capture](tv-cues.png) | [simulation](tv-cues-protanopia.png) | [simulation](tv-cues-deuteranopia.png) |
| Phone movement/risk | [capture](phone-move-390.png) | [simulation](phone-move-390-protanopia.png) | [simulation](phone-move-390-deuteranopia.png) |
| Phone aiming | [capture](phone-aim-390.png) | [simulation](phone-aim-390-protanopia.png) | [simulation](phone-aim-390-deuteranopia.png) |
| Numbered health roster | [capture](phone-units-390.png) | [simulation](phone-units-390-protanopia.png) | [simulation](phone-units-390-deuteranopia.png) |

The 320 px captures and legend captures are also retained. Native capture sources are stored as `.txt` files so review scenes do not ship. Use the unit-card report’s capture instructions, updating absolute paths. `python3 simulate.py <capture-directory>` reproduces the simulations with NumPy and Pillow. Full repository gate totals are recorded in the PR before merge.
