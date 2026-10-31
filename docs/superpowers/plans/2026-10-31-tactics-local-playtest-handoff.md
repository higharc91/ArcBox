# Handoff: playtest ArcBox Tactics on a PC and a real phone

You're a local Claude Code session on the user's PC, in their clone of `higharc91/arcbox`. A cloud session built ArcBox Tactics' battle adapter (milestone 3a) and character portal (sub-project 4a), and played them in headless Chromium. Nobody has tried them on a real phone yet. Your job is to help the user do that, fix what's small, and write down what isn't.

## 1. Get the code

```sh
git fetch origin ccr-18ad4510-q68nad
git checkout ccr-18ad4510-q68nad
```

This branch is stacked on the rules-engine branch `ccr-472d8660-sxpjlj`, and both merge cleanly with `main`. Work and push on `ccr-18ad4510-q68nad`. Don't merge to `main` or open a PR unless the user asks.

Read these first. They're short and say what exists and why:
- `docs/dev-playtest.md`: running the server, the dev page, and phones on the LAN. Includes a Tactics section.
- `docs/superpowers/reports/2026-10-31-tactics-playtest.md`: the headless playtest's findings and screenshots.
- `docs/superpowers/specs/2026-10-31-tactics-battle-adapter-design.md`: the battle flow, phases, timers and phone screens.
- `docs/superpowers/specs/2026-10-31-tactics-characters-design.md`: the portal's rules.

## 2. Prerequisites (check, don't assume)

- **.NET 10 SDK:** `global.json` pins it, so run `dotnet --version`.
- **Node 22 and npm**, for `web/play`.
- **Phone and PC on the same Wi-Fi.** Windows asks to allow `dotnet` through the firewall the first time it listens. Say yes for private networks, or allow inbound TCP 5080.

Before the user touches anything, confirm the build is green:

```sh
dotnet build ArcBox.slnx              # expect 0 warnings
dotnet test ArcBox.slnx               # expect 1,909 passing
cd web/play && npm ci && npx vitest run && npm run build && cd ../..
```

If anything fails here, stop and tell the user before playtesting. A failure on their machine that CI didn't show (path separators, line endings, culture) is itself a finding.

## 3. Run it

```sh
dotnet run --project src/ArcBox.Server --urls http://0.0.0.0:5080
```

Find the PC's LAN address (`ipconfig` on Windows) and tell the user both URLs:
- **On the PC:** `http://localhost:5080/dev.html?phones=3` (the dev page stands in for the TV, with 3 phone iframes).
- **On the phone:** `http://<pc-ip>:5080/?room=<CODE>`, where `<CODE>` is the room code the dev page shows.

**Any number of players from 1 works.** AI companions now fill a party up to the encounter's minimum of 4. For the turn screen, though, more human players means more turns to try.

## 4. What the user should try (in this order)

1. **Lobby → campaign.**
   - On the dev page, pick **Tactics**.
   - The VIP (the iframe `P1`) taps **New campaign** and names it.
   - The real phone and the other iframes each tap **I'm new here** twice. The first tap opens an optional name field and the second confirms.
   - The VIP taps **Everybody's in**.
2. **Muster.** Each phone picks a class. The real phone should pick a caster (wizard or cleric) so area spells get tried.
3. **Battle pick.** The VIP picks **River Crossing**.
4. **The turn screen, on the real phone. This matters most.** Ask the user about each of these:
   - Can they see Move here / Delay / End turn without scrolling?
   - Is the map readable, with terrain (a blue river, grey rock)?
   - Is the d-pad comfortable?
   - Can they walk somewhere?
   - Can they attack a goblin (Attack → target → Use)?
   - Can they cast an **area spell** at a tile picked on the map? This was never tried by a person.
   - Does the 60 s countdown feel right?
   - When the iframe players don't act, their turns time out after 60 s, and the AI finishes them, marked "AI finished". You can end those turns on the dev page instead.
5. **A reaction prompt** (Shield, or an opportunity attack), if one comes up: is the 10 s window long enough?
6. **Results.** The VIP taps **End session**.
7. **The portal on the real phone.**
   - Open `http://<pc-ip>:5080/c/<campaign-code>`.
   - Find the campaign code with `GET /api/me`, sending the phone's profile key as `Authorization: Profile <key>` (the key is in the phone's `localStorage` under `arcbox:profiles`). It may also show in the lobby's campaign picker.
   - Make a character: point-buy, race, class and background. Then start a new session and check the real phone plays that character, not a class pick.
8. **Reconnects.**
   - Lock the phone mid-turn and unlock it. It should rejoin, and the AI should have finished its turn.
   - Restart the server mid-battle. The room should resume in "Resuming…", and the battle continue where it was.

Also worth a look: **Wake Lock** only works over HTTPS or `localhost` (see `docs/dev-playtest.md`). Over plain LAN HTTP the phone may dim mid-battle.

## 5. Working rules

- **Fix small, clear UI problems on the spot**, such as layout, wording or tap targets in `web/play/src/games/tactics/*`. Then:
  - rebuild (`npm run build`; the server serves `web/play/dist` from disk, so a reload picks it up);
  - run `npx vitest run`, and `dotnet test` for C# changes;
  - commit with a clear message.
- **Fixture contract.** If you change what the server sends to the phone, regenerate the fixtures (`UPDATE_FIXTURES=1 dotnet test games/Tactics/Tactics.Game.Tests --filter PhoneFixtureTests`) and check the diff.
- **Don't change game rules or balance numbers without the user's say-so.** Several balance decisions are waiting on them; they're listed in `docs/superpowers/plans/2026-10-30-tactics-rules-engine.progress.md` under "Decisions for the user".
- **Write the playtest up** in `docs/superpowers/reports/<date>-tactics-phone-playtest.md`:
  - what worked;
  - what you fixed (with commits);
  - what's left, ranked by how much it hurt play.

  Phone screenshots from the user help.
- Push to `ccr-18ad4510-q68nad` when done.

## 6. Known limits, so they aren't reported as surprises

- **There's no real TV app yet.** The dev page board is a stand-in. The Godot scene is milestone 3b, planned in `docs/superpowers/plans/2026-10-31-tactics-tv-scene.md`. If the user has Godot 4.7 .NET installed, that's a natural next task, but only once the phone flow is solid.
- **Characters from Muster** are human, with default ability scores. Custom ones come from the portal.
- **No feats yet**, and no exploration between battles (sub-project 5).
- **Level-1 parties often lose River Crossing** when played carelessly. A defeat isn't a bug, but tell the user if it feels unfair.
