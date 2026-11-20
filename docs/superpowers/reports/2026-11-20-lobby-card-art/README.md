# Lobby card art (#405)

The TV lobby at 1280×720, from `tools/tv-e2e.sh` with Meridian selected (Godot 4.7.2 on Xvfb, low preset).

| File | What it shows |
|---|---|
| `without-art.png` | The lobby before #405, with text cards only. Every game without `CardArt` still looks like this |
| `with-art.png` | Meridian's card with its UI-kit logo as a faded emblem behind the text, at 30% opacity and 2× scale |
| `cards-before-after.png` | The card row before (top) and after (bottom). The cards keep their size, and every player count still shows |

The first try put the art above the game's name. That made the card row taller, and at 720p it cut off every card's
player count. So the art now sits behind the text, on a layer with no layout that is clipped to the card. At 50%
opacity the emblem made Meridian's description harder to read, so it's drawn at 30%. How it works is in
[`docs/tv-host.md`](../../../tv-host.md#card-art-in-the-lobby-405).
