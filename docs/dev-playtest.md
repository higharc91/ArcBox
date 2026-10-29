# Playtesting in a browser

Until the Godot TV app exists, the dev page (`/dev.html`) stands in for the TV: it hosts a room, shows the
REDLINE board (or a game's raw host view), and puts a few phones next to it in iframes. Real phones on your
network can join the same room.

## 1. Build the phone app and run the server

```sh
cd web/play
npm ci            # first time only
npm run build     # writes web/play/dist
cd ../..
dotnet run --project src/ArcBox.Server --urls http://0.0.0.0:5080
```

`dotnet run` uses the Development environment (from `launchSettings.json`), which:

- serves `web/play/dist` straight from disk (`Web:Root` in `appsettings.Development.json`), so after the
  next `npm run build` a browser reload picks it up, no server restart needed;
- turns OpenTDB off: REDLINE questions come from the bundled fallback pack.

If the page says "ArcBox server is running. Phone app arrives in M2.", the build is missing: the server
fell back to `wwwroot` and logged a `Web:Root … does not exist` warning. Run `npm run build`.

Use `--urls http://localhost:5080` instead of `0.0.0.0` if you don't want the server reachable from
the network.

For live-reloading phone UI work, `npm run dev` in `web/play` serves the app on http://localhost:5173 and
proxies `/hub` to the server on port 5080.

## 2. Open the dev page

http://localhost:5080/dev.html?phones=4

- `phones=N` (1–8, default 4) is how many phone iframes to show. They join as `P1`…`PN`; `P1` is the VIP.
- Pick a game and press **Start** on the host panel, or use the VIP phone's picker and "Everybody's in".
- Each phone has a **reload** button: it reloads that iframe, which should rejoin and land on the current view.
- The room code stays in the address bar (`&room=ABCD`), so reloading the dev page resumes hosting that room.
- REDLINE needs at least 2 players; Counter works with 1.

## 3. Join from real phones on the LAN

1. Find the PC's address: `ipconfig` (Windows), `ipconfig getifaddr en0` (macOS) or `hostname -I` (Linux).
2. On the phone (same Wi-Fi), open `http://<pc-ip>:5080/?room=ABCD` with the code from the dev page, or
   `http://<pc-ip>:5080/` and type the code.
3. If the phone can't connect, allow inbound TCP 5080 in the PC's firewall (Windows asks the first time
   `dotnet` listens; say yes for private networks).

Real phones and dev-page iframes can be mixed in one room, up to 8 players.

### Wake Lock needs HTTPS

The phone app asks for a screen Wake Lock while it shows an input, so the screen doesn't dim mid-question.
Browsers only grant it on a secure origin: `localhost` or HTTPS. Over plain `http://<pc-ip>:5080` it is
silently skipped and the phone may sleep. That is harmless (the app reconnects and rejoins when the screen
comes back), but annoying in a long REDLINE race. To test with Wake Lock:

- a tunnel such as `cloudflared tunnel --url http://localhost:5080` gives a public `https://…` URL that
  phones can open from anywhere (SignalR WebSockets work through it); or
- a certificate the phone trusts for the PC's LAN address (e.g. `mkcert <pc-ip>`, install mkcert's root CA
  on the phone) with `--urls https://0.0.0.0:5443` and Kestrel pointed at that certificate.
  The `dotnet dev-certs` certificate only covers `localhost`, so phones reject it.

## 4. Scripted playthrough (Playwright)

`web/play/e2e/redline.e2e.mjs` drives the same pages with bots that tap random options. REDLINE's qualifying is the
exception: before the Grid, bot k answers each lap after (1 + k) s and always right, from the bundled question pack's
answers, except bot 3 on lap 2. The run fails unless phone 1 showed all three qualifying views. It logs the bots'
expected grid order as `QUALI order …`, which `tools/tv-e2e.sh` checks against the TV. It needs the server
running as above and the pre-installed Playwright Chromium; it starts nothing itself.

```sh
node web/play/e2e/redline.e2e.mjs                                  # REDLINE, 3 phones, stops after the first Reveal
ARCBOX_FULL=1 node web/play/e2e/redline.e2e.mjs                    # race to the chequered flag and back to the lobby
ARCBOX_GAME=counter ARCBOX_PHONES=6 node web/play/e2e/redline.e2e.mjs
ARCBOX_ROOM=ABCD node web/play/e2e/redline.e2e.mjs                 # join a room hosted elsewhere with bot phones
ARCBOX_ROOM=ABCD ARCBOX_START=host node web/play/e2e/redline.e2e.mjs   # …and wait for that host (the TV) to start
```

To run it against the Godot TV app instead of the dev page, with screenshots from both, use `tools/tv-e2e.sh`
([`redline-tv.md`](redline-tv.md)).

It reloads phone 2 mid-game to check rejoin, fails if any phone sits on one view longer than a phase allows
or a page throws, and saves screenshots of phone 1 at each phase (and the dev board) to `web/play/e2e/shots`
(override with `ARCBOX_SHOTS`). The header of the script lists every option.
