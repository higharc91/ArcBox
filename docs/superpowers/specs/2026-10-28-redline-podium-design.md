# REDLINE Podium Celebration — Design Spec

- **Date:** 2026-10-28
- **Status:** Approved in conversation
- **Builds on:** [REDLINE design](2026-10-25-redline-design.md), [audio design](2026-10-26-redline-audio-design.md)

## 1. Purpose

The race currently ends on a static Chequered screen. Add an F1-style podium ceremony: an anthem, then a champagne spray that the three podium players perform **from their phones** by shaking them and popping the cork.

### Decisions made by the user
| Topic | Decision |
|---|---|
| Music | **The winning car's driver's national anthem** (public-domain melody, played by our own synth), **then an original REDLINE victory theme** for the champagne. An anthem that is not clearly public domain falls back to the theme. |
| Who sprays | **The top 3 players** (humans), even if AI cars finished ahead of them. With fewer than 3 players, every player gets a bottle. |

### Copyright note
The music heard behind real F1 podium celebrations is the official F1 theme (Brian Tyler, 2018) — copyrighted and not usable. National anthem **compositions** used here are public domain; we synthesise the melody ourselves, so no recording is involved. Each anthem entry records its composer, year and why it is public domain.

| Driver nationality (2026 grid) | Anthem | Status |
|---|---|---|
| GB (NOR, HAM, RUS, LIN, BEA) | God Save the King (traditional) | PD |
| AU (PIA) | Advance Australia Fair (P. D. McCormick, 1878) | PD |
| MC (LEC) | Hymne Monégasque (T. Bellando de Castro 1841; L. Jehin 1896) | PD |
| NL (VER) | Het Wilhelmus (16th c.) | PD |
| FR (HAD, GAS, OCO) | La Marseillaise (Rouget de Lisle, 1792) | PD |
| IT (ANT) | Il Canto degli Italiani (M. Novaro, 1847) | PD |
| ES (ALO, SAI) | Marcha Real (18th c.) | PD |
| CA (STR) | O Canada (C. Lavallée, 1880) | PD |
| AR (COL) | Himno Nacional Argentino (B. Parera, 1813) | PD |
| NZ (LAW) | God Defend New Zealand (J. J. Woods, 1876) | PD |
| DE (HUL) | Das Lied der Deutschen (J. Haydn, 1797) | PD |
| BR (BOR) | Hino Nacional Brasileiro (F. M. da Silva, 1831) | **Fallback** — Brazilian law (Lei 5.700/1971, art. 34) restricts arrangements of the anthem |
| FI (BOT) | Maamme (F. Pacius, 1848) | PD |
| TH (ALB) | Phleng Chat Thai (Phra Chenduriyang, 1932) | **Fallback** — public-domain status outside Thailand unclear |
| MX (PER) | Himno Nacional Mexicano (J. Nunó, 1854) | **Fallback** — Mexican law forbids arrangements of the anthem |

Only the opening phrases are played (≈ 16 s), as F1 broadcasts often do.

**Update 2026-10-28:** MC, AR and NZ are public domain but ship without a melody until each is checked against a score (the TV plays the theme when `Anthems.Find(id)?.HasMelody` is false). Brazil is a permanent fallback for the legal reason in the table.

## 2. Rules and flow

Two new phases after **Chequered**; `EndGame` moves from Chequered's timer to Champagne's.

| Phase | Duration | TV | Podium players' phones | Other phones |
|---|---|---|---|---|
| **Chequered** (unchanged) | 10 s | Classification, awards | "You finished P{n} of {total}." | same |
| **Anthem** (new) | `RedlineRules.Anthem` 16 s | 3D podium set; the three players on steps 1–3 (name, car code, team colour); "NATIONAL ANTHEM · {Country}" caption; anthem plays | `ShakeView` (CanFire = false): "Shake to build pressure!" | `WaitView("P{n}. Watch the podium!")` |
| **Champagne** (new) | `RedlineRules.Champagne` 16 s | REDLINE theme; confetti; each spray fires from that player's step with strength = charge | `ShakeView` (CanFire = true): "Pop the cork! Shake again to reload" | same wait view |

- `RedlineRules.Celebrate` (bool, default true) skips both phases when false.
- **Podium players:** humans ranked by race position (§2.3 ties keep classification order), first three. `Step` = 1..3 in that order. If the room has no players (AI-only), both phases are skipped.
- **Inputs** (`ShakeInput(double Charge, bool Fire)`):
  - Anthem and Champagne: `Charge` (clamped 0..1, NaN ignored) updates the player's stored charge.
  - Champagne only: `Fire = true` records a spray `{Step, Strength = stored charge (min 0.15), Seq}` and resets charge to 0. At most one spray per 400 ms per player (extra fires ignored).
- The game is still deterministic; no timers beyond the phase timer.
- Disconnected podium players keep their step; they just don't spray.

### HostView additions
`RedlineHostView` gains a last positional parameter `Podium? Podium` (non-null in Anthem and Champagne only):
```csharp
public sealed record Podium(string AnthemCountry, string? AnthemId,  // AnthemId null => theme only (fallback)
    IReadOnlyList<PodiumStep> Steps);
public sealed record PodiumStep(int Step, PlayerId Player, string Name, string Code, string TeamId,
    int RacePosition, double Charge, IReadOnlyList<Spray> Sprays);   // Sprays: all so far, Seq increasing
public sealed record Spray(int Seq, double Strength);
```
`Charge` is quantised to 0.05 so the host view changes at most ~20 times per player per phase from shaking.

### Data
`grid2026.json` drivers gain `"nationality"` (ISO 3166-1 alpha-2), from F1DB's `nationalityCountryId`; the refresh script maps it. Anthem country = the **winning podium player's car's driver** nationality (P1 of the podium, i.e. the best player).

## 3. SDK: a reusable `shake` widget

```csharp
[JsonDerivedType(typeof(ShakeView), "shake")]
public sealed record ShakeView(string Prompt, string? Color = null, bool CanFire = false, double InitialCharge = 0) : ControllerView;
public sealed record ShakeInput(double Charge, bool Fire);
```
- Generic: "shake to charge, tap to fire". Any game can use it (dice, spray cans, maracas…).
- The view stays unchanged while the player shakes (charge is shown locally on the phone), so in-flight inputs are never voided. Moving to Champagne sends a new `ShakeView` with `CanFire = true` and `InitialCharge` = the stored charge.
- Phones send `Charge` at most 4×/s (and on change ≥ 0.05) and `Fire` on pop; well within the 20/s hub limit.

## 4. Phone (`web/play/src/widgets/Shake.tsx`)
- A champagne bottle (SVG) in `Color` (team accent), foil, label "REDLINE"; foam level and fizz bubbles follow the local charge; the bottle wobbles with motion.
- **Charging:** `DeviceMotionEvent` acceleration magnitude above gravity adds charge (decays slowly when still). iOS: a "Tap to enable shaking" button calls `DeviceMotionEvent.requestPermission()` inside the gesture; denied/unavailable (or desktop) → **fallback: tap the bottle rapidly** to charge. Works over HTTP for the fallback; motion needs HTTPS (documented).
- **Firing:** a big "POP!" button (or a sharp flick when motion is on) when `CanFire`; cork-pop sound, spray animation, strong haptic; charge resets; shake again to reload.
- Haptics: ticks while charging (Android); sounds: rising fizz, cork pop (WebAudio, muted with the existing toggle).

## 5. TV
- **Podium set** (built in code, near the pit building): three stepped blocks (1 centre/highest, 2 left, 3 right) with numerals, a REDLINE backdrop with lights, the three players' cars parked in front, a champagne bottle on each step (lathed mesh, team-colour foil).
- **Spray:** each new `Spray` (by `Seq`) launches a `GPUParticles3D` champagne arc from that step's bottle toward the crowd/camera, size and duration scaled by `Strength`; foam drips; cork projectile. Confetti during Champagne.
- **Camera:** front of podium, slow push-in during the anthem, wider during champagne.
- **HUD:** names over steps with car code and team colour; "NATIONAL ANTHEM · {Country}" caption during Anthem (with "for {Driver}, {Team}" subtitle); "CHAMPAGNE!" banner and a live pressure gauge per step during Champagne.
- **Audio:** anthem via the synth on the Music bus (brass-band timbre), then the REDLINE victory theme; crowd cheer; cork pop + spray hiss per spray; commentator: Anthem "The national anthem of {Country}, for {Name} in the {Team}!" and Champagne "Let the champagne fly!".

## 6. Testing
- Logic: phase flow, podium selection (players only, ties, < 3 players, AI-only skip), charge clamping/quantising, fire only in Champagne, 400 ms spray throttle, Celebrate=false, EndGame after Champagne, host view contents.
- SDK: `ShakeView`/`ShakeInput` serialisation and the web contract fixture.
- Synth: every anthem renders within length, peak ≤ −1 dBFS; each anthem entry has composer/year/status; fallback countries have no melody; the victory theme loops cleanly.
- Phone: vitest for charge accumulation (motion and tap fallback), permission flow, send throttling, fire/reset, mute.
- TV: `--redline-demo` reaches Anthem and Champagne with scripted sprays; screenshots; `AUDIO` log lines for anthem, theme, pops; e2e drives three bot phones that charge and fire.
