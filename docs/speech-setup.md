# Speech setup (Google Cloud Text-to-Speech)

The ArcBox server can give games a neural voice: REDLINE uses it to read questions and to commentate. The voice is
Google Cloud Text-to-Speech with **Chirp 3: HD** voices. The server holds the API key; the TV only talks to the
server's `/api/tts` endpoint and never sees the key.

Speech is optional. With no key (or any Google problem) `/api/tts` answers 503 and the TV uses its built-in voice,
so games still work, they just sound more robotic. How the TV narrates, falls back, ducks and logs is in
[`redline-tv.md`](redline-tv.md#sound); press F3 on the TV to see which voice is in use.

You need a Google account and a payment card (for the billing account; see step 3). Allow about 15 minutes.

## 1. Create a Google Cloud project

1. Open the Google Cloud console: https://console.cloud.google.com/
2. Click the project picker at the top of the page, then **New project**.
3. Name it (for example `arcbox-tts`), leave **Location** as it is, and click **Create**.
4. Make sure the new project is selected in the project picker before you continue.

## 2. Enable the Text-to-Speech API

1. Go to **APIs & Services → Library**: https://console.cloud.google.com/apis/library
2. Search for **Cloud Text-to-Speech API** and open it.
3. Click **Enable**.

## 3. Attach a billing account

Google requires billing on the project even if you stay inside the free tier. You are charged only for usage
beyond the free allowance (see [Cost and the free tier](#cost-and-the-free-tier)).

1. Go to **Billing**: https://console.cloud.google.com/billing
2. If you have no billing account, click **Create account** (or **Add billing account**) and follow the steps.
   New accounts may be offered free trial credit.
3. Link the project: **Billing → My projects** (or **Account management**), find `arcbox-tts`, open its
   **Actions** menu and choose **Change billing**, then pick your billing account.
4. Recommended: **Billing → Budgets & alerts → Create budget**, for example a budget of 1 (in your currency) with
   an email alert at 50 % and 100 %. Budgets alert you; they do not stop spending.

If the API is called on a project without billing, Google refuses it and the server logs a
`Speech synthesis failed … PERMISSION_DENIED …` warning that mentions billing.

## 4. Create an API key restricted to Text-to-Speech

1. Go to **APIs & Services → Credentials**: https://console.cloud.google.com/apis/credentials
2. Click **Create credentials → API key**. If the console offers to bind the key to a service account, leave
   that unticked: the server sends the key as a plain API key.
3. Open the new key (click its name, or **Edit API key**):
   - **Name:** for example `arcbox-server-tts`.
   - **API restrictions:** choose **Restrict key** and tick only **Cloud Text-to-Speech API**.
     A leaked key restricted this way can be used for nothing else.
   - **Application restrictions:** **None** is fine for a home server. If the server has a fixed public
     address you can choose **IP addresses** and add it.
4. Click **Save**, then **Show key** and copy it. Treat it like a password.

The same with the gcloud CLI, if you prefer it:

```sh
gcloud projects create arcbox-tts-12345            # project ids are globally unique: pick your own
gcloud config set project arcbox-tts-12345
gcloud billing accounts list                       # note the ACCOUNT_ID
gcloud billing projects link arcbox-tts-12345 --billing-account=ACCOUNT_ID
gcloud services enable texttospeech.googleapis.com
gcloud services api-keys create --display-name=arcbox-server-tts \
  --api-target=service=texttospeech.googleapis.com
gcloud services api-keys get-key-string KEY_ID     # KEY_ID is printed by the previous command
```

## 5. Give the key to the server

Never put the key in `appsettings*.json` or anywhere else in the repository.

**Development (`dotnet run`)**: use user-secrets. The server project already has a `UserSecretsId`, and the
Development environment (which `dotnet run` uses) reads them automatically:

```sh
dotnet user-secrets set "Speech:Google:ApiKey" "PASTE-THE-KEY" --project src/ArcBox.Server
dotnet user-secrets list --project src/ArcBox.Server      # check it is there
```

The value is stored outside the repository, in your user profile
(`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` on Windows, `~/.microsoft/usersecrets/<id>/secrets.json`
elsewhere).

**Anything else (a published build, a service, a container)**: set the environment variable
`Speech__Google__ApiKey` (two underscores between each part) before starting the server:

```powershell
$env:Speech__Google__ApiKey = "PASTE-THE-KEY"     # PowerShell, this window only
```

```sh
export Speech__Google__ApiKey="PASTE-THE-KEY"     # bash/zsh
```

Restart the server after setting the key. Other settings live in the `Speech` section of
`src/ArcBox.Server/appsettings.json`:

| Setting | Default | Meaning |
|---|---|---|
| `Speech:Enabled` | `true` | `false` turns `/api/tts` into a 503 everywhere (the TV uses its built-in voice). |
| `Speech:CacheDir` | `.tts-cache` | Where clips are cached, relative to the server's content root (`src/ArcBox.Server`). Safe to delete. |
| `Speech:MaxChars` | `300` | Longest text accepted per request. |
| `Speech:RequestsPerMinutePerRoom` | `60` | Per-room limit on `/api/tts`. |
| `Speech:Voices:<theme id>` | — | Per-theme voice overrides; see [Voice themes](#voice-themes-for-game-authors). |

## 6. Check that it works

Start the server and host a room, then ask `/api/tts` for a clip. The endpoint only answers a room's host, so you
need a room code and its host token.

1. Run the server (`dotnet run --project src/ArcBox.Server --urls http://localhost:5080`) and open the dev page,
   http://localhost:5080/dev.html. It hosts a room and shows the code (also in the address bar: `&room=ABCD`).
2. Get the host token: in the browser's developer tools console on the dev page, run
   `localStorage.getItem("arcbox-host:ABCD")` with your room code. (The Godot TV keeps the same pair in
   `session.json` in its user data folder, `user://`.)
3. Request a clip (on Windows PowerShell type `curl.exe`, not `curl`):

```sh
curl -sS -o test.mp3 -w "%{http_code} %{content_type}\n" \
  -H "Content-Type: application/json" \
  -d '{"code":"ABCD","hostToken":"THE-TOKEN","text":"Lights out and away we go!","voice":"redline.announcer"}' \
  http://localhost:5080/api/tts
```

`200 audio/mpeg` and a playable `test.mp3` mean everything works. On the TV, F3 then shows `VOICE GOOGLE` once a
line has been spoken. Otherwise:

| Status | Meaning |
|---|---|
| 401 | Wrong room code or host token (or the room has closed). |
| 400 | Empty text, text over `MaxChars`, or an unknown voice theme. |
| 429 | Over the room's per-minute limit; wait for `Retry-After` seconds. |
| 503 | Speech is off, no key is configured, or Google refused or failed. The server log says why (look for `Speech synthesis failed`; the key is always shown as `REDACTED`). |

The voices a room can use (ids and descriptions only):

```sh
curl -sS -H "X-ArcBox-Room: ABCD" -H "X-ArcBox-Host-Token: THE-TOKEN" http://localhost:5080/api/tts/voices
```

## Cost and the free tier

Google bills Text-to-Speech per character sent. At the time of writing, **Chirp 3: HD voices include 1 million
characters free per month**, then cost US$30 per million characters. Check the current numbers on the official
pricing page: https://cloud.google.com/text-to-speech/pricing

ArcBox keeps usage low:

- Every clip is cached on disk (`Speech:CacheDir`), keyed by the exact voice settings and text, so repeated text
  (the same question in a later game, the same commentary line) is never sent twice.
- REDLINE sends each question once per race (prewarmed in the background when the race starts) plus a few short
  commentary lines per round; a typical game night of about 10 races is a few tens of thousands of characters.

**Where to see usage:** the server logs a running character count, per voice theme and in total, once an hour and
when it shuts down:

```
info: ArcBox.Server.Speech.SpeechUsageReporter[0]
      Speech usage since start: 12345 characters synthesised (arcbox.host=120, redline.announcer=2210, redline.questions=10015)
```

It counts only text actually sent to Google (cache hits and failed requests are not counted) and restarts from
zero when the server restarts. Google's own figure is under **APIs & Services → Cloud Text-to-Speech API →
Metrics** and in **Billing → Reports**.

## Voice themes for game authors

Games never name a Google voice directly. They name a **voice theme**: a namespaced id such as
`redline.announcer` with a voice, optional pace, and hints for the TV's built-in fallback voice. The platform
provides `arcbox.host` (the default when a request names no theme). This lets the server owner re-voice any game
in configuration, and keeps provider details off the TV.

**1. Declare themes in your game's Logic project**, next to the game, using the SDK types
(`ArcBox.Sdk.Speech`). Ids must match `^[a-z0-9]+(\.[a-z0-9-]+)+$` and start with your game id:

```csharp
using ArcBox.Sdk.Speech;

public static class MyGameVoices
{
    public const string Narrator = "mygame.narrator";

    public static IReadOnlyList<VoiceTheme> All { get; } =
    [
        new(Narrator, "Story narrator: calm", "en-US-Chirp3-HD-Charon",
            SpeakingRate: 0.95, Fallback: new FallbackVoice(Rate: 0.95f)),
    ];
}
```

`VoiceTheme(Id, Description, Voice, LanguageCode = "en-US", SpeakingRate = null, Pitch = null, VolumeGainDb = 0,
Fallback = null)`. Pick voices from Google's Chirp 3: HD list
(https://cloud.google.com/text-to-speech/docs/chirp3-hd); a voice name is `<language>-Chirp3-HD-<Name>`.
Chirp 3: HD voices accept `SpeakingRate` (0.25–2.0) but not `Pitch`, and `VolumeGainDb` is not documented for
them, so the server sends only the speaking rate for Chirp 3: HD and logs one warning per theme for anything it
drops. Older voice families (Standard, WaveNet, Neural2) accept all three.

**2. Register them in your game's server registration** (as `AddRedline` does):

```csharp
services.AddVoiceThemes(MyGameVoices.All);
```

Ids must be unique across the platform; a duplicate stops the server at startup with a clear message.

**3. Optionally prewarm text you know in advance**, so the TV's request is a cache hit. `ISpeechService.Prewarm`
returns at once, works in the background (two clips at a time across the server) and never throws:

```csharp
speech.Prewarm(MyGameVoices.Narrator, chapters.Select(c => c.Intro));
```

Check `ISpeechService.IsAvailable` first if building the texts is costly; when speech is off, `Prewarm` does nothing.

**4. Override voices in configuration** (no code change). Any field can be overridden per id under
`Speech:Voices:<id>`, and a new id can be added if it sets `Voice`:

```json
"Speech": {
  "Voices": {
    "redline.announcer": { "Voice": "en-US-Chirp3-HD-Puck", "SpeakingRate": 1.15 },
    "redline.questions": { "Voice": "en-GB-Chirp3-HD-Charon", "LanguageCode": "en-GB" },
    "arcbox.host": { "Fallback": { "Rate": 1.1, "Pitch": 1.0 } }
  }
}
```

Changing a theme's voice settings never serves old audio: the cache key includes the resolved voice, language,
rate, pitch and gain. An override for an id nobody registers, without a `Voice`, stops the server at startup.

**5. Speak from the TV scene** with the session's room code and host token and your theme id:

```http
POST /api/tts
Content-Type: application/json

{"code":"ABCD","hostToken":"…","text":"Chapter one.","voice":"mygame.narrator"}
```

The response is `audio/mpeg` (play it with Godot's `AudioStreamMP3`); `voice` may be omitted for `arcbox.host`.
On any non-200 answer, speak the text with the built-in voice using the theme's fallback hints, which
`GET /api/tts/voices` (headers `X-ArcBox-Room`, `X-ArcBox-Host-Token`) lists as
`[{"id","description","fallback":{"rate","pitch"}}]`. On a 503, wait a minute before asking the server again.
The TV's shared narrator does all of this for you: implement `IAudioScene` in your scene and call
`narrator.Say(text, "mygame.narrator", NarrationPriority.Commentary)` ([`tv-host.md`](tv-host.md#adding-a-game-scene)).
