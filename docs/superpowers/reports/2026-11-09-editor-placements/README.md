# Editor placement round trip (#156)

Ashen Ford gains draggable markers for its existing interactables and five roaming patches. Shop labels use the existing People markers; moving one retains its keeper binding across canonical people sorting. Import preserves existing painting and refuses replacement of an existing marker group.

The native Godot check loads the production scene into temporary files, verifies unchanged interactable/mob/shop/world bytes after a no-op export, moves an interactable, shopkeeper and whole mob patch, exports through complete pack validation, then verifies the second export is byte-identical. Native scene save/load also preserves the moves. See `native-check.txt` and `--check-placement-markers` for reproduction. This is automated native persistence/export evidence, not a human editor usability test.

Focused tests additionally cover authored data preservation, full-patch translation/order, keeper-index changes, missing/duplicate/unknown IDs, and rejection of invalid coordinates before any production file changes. Full repository validation is required before merge.
