# Wardenflame title screen and themed lobby

The start screen for the TV (`web/play/title.html`) is a 3D flyover of the Vale under the Wardenflame logo. The shell's
lobby and "Who are you?" screens take a Wardenflame look while a room has the game selected. Both are built in
three.js from the content pack alone; nothing here is hand-drawn.

## Running it

```bash
cd web/play && npm run build
dotnet run --project src/ArcBox.Server --urls http://0.0.0.0:5080
# then open http://<host>:5080/title.html?room=KQZT
```

No server change is needed. In Development, `Web:Root` points at `web/play/dist`, so `/title.html` is served there
like `index.html`, and the art comes from the existing `/content/tactics/...` endpoints. Outside Development the built
page has to be in `wwwroot` with the rest of the phone app.

`title.html` takes these query parameters:

| Parameter | Effect |
|---|---|
| `room=ABCD` | Shows "Join at &lt;host&gt; with code ABCD" in the bottom band |
| `host=…` | The address to show instead of the page's own, for when the TV opened it as localhost |
| `t=<seconds>` | Freezes the tour at that moment, for screenshots |
| `gleam=<frame>` | Freezes the logo gleam on that frame |

## How the flyover works (`web/play/src/games/tactics/world/`)

| File | Does |
|---|---|
| `assets.ts` | Loads the manifests (through `pack.ts`), sets textures up for pixel art (nearest filtering, no mipmaps, colours exactly as drawn), cuts animation strips into frames, finds glow masks by hue, and combs the vertical streaks out of the mountain ranges |
| `terrain.ts` | Says how a map material looks as a tile column: its top, cliff side and overhang, and whether it glows, flows (water) or is a chasm (`void`). It covers the terrain and dungeon sets |
| `areas.ts` | The tour: one battle map per region in story order, each with its sky, mountain ranges, colour grade, lighting, set dressing and title-screen retexturing |
| `build.ts` | Builds one area as a scene: a box per tile column, with only its visible faces, textured world-aligned; props and dressing as camera-facing billboards; point lights from the props' `light` data and glowing ground, flickering on the manifest's curves; and a screen-space sky with two parallax ranges and clouds |
| `flyover.ts` | Renders about 270 lines (200 behind the phone lobby), scaled up by a whole number with nearest-neighbour. The world is lit from the screen's top-left. A post pass applies the pack's colour grade, a vignette and dimming. Each area flies in, holds in a slow drift for 16 s, then dissolves (the `dissolve` transition) into the next. The old scene is disposed before the next is built |

Water, lava, the void and wet stone are animated. Lava, wet stone, fel crypt tiles, flames and crystals glow and skip
lighting. The mountain-range PNGs draw their slope shading as one-pixel columns, which read as stripes when moving, so
`destreak()` replaces each pixel with the most common colour along its row. The lasting fix belongs in the
asset tools.

Title-screen art direction only: Mirewood draws its grass as mossy forest floor and its dirt as swamp mud, and the
Sunscar draws its grass, dirt and rock as sand, packed path and red sandstone. The battle maps are unchanged.

## The title screen (`web/play/src/title/`)

- The logo is drawn at a whole multiple of its pixels, and the `logo_gleam` strip plays every 5–8 s.
- The region caption shows the chapter numeral, the region's name from `lore/regions.json` in Cinzel, and the place.
  It sits top-left on a TV and under the logo on a phone.
- A navy-glass band holds a pulsing "Press start" and the join code at sofa size.
- `music.title` starts on the first key press or tap and fades in. A "♪ Sound on/off" pill sits top-right.
- With `prefers-reduced-motion` the screen shows a still frame: no tour, no gleam.

## The lobby theme hook

`GameModule.lobbyTheme` (`src/games/module.ts`) is optional. Every part of it is optional and only adds to the screen:

| Part | Does |
|---|---|
| `className` | Added to the screen's root; the game's CSS scopes to it |
| `Backdrop`, `Banner` | Drawn behind the screen and at its top |
| `CardBadge` | Decorates player chips and seat cards (`place`: chip, row, tile or hero) |
| `text` | Rewords the campaign step; labels it leaves out keep their defaults |

`useLobbyTheme(gameId)` (`src/games/registry.ts`) returns `undefined` for a game without a theme and while a themed
game's module is still loading. `Lobby.tsx` and `Pick.tsx` then render exactly their default DOM. The tests in
`test/lobbyTheme.test.tsx` check this by comparing the markup.

Wardenflame's theme (`src/games/tactics/lobby.tsx` and `lobby.css`):

- A darkened flyover behind the screens. It is one instance shared across the lobby and "Who are you?", paused while
  the page is hidden, and a still frame under reduced motion.
- The logo, and navy glass and gold panels in the module's fonts.
- Class crests on the player chips and class portraits on the seat cards, both read from the campaign's seat cards.
- The p1–p6 player colours.
- The campaign step told as "The party's chronicle": each campaign a volume with a ribbon, "5 heroes · last told 6 days
  ago".

The room-code Join screen comes before any game is known, so it stays generic. A phone opened with `?room=` for a
Wardenflame room goes straight to the themed "Who are you?" screen, because the room's peek names the game.

## Screenshots

TV shots are 1920×1080 and phone shots 390×844. The phone lobby shots render the real `Lobby` and `Pick` components
with fixture state, because the server couldn't run here.

| Phase | Files |
|---|---|
| 1. Prototype | `p1-flyover-ford.png`, `p1-flyover-drift-left.png`, `p1-flyover-drift-right.png`, `p1-flyover-live.png`, `p1-flyover-phone.png` |
| 2. The world | `p2-ford-join-code.png`, `p2-mirewood.png`, `p2-sunscar.png`, `p2-frostpeak.png`, `p2-ember-keep-gate.png`, `p2-hall-of-the-flame.png`, `p2-dissolve-ford-to-mirewood.png`, `p2-fly-in-ember-keep.png`, `p2-logo-gleam.png`, `p2-reduced-motion-still.png`, `p2-live-after-transition.png`, `p2-phone-390.png` |
| 3. Phone lobby | `p3-lobby-wardenflame.png`, `p3-chronicle-existing.png`, `p3-chronicle-new.png`, `p3-who-are-you.png`, `p3-lobby-claim-and-waiting.png`, `p3-reduced-motion-still.png`, `p3-other-game-redline.png`, `p3-title-phone-390.png` |

## Checks

`cd web/play && npx vitest run` passes 273 tests, including `test/tacticsWorld.test.ts` and
`test/lobbyTheme.test.tsx`. `npm run build` is clean. three.js (`three@0.185.1`, pinned) loads in its own chunk only
when a flyover is shown.
