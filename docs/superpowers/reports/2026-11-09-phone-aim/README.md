# Phone aim previews (#161)

Direct target chips and list cards explain the opening hit or failed-save probability, attack/save modifier, effective AC/DC, advantage/disadvantage with the relevant condition/passive/situation, and the full damage range including misses/criticals. Automatic effects say so. Multi-attack ranges describe the opening attack, with an explicit note that later attacks can change as effects are spent. Existing whole-use expectations remain available to callers. Secondary reactions use a damage-probability distribution including their trigger chance and identify their whole-use scope.

The combat scorer supplies situated odds including cover/high ground, without rolling RNG. Repeated typed previews and roll descriptions are interned by the phone adapter; the client expands them before rendering. The original 32 KiB choice/60 KiB packet guards and environmental 64 KiB guards pass unchanged. Target buttons retain their names and expose odds as accessible descriptions.

Focused validation: seven server aim cases, 18 coating cases, five packet-size cases, three environment/compaction cases, 18 production fixture cases and seven phone aiming/multi-attack cases pass. The coating test now explicitly checks the intervening ally’s cover (+12 vs AC 20, 65%) and the once-only bonus formula. The production phone build passes.

Real Chromium: Attack and Burning Hands aiming states at 320×844 and 390×844 pass all four size/target/contrast/overflow audits. JSON reports and screenshots are retained. Additional screenshots use actual Chromium deuteranopia/protanopia simulation and were visually reviewed: numeric odds, roll types and text retain their meaning without colour. Reduced motion remains enabled. No physical-phone or human usability test is claimed.

Reproduction uses the ordinary Vite journal fixtures, selects the ability and first target, and runs `tools/tactics-playtest/phone-accessibility.cjs` on that aiming state. The full repository gate is required before merge.
