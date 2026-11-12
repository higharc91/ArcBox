# Monster roster and player forms (#127)

Seven existing bestiary creatures now have buildable stat blocks and signature actions using their existing art. Every authored enemy, including size-specific variants, has a usable ability or a passive with an actual hook and effects. Rank-and-file moves use existing effects, conditions and animations. No generated assets or voiced lines change.

Player transformations use `form_wolf`, `form_bear`, `form_dire_wolf`, and `form_elder_bear`; Conjure Animals uses `summon_wolf`. Each defines HP explicitly at every level through 10. The original monster IDs retain their stat blocks for compatibility with old content and saved states. The form curves preserve unlock-level HP (wolf 14 at 2, bear 26 at 4, dire wolf 32 and elder bear 44 at 6), then grow by 3 HP per level. Summon wolves grow by 2 per level. No encounter can reference a player form in the coverage test.

Challenge rating remains metadata, validated as an integer or supported fraction, with nonnegative monster XP. Encounter XP remains separately authored. Negative-loader tests enforce both metadata rules.

Validation: 12 roster/legality/negative-loader tests, 14 base-pack tests, and 9 caster tests pass with no skips; build has zero warnings. Frozen paired simulations completed 1,200 fights before and 1,200 after, with identical aggregate win-rate rows for the default four-class party in six Ford/Hollow encounters at levels 1/2. See paired-summary.json and balance-before/after.md; this does not assert identical battle states or all-party balance. Global full-check failures on main remain a merge blocker.
