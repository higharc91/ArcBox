> Status: Ready for PR update on issue #203 (asset-rebuild-203): KeyError villains repaired, dependencies pinned, full cold-build measured (669.15s across all 32 sets), drift fully analyzed and documented, approved native artwork preserved, local checks passing.

# SDD ledger — issue #203: Pin asset-build dependencies, repair cold-build villains KeyError, investigate drift, and document cold-build time

Branch: `asset-rebuild-203` (worktree at `/home/antigravity/worktrees/issue-203`, based on `origin/asset-rebuild-203` merged with `origin/main`).

## Tasks

- [x] Task 1: Inspect `tools/tactics-assets/build.py`, `parts/icons.py`, and `parts/ability_icons.py` to identify why `KeyError: 'villains'` occurs during cold-build.
- [x] Task 2: Fix `villains.json` ability icon enumeration in generator (treat `villains.json` as enemy-only, skipping player class plates).
- [x] Task 3: Pin Python dependencies in `tools/requirements.txt` (`Pillow==12.3.0`, `numpy==2.3.5`) and verify installation.
- [x] Task 4: Reconcile authored/imported native assets and investigate source/content drift in `parts/` vs `content/tactics/base`, preserving approved native artwork.
- [x] Task 5: Execute full asset rebuild (`tools/tactics-assets/build.py`), measure and document successful cold-build time in `tools/tactics-assets/README.md`.
- [x] Task 6: Document complete drift table and preservation of approved native artwork (22 of 32 sets byte-identical, approved art preserved).
- [x] Task 7: Run local verification (`tools/check-all.sh --fast` and `.NET build ArcBox.slnx -c Release`), push `asset-rebuild-203`, and update draft PR #230.
