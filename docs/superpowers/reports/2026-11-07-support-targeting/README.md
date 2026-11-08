# Support targeting: issues #116 and #117

Healing and rally effects now use the intended recipients. Six rally abilities use an allied burst centred on the caster. Mass Cure Wounds, Verdant Bloom and Sanctuary Grove keep their tile aim and filter their immediate heals to allies. Sanctuary Grove and Consecrated Ground filter zone passives to the zone owner's side. Vampiric Touch and Cursed Slash heal the caster only on a hit; the heal comes before damage inside that hit branch, so a final killing blow cannot end the battle before it pays out.

The shared effect data supports `self: true` on heal/temporary HP and an optional `affects` side filter on areas and zones. Old JSON omits these fields and retains its existing behavior. Zone filters survive save/load and partial zone removal. AI scoring and previews attribute caster healing separately from damage to the victim, and zone evaluation respects the same side filters. The loader rejects unambiguous beneficial effects directed at hostile sides, including nested branches and healing-zone passives. The rules spec documents the authoring contract.

## Paired battle simulations

The baseline is main at `9c7e1ab103f3126a86aa27e51c42f8e5f7053912`. Each side uses the same AI party sampler, human races, party sizes 4/5/6, six sampled parties per size, seeds 1–5, Normal difficulty and a 30-round cap. Each encounter has 90 battles per version (30 per party size): 360 before and 360 after. These are small deterministic comparisons of the combined fixes, not estimates of human success or a complete balance gate. The sampler uses default class builds; subclass/race coverage remains tracked in #119.

| Encounter | Level | Before won | After won | Before win rate | After win rate |
|---|---:|---:|---:|---:|---:|
| [Ford Assault](after/ford_assault.md) | 1 | 62/90 | 60/90 | 68.9% | 66.7% |
| [Forest Ruins](after/forest_ruins.md) | 3 | 71/90 | 70/90 | 78.9% | 77.8% |
| [Warlord's Bridge](after/warlord_bridge.md) | 5 | 82/90 | 88/90 | 91.1% | 97.8% |
| [Hall of the Flame](after/hall_of_the_flame.md) | 10 | 59/90 | 55/90 | 65.6% | 61.1% |

| Encounter | Size | Before | After |
|---|---:|---:|---:|
| Ford Assault | 4 | 63.3% | 56.7% |
| Ford Assault | 5 | 53.3% | 56.7% |
| Ford Assault | 6 | 90.0% | 86.7% |
| Forest Ruins | 4 | 50.0% | 46.7% |
| Forest Ruins | 5 | 86.7% | 86.7% |
| Forest Ruins | 6 | 100.0% | 100.0% |
| Warlord's Bridge | 4 | 83.3% | 100.0% |
| Warlord's Bridge | 5 | 90.0% | 93.3% |
| Warlord's Bridge | 6 | 100.0% | 100.0% |
| Hall of the Flame | 4 | 56.7% | 56.7% |
| Hall of the Flame | 5 | 70.0% | 66.7% |
| Hall of the Flame | 6 | 70.0% | 60.0% |

Warlord's Bridge exceeds the aggregate 95% upper target after the fixes; its timer-win redesign remains #135. Four-player Ford Assault, Forest Ruins and Hall of the Flame remain below the 60% lower target in this sample. Ford fairness is #129 and party-size scaling is #130. These observations are retained for those encounter-balance issues; this change corrects support targeting without adjusting encounter numbers.

Full generated reports, including rounds, caps, role bands and ability use: [before](before/) and [after](after/).

Reproduce each run with the corresponding source/rules checkout:

```sh
source /workspace/.arcbox/activate.sh # This container's toolchain; use your own .NET SDK elsewhere.
dotnet build games/Tactics/Tactics.Sim -c Release
for item in ford_assault:1 forest_ruins:3 warlord_bridge:5 hall_of_the_flame:10; do
    encounter=${item%:*}
    level=${item#*:}
    dotnet games/Tactics/Tactics.Sim/bin/Release/net10.0/Tactics.Sim.dll \
        --encounters "$encounter" --levels "$level" --sizes 4,5,6 \
        --parties 6 --seeds 5 --out "/tmp/support-targeting/$encounter.md"
done
```

## Regression evidence

- `SupportTargetingTests`: six named rallies, both drains on hit/miss (including lethal hits), three tile heals, both healing zones through full turn cycles and state serialization, hit-weighted caster previews, AI drain selection, source-only temporary HP, and default/all/allied/enemy zone scopes through partial removal.
- `SupportTargetValidationTests`: rejects hostile buffs, heals nested under attacks/repeats, unfiltered healing zones and misspelled filters; accepts allied tile heals, caster-only heals and hostile debuffs.
- The Warlord's Bridge golden is re-recorded from the AI with its original seed 2. Its added assertion requires every Warcry recipient to be on the enemy side. All seven replay tests pass, including deterministic save/load and the original archers, collapse and boss-phase assertions. Other golden files remain byte-identical.
