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
- [ ] **Grid:** five red lights go out; the strip shows the payouts and the credit line.
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

## After

- [ ] Save the server console log and the Godot log (`%APPDATA%\Godot\app_userdata\ArcBox\logs`) if anything failed.
- [ ] Note the frame-rate numbers and any visual glitch with a phone photo of the TV.
- [ ] Note the server's `Speech usage since start: … characters` line at shutdown (free tier: 1M characters a month).
