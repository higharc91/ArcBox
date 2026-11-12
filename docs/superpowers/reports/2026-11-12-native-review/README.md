# Native TV review of companion names and personal credits

Captured the real Godot renderer at 1920×1080, OpenGL compatibility, software Mesa and Dummy audio. The companion active card displays all 16 characters of “Brindle Ashthorn” with “Mara's hound · Party”; the turn banner also displays the full name. The compact turn-order tile uses its existing shortened surname presentation. The personal dialogue option displays “Soldier · Kara” alongside the real Ford truth option without clipping.

These use scripted review views, with the injection retained as demo.patch and removed from feature source after capture. They are layout checks, not a network privacy test or a physical-TV/human usability test. Secret packet privacy and persistent names are tested by backend tests. The battle log is an event stream, not a separately rendered TV log panel; no claim of a separate log-panel capture is made.

Reproduce: apply demo.patch, source /workspace/.arcbox/activate.sh, export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0, build src/ArcBox.Host with -c Debug -m:1 -nr:false. Start Xvfb :97 -screen 0 1920x1080x24, then run DISPLAY=:97 godot --path src/ArcBox.Host --rendering-method gl_compatibility --rendering-driver opengl3 --audio-driver Dummy -- --tactics-demo=named-companion --res=1920x1080 --shots=/tmp/companion-shots --quit-after=8. Repeat with --tactics-demo=personal-credit. Imported fonts/models are required (tools/godot-import.sh). Revert the injected demo after use.

The native Debug build has zero warnings. Godot logs retain its existing compatibility-renderer anti-alias warning and the absence of Linux TTS. No artwork or voice recordings were generated.
