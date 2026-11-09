# Mob approach entries (#139)

All seven arenas provide authored dry entry strips selected by Manhattan distance from the party’s contact in arena coordinates, with stable name ties. A narrow entrance uses two columns/rows so six heroes remain cardinally connected on the same edge. Walls and water do not become entry edges. The chosen zone is saved for reload and rules-change restart; old saves and non-mob encounters retain the original entry behavior.

Pine Wolves opens fence cells (9,9) and (10,10); Rooting Boars opens (11,5). All other scenery, terrain, monsters, stats and abilities remain authored as before. Bats’ default entry now keeps all six heroes on the west entrance; River Slimes’ default entry is dry. The machine-readable entry inventory records 0 unreachable walkable cells in each arena; regression checks independently use engine reachability.

Validation: 14 arena cases cover every entry, one-to-six hero setup, dry/connected spawns, reachability and nearest-contact selection. All 22 mob runtime cases pass, including seven real saved contacts and reloads. Fresh native Godot Debug build: zero warnings. `--tactics-demo=mob-entries --shots=<folder>` renders all 22 entry groups with six real encounter heroes. Four representative 1920×1080 screenshots are retained here. Automated renderer review uses software OpenGL/Dummy audio, not a physical TV or human playtest. The native log only reports the known software-renderer VSync/AA limitations; no script errors.

The full repository gate is required before merge.
