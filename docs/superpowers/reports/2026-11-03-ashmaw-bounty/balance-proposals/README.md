# Superseded level-2 Ashmaw proposal

This 74.6% proposal was never installed. The owner subsequently requested approximately 60% four-player wins and clarified that only four-to-six-member fights should become harder. Retained here as calibration evidence; see the final group-only balance report.

| Setting | Current draft | Recommended proposal I |
|---|---|---|
| AC | 12 | 14 |
| HP for 1–6 members | 32 / 60 / 90 / 120 / 150 / 180 | 20 / 60 / 160 / 240 / 320 / 400 |
| Maul | Two separate attacks, each 1d6 on hit | Two separate attacks, each 1d6+7 on hit |

The damage expressions are the complete on-hit values; no additional Strength bonus is silently added. The lower solo HP offsets the stronger attacks. No cubs, new abilities or new mechanics are proposed.

At level 2, **358/480 four-member runs won (74.6%)**, averaging **11.8 rounds** with a median of 11. Four runs reached the 30-round cap and count as losses. This meets the requested approximate 55–75% full-party win / 8–12-round target in this sample. The 24 parties and 20 seeds are listed in the [full report](ashmaw-candidate-i-party.md).

Solo level-2 Story won **53/100** runs across five sampled healer/defender builds, averaging 3.0 rounds. This proves sampled solo viability, not all-class coverage. See the [solo report](ashmaw-candidate-i-solo.md).

The adjacent candidate H uses identical solo/four-member HP and AC and one less Maul damage per hit: 78.5% four-member wins, 11.5 rounds, 59% sampled solo Story wins. It is a gentler alternative. Health-heavy pilots extended fights to 14–17 rounds; the lower-health, stronger-attack proposal avoids that prolonged version of the fight. Earlier weak candidates and stronger pilots remain outside the production pack.

`proposal.json` records exact settings and input hashes. To reproduce, copy the base `rules` directory at commit `8d0f2eb9`, replace its three files with `overrides/`, then run Tactics.Sim with that copied `--rules` directory. The [1,380-battle level 1–3 / sizes 1–4 confirmation](ashmaw-candidate-i-scaled.md) used 80 HP for two members, which was too severe. The final proposal uses 60 HP for two members, verified in a separate [360-battle refinement](ashmaw-candidate-i-two-member.md). At level 2, two-member wins rose 44.2% → 60.8%; three-member wins were 65.0%. Other settings and sampled results are unchanged. Five/six-member numeric scaling is authored but outside the issue’s required simulation sizes.
