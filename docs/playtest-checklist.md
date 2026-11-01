# First playtest checklist

A manual pass for the first real game night: the TV app on the RTX 5080 PC, real iPhones and Android phones on the
LAN. It covers what the automated tests can't (platform spec §7) and maps to the success criteria in platform spec §1
and REDLINE spec §1 (numbers in brackets: **P** = platform, **R** = REDLINE). Tick each box, and note anything odd with
the time, so it can be found in the logs.

## Before everyone arrives

- [ ] Build: `cd web/play && npm ci && npm run build`, then `dotnet build ArcBox.slnx`.
- [ ] Start the server reachable from the LAN: `dotnet run --project src/ArcBox.Server --urls http://0.0.0.0:5080`.
      Allow TCP 5080 through the Windows firewall (private networks) when asked.
- [ ] Questions: `dotnet run` uses Development, which turns OpenTDB off (bundled pack only). For the live-question race
      add `--Redline:OpenTdb:Enabled=true` to the server command line, and check the Grid strip credits Open Trivia DB.
      One race later in the evening runs with the PC's internet unplugged (R2 below).
- [ ] Open `src/ArcBox.Host/project.godot` in Godot 4.7.2 .NET (or run an export) at 2560×1440 fullscreen.
      Connect to `http://<pc-ip>:5080`; the lobby shows the room code and the QR.
- [ ] **Frame rate [R6]:** start a 2-player race from two phones (or the dev page), press **F3** and note fps and ms on
      the Ultra preset at 2560×1440 for: the grid, a helicopter shot of the pack in Reveal, the trackside follow-up, and
      the Chequered podium. Target: 144 fps (≤ 6.9 ms). Press **F2** to check High and Low too, and write the numbers into
      `docs/redline-tv.md`. If Ultra misses 144, note which shot drops most.
- [ ] Press **Esc** to end that race; the room returns to the lobby.
- [ ] **Voice:** with a Google key set up ([`speech-setup.md`](speech-setup.md)), restart the server, start a race and
      press **F3**: after the first line it reads `VOICE GOOGLE`. Without a key it reads `VOICE FALLBACK` (Windows'
      built-in voice). Set the TV's volume so the commentator is comfortably loud from the sofa.

## Joining

- [ ] Scan the QR from an **iPhone** (Safari) and an **Android** phone (Chrome): both open the join page with the code
      filled in [P1].
- [ ] Type the code by hand on a third phone.
- [ ] A 12-character name fits on the TV lobby, the timing tower and the name tags.
- [ ] The VIP badge is on the first player; the TV picks REDLINE with ←/→ and starts with Enter (or the VIP starts it).

## The race

- [ ] **Driver Select:** every phone can claim a different driver; a taken driver is greyed on the others; the TV
      board shows the player in the claimed seat and the line-up camera holds on the new claim.
- [ ] **Qualifying:** three easy questions before the Grid. Each phone shows "QUALIFYING · LAP n/3", the question
      while the TV reads it out, then the four answers. After a tap it shows "Lap time 2.41 s — checking…". At the
      result it shows a green "✔ 2.41 s" banner (with "Purple!" for the lap's fastest) or a red "✖ Lap deleted"
      banner, with the right buzz and sound. The TV tower lists the times, with purple for the fastest lap and red for
      DELETED, and shows the provisional pole and pole position cards. The commentator opens with "Qualifying! …" and
      closes with "Pole position for …".
- [ ] **Grid:** the cars line up in the qualifying order and each phone adds "Qualified P<n>"; five red lights go out;
      the strip shows the payouts and the credit line.
- [ ] **Throttle:** the phone shows only the category and difficulty; the tower says LOCKED / CHOOSING; nobody's
      choice shows on the TV before Question [R3].
- [ ] **Question:** answers on the phones match the TV; the tower shows a tick for each player who answered, never which
      answer [R3].
- [ ] **Reveal:** the right answer goes green, the cars move the right amount, a REDLINE spin-out spins and smokes;
      the camera cuts to the follow-up after about 3.5 s.
- [ ] **Every car identifiable [R7]:** each player car is either on screen with its name tag or on the track map, in
      every shot; player cars stand out from AI (yellow names, bigger map dots with a ring).
- [ ] **AI [R8]:** unclaimed seats race; no phase ever waits for an AI.
- [ ] One race takes roughly 8–12 minutes.
- [ ] **Chequered [R1]:** podium, classification and awards on the TV; each phone shows its finishing place.
- [ ] **Podium:** the top three players' phones show the champagne bottle (everyone else "Watch the podium!"); the TV
      shows the podium set with their names, cars and team colours, the anthem caption, then "CHAMPAGNE!" with confetti.
- [ ] **Shaking on iPhone:** motion needs HTTPS. Over plain `http://<pc-ip>:5080` the bottle says "Tap the bottle fast
      to shake it" (the tap fallback); behind HTTPS (e.g. a tunnel or a local certificate) "Tap to enable shaking"
      asks for motion permission, and shaking fills the bottle. Deny it once: the tap fallback still works.
- [ ] **Shaking on Android (Chrome):** shaking fills the bottle without a prompt (HTTPS needed here too); the gauge on
      the TV follows within about a second.
- [ ] **Champagne:** POP! (or a flick) fires; the TV sprays from that player's step, bigger and longer the fuller the
      bottle; shaking again reloads it; all three can spray at once.
- [ ] **Results [P1, R9]:** the results list exactly the human players in race order; the room returns to the lobby and a
      second game can start.

## Listening (audio spec §5: the sandbox has no sound card, so these are only checked here)

- [ ] **Voice clarity:** every question is read in full, clearly, before the answers open on the phones; the reading
      never gets cut off by the answer clock in a way that loses the question [audio 1]. Note any question that ran
      long (the Reading time is an estimate from the word count).
- [ ] **Built-in fallback [audio 2]:** stop Google mid-race (unplug the PC's internet, or remove the key and restart
      the server): within a line or two the TV switches to the built-in voice (F3 `VOICE FALLBACK`), the race never
      waits, and after reconnecting it goes back to `VOICE GOOGLE` within about a minute.
- [ ] **Commentary timing [audio 3]:** the commentator uses players' names and pronounces them sensibly (an all-caps
      name is read as a word); at most two lines per Reveal; it never talks over the question; nothing is said so late
      it describes an earlier moment.
- [ ] **Ducking:** music and effects dip while a voice speaks and come back smoothly afterwards, with no pumping on
      short lines.
- [ ] **Mix:** start beeps line up with the gantry lights; lights-out roar; engines are a quiet bed while players
      think and roar in the Reveal drive; spin-outs screech and crunch; the correct sting / wrong buzz are clear but not
      harsh; the fanfare and crowd at the flag; the lobby loop and race bed loop without a click or gap. Nothing clips
      at the volume you set.
- [ ] **Anthems:** listen to each anthem with a melody (God Save the King, Advance Australia Fair, Het Wilhelmus, La
      Marseillaise, Il Canto degli Italiani, Marcha Real, O Canada, Das Lied der Deutschen, Maamme): the tune is right
      and recognisable, the brass sounds like a band, the commentator's line comes first and the band does not talk
      over it badly, and the victory theme follows cleanly. A Monaco, Mexico or Brazil winner gets the theme alone.
      Quickest: `--redline-demo=anthem` with `--redline-demo-winner=<code>` for a player car in the demo (LEC, NOR,
      VER, ANT, ALO, PER, HUL), or win a race with the right driver.
- [ ] **Champagne sounds:** each pop is a crisp cork with a fizz behind it, the spray hiss follows the strength, the
      first spray has the brass sting, and the victory theme loops without a gap.
- [ ] **M** mutes everything (toast "SOUND OFF"), including the built-in voice, and the setting survives a restart.
- [ ] **Phone haptics on Android:** a tap on every input, a double pulse for a correct answer, a long buzz for a wrong
      one, a stutter for a spin-out; short sounds play after the first tap on Join; the speaker toggle in the status
      bar silences both and is remembered.
- [ ] **iPhone silent switch:** iOS has no vibration for web pages, so only the sounds play; with the ring/silent
      switch on silent, note whether they still play (Safari plays Web Audio through the silent switch on some iOS
      versions); the speaker toggle mutes them either way.

## Robustness

- [ ] **Lock a phone for 60 s** mid-round, unlock: the player is back on the right screen and their car kept racing
      [P2].
- [ ] Turn a phone's Wi-Fi off for 20 s and back on: same.
- [ ] Reload a phone's browser tab mid-question: it rejoins on the current view.
- [ ] **Force-quit the TV app** mid-round (Task Manager, or close the window) and relaunch it: it resumes the same room
      and redraws the current phase; the phones never noticed [P3].
- [ ] Stop and restart the server mid-round: the TV shows "reconnecting", then opens a new room (the old room is gone;
      that's expected), and the phones show the room closed.
- [ ] **Offline questions [R2]:** with the PC's internet off (and OpenTDB enabled), a race starts and finishes on the
      fallback questions.
- [ ] Remote player (optional): one player joins over a Discord screen share of the TV, with their own phone on the
      server's public address or a tunnel; nothing in the game rewards reaction speed.

## Tactics battle on the TV

Ticked boxes were proved on real battles with the TV and bots
(`docs/superpowers/reports/2026-10-31-tactics-tv-scene/task-5-*.png` and `task-6-*.png`). Boxes marked **(person)** only a person can judge,
so tick those on the night. To play alongside the bots, see "With your own phone" in `tools/tactics-playtest/bots.cjs`.

- [x] **Turn order:** faces in order with the active unit first, bigger, with its NOW tab, and it moves on with each turn.
- [x] **Round:** the round and whose turn it is follow the beat on screen, not the game ahead of it.
- [x] **Falling out:** a unit that's Out or Dead leaves the strip; a Downed one stays, dimmed; a long order shows "+n".
- [x] **AI badge:** a character whose player has gone shows the blue AI badge on the strip; enemies don't.
- [x] **Objectives:** `warlord_bridge` reads "Defeat the warlord, or survive 11 rounds" and `river_crossing` reads "Defeat every
      enemy". Those two were seen on the TV.
- [ ] **(person)** `forest_ruins`, `crypt_hall` and `crystal_cave` read "Defeat every enemy" too. Same objective in their files,
      not yet seen on a TV.
- [x] **AI finished:** a player sits out their turn; after 60 s the AI finishes it, with the "AI finished" badge over that unit.
- [x] **Reaction prompt:** "<name> can react" comes up as the beat that raised it plays, and goes when it's answered or timed
      out. Seen timing out; an answered one wasn't seen on the TV.
- [x] **TV killed mid-battle:** relaunch it and it's back in the same room, on the board as it stands, playing only the newest
      beat, and it goes on to the Results (`ARCBOX_GAME=tactics ARCBOX_RECONNECT=1 tools/tv-e2e.sh`).
- [x] **Server restarted mid-battle:** the TV shows "Resuming the battle…", the phones come back, and the battle carries on to
      the Results (`ARCBOX_GAME=tactics ARCBOX_RESTART=1 tools/tv-e2e.sh`). Bots rejoined within a second, so the screen was only up briefly.
- [x] **An area spell:** seen once on a real TV, a level 1 wizard's Sleep on two goblins (`bots.cjs --fight --area`). It's a
      save spell: no fireball-sized hit was seen.
- [ ] **(person)** A TV too slow to keep up (over 8 s behind) drops to the newest beat and stays near the game. Tested, not seen
      on a real slow machine.
- [ ] **(person)** The strip, round and objective can be read from the sofa on the real TV, names under the faces included.
- [ ] **(person)** The "AI finished" badge stays up long enough to notice, and isn't mistaken for the strip's AI badge.
- [ ] **(person)** A reaction prompt is noticed by the room in its 10 s, and "Choosing on their phone" tells the others to wait.
- [ ] **(person)** On a map with a raised back corner, a unit behind a panel can still be followed (the panel fades; a
      prompt moves down the left side instead).
- [ ] **(person)** The HUD feels part of the same picture as the board at 2560×1440, not laid over it.
- [ ] **(person)** Music: each loop runs seamlessly, the crossfades between Muster, the encounter list and the fight sound right, and the boss and finale tracks suit their fights. The logs prove what played, not how it sounds.
- [ ] **(person)** The VICTORY and DEFEAT stings land as the banner comes up, and the step into camp music afterwards feels natural.
- [ ] **(person)** The mix: music, ambience, effects and voice balance on the real TV, ambience sits under the rest, and the voice ducking is enough for a line to be understood from the sofa.
- [ ] **(person)** Effects match what is on screen: swings land with the blow, footsteps suit the ground, no sound is too loud or repeats annoyingly. Turn ticks and footsteps are the likeliest to wear thin over a long battle.
- [ ] **(person)** A weapon or creature sounds right for what it is. Weapons come from the unit's art preset, so a player's real gear may differ.
- [ ] **(person)** Recorded lines on `warlord_bridge`: each voice suits its speaker, lines never overlap, and captions keep up with the voice when two lines share a beat.
- [ ] **(person)** The VICTORY and DEFEAT strips read well on the Results and the logo reads well in the header at 2560×1440, and the Results rows are still comfortable with the banner above them.
- [ ] **(person)** Player colours on the turn-order frames read as each player's, next to the stripes.
- [ ] **(person)** Light: `crypt_hall`, `crystal_cave` and `hall_of_the_flame` feel lit and not just dim from the sofa, the far end of the crypt isn't too dark to follow a fight, and dark figures (skeletons, the wraith) still stand out there. `AmbientLift` and `FigureFloor` are the two numbers to change, both in `BoardLighting`.
- [ ] **(person)** Flicker: fire, crystals and lava breathe gently and out of step, never as a strobe. Stills can't show this.
- [ ] **(person)** Zones (ice, webs, poison) stay unlit on purpose, so they're clear; check they don't look pasted on in a dark room.
- [ ] **(person)** Halos and tile-sized pools look right on the real TV beside the mockups, without the recipe's banded, dithered steps.
- [ ] **(person)** A brazier's halo sits over a unit in front of it and over a big unit's ring. It's faint (it fades out about 21 art px from the flame, so a ring on the next tile is clear of it, and the dragon's wing in the demo showed only a slight tint). Check nobody's head looks washed out.
- [ ] **(person)** 130 of `crypt_hall`'s 168 tiles sit at flat ambient. Does a light's reach x sqrt 2 read better than more `AmbientLift`?
- [ ] **(person)** The wraith on a dark floor: can it be picked out from the sofa?
- [ ] **(person)** Join on a real phone over the LAN (`--people=1`): the QR opens the join page and the turn screen matches
      the TV. Checked here only in a browser on the PC, not on a phone.

## After

- [ ] Save the server console log and the Godot log (`%APPDATA%\Godot\app_userdata\ArcBox\logs`) if anything failed.
- [ ] Note the frame-rate numbers and any visual glitch with a phone photo of the TV.
- [ ] Note the server's `Speech usage since start: … characters` line at shutdown (free tier: 1M characters a month).
