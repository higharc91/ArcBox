# Tactics art storage proposal (#193)

Measured from tracked files on `origin/main` before this change. Folder totals can overlap: `docs/` media is a subset of all `docs/`. MiB uses 1,048,576 bytes.

| Tracked folder | Files | Bytes | MiB |
|---|---:|---:|---:|
| `tools/tactics-assets/concepts/` | 296 | 201,036,379 | 191.7 |
| `tools/tactics-assets/imagegen/` | 4,182 | 461,095,815 | 439.7 |
| `tools/tactics-assets/previews/` | 141 | 101,580,610 | 96.9 |
| `docs/` media (images, audio, video, PDF, SVG) | 890 | 754,431,630 | 719.5 |
| `docs/` all files | 1,659 | 778,555,264 | 742.5 |

The local Git pack is 1.92 GiB (`git count-objects -vH`). This branch deletes no tracked art, so the tracked-folder and Git-history sizes are unchanged before and after. It stops ordinary asset builds from generating new review files and ignores newly generated previews. The 141 tracked previews remain, including sheets linked from the asset README. Concept and provenance art remains in `tools/tactics-assets/concepts/` and `tools/tactics-assets/imagegen/`.

## Decision for a later migration

- **Git LFS:** Keeps asset paths and versioning in Git while storing future binary contents in LFS. It adds hosting storage and bandwidth costs, and normal LFS checkout still downloads the binaries. To make an existing clone materially smaller, old binary commits must also be migrated with a coordinated history rewrite; simply adding LFS tracking leaves the current 1.92 GiB Git pack.
- **External object store:** Keep a versioned manifest with URLs, hashes, licenses, and provenance in Git; download selected concept and review files on demand. This can avoid routine clone and LFS transfer costs but needs durable hosting, access control, and a fetch/verification tool. Existing history still contains the art unless a coordinated rewrite removes it.

Choose a fresh-clone byte budget and which art must be available offline before migrating. Either option needs a separate plan for existing history, collaborators' clones, and links in docs. This branch makes no LFS or history change.
