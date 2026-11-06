> Status: Completed issue #146 (level-up-choices-146): in-session CharacterRules choices, level-up celebration, phone and TV views, and tests verified.

# SDD ledger — issue #146: In-session subclass/ability-increase/ability-pick choices & level-up celebration

Branch: `level-up-choices-146` (worktree at `/home/antigravity/worktrees/issue-146`, based on `origin/main`).

## Tasks

- [x] Task 1: Accept `chooseSubclass`, `spendIncrease`, and `setPicks` session inputs in `TacticsGame.cs` outside battle reusing `CharacterRules`. Refuse changes during `TacticsPhase.Battle` with a toast.
- [x] Task 2: Update `TacticsGame.Views.cs` to supply character choice options (`CharacterChoices`), level-up indicators, loot/gold, and hero XP progress metrics.
- [x] Task 3: Update `PhaseScreen.cs` to display `level_up_anim` banner and level-up notice on TV Results when the party leveled up.
- [x] Task 4: Update web/play phone components (`types.ts`, `screens.tsx`, `walk.tsx`, `choices.tsx`):
  - Add Level-up card and pending choices card component.
  - Show "1 choice waiting" (or N choices waiting) badge on Results, waiting/muster, and walk screens.
  - Show XP progress bar (`current/nextLevel XP`) per hero on Results, plus loot and gold lines.
  - Prompt returning players with choices at Muster.
  - Reword Results link ("Your character") and align button copy.
- [x] Task 5: Add comprehensive .NET and web tests covering:
  - Mid-session subclass choice at level 3 without ending session.
  - In-session `chooseSubclass`, `spendIncrease`, `setPicks` validation and state updates.
  - Rejection of choices during battle.
  - Returning absent player prompted at Muster.
  - Results screen XP progress, loot, gold, and level-up banner.
- [x] Task 6: Run local gate validation (`tools/check-all.sh --full`), push `level-up-choices-146` to origin, and open draft PR.
