# Representative build sweep (#119)

`audit.md` records the content hash, all 144 subclass/level comparisons and 44 race/level comparisons. `audit.json` retains each cell’s complete configuration and aggregate metrics; `audit.battles.jsonl.gz` retains all 4,720 individual results. Coverage verified: 236 cells × 20 paired seeds, all 36 subclasses and 11 races at levels 3, 5, 7 and 10.

Reproduce from the repository root:

```bash
source /workspace/.arcbox/activate.sh
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --build-audit --seeds 20 --out /tmp/build-audit/audit.md
```

Each subclass changes one focus hero against its base-class control, with fixed teammates. Race comparisons change all four heroes against Human using identical classes, subclasses and seeds. Spawn seats rotate. Ability points are spent by class priority with the normal cap of 20; no consumables are used. Mixed-race parties are separately supported with `--races class:race,...`.

Subclass role changes beyond ±25% and race win-rate changes beyond ±5 percentage points are explicitly flagged in the table. These are documented observations for the existing balance issues #118/#120/#121/#125, not balance changes. Twenty seeds and four benchmark encounters cannot certify all party compositions; the level 10 encounter’s ceiling particularly limits discrimination. A zero role baseline is labelled a new capability rather than divided by zero.

Validation: six focused build-audit regression cases pass, covering increases at levels 3/4/8/10, mixed races, explicit parties and complete scenario coverage. The full repository gate is required before merge.
