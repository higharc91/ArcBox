# Camp and recovery on touch: issue #175

Camp job buttons now show the result of a successful roll and the player's actual skill and target number from the server. Watch explains that its target varies by secret, and Rest explicitly needs no roll. The help sheet explains rolling twice for a healing spell and what leaving a job unchosen means.

Recovery cards show injury names alongside their icons. Tapping an injury opens its existing detail/treatment sheet. Well fed has a visible name and “3 temporary HP next battle”; neither benefit depends on a hover tooltip. Haggling states the purchase/sale benefit before the skill check and uses “needed” to explain a result's target. HP and each class's named resources remain the terms used throughout these screens.

TV recovery names, meters and notes use at least 24 px. Compact card resources stack vertically, and Well fed has a text benefit. The camp ribbon and result caption were repositioned to fit six cards at 1920×1080. An initial native capture caught clipping with six members; the final 334 px ribbon passes label bounds checks for both four and six members.

## Previews

- [Phone camp jobs and party cards](phone-camp.png), 390 px wide.
- [Injury detail on a narrow phone](phone-injury-320.png), 320 px wide.
- [Shop haggling benefit before the roll](phone-shop.png), 390 px wide.
- [Native TV recovery cards for six members](tv-six-members.png), 1920×1080.

The phone previews use the committed real server fixtures, with Well fed enabled on the first member and an available haggle for the shop. Chromium checks at 320×844 and 390×844 verify no horizontal overflow, a minimum 44 px injury button, the complete injury sheet inside the viewport after its animation, and the correct campJob/shopHaggle commands. Three focused interaction tests cover visible job/check information, injury and meal labels without title attributes, and the updated help copy; existing haggle tests still check the server's actual result target.

The TV preview uses Godot 4.7.2 .NET under Xvfb/Mesa OpenGL, calling the production RecoveryCard builder with four and six well-fed members, two resources and two injuries each. Every label fits inside the final ribbon and viewport. It is a native card layout check rather than a whole campaign run. The temporary preview scenes/scripts were removed. The renderer reports the existing V-Sync/screen-space-AA compatibility warnings at startup.
