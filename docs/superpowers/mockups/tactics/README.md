# ArcBox Tactics mockups

Draft TV and phone screens for the campaign RPG: HD-2D isometric battles with FFT-style speed-based turns
and d20 rolls, light exploration with a rotating party leader and party votes, and a between-sessions
campaign portal. The HTML sources and PNG renders are the visual spec.

Re-render with `node shoot.js` (or `node shoot.js 01-tv-battle.html` for one screen). It uses the Playwright
that's already installed. Everything is drawn procedurally by `kit.js` (terrain, pixel sprites, lighting,
bloom, tilt-shift depth of field), so there are no image assets. The real game builds these scenes in Godot.

| Screen | What it shows |
|---|---|
| [01 · TV battle](01-tv-battle.png) | Speed-based turn order with an AI badge on the absent player's character, the ability banner, a d20 roll with advantage, push/terrain effects, and the active unit and target cards |
| [02 · TV exploration and vote](02-tv-explore-vote.png) | The party walking as one group behind the crowned leader, NPC dialogue, and a live party vote that ends in a tie the leader breaks |
| [03 · TV skill check](03-tv-skill-check.png) | The party picks who attempts the check, everyone's bonus with its reason, the absent player's character still eligible, and the roll result |
| [04 · Phone battle](04-phone-battle.png) | Your turn (action economy and actions), targeting (cycle targets or move a free cursor on the TV's diagonal grid), and waiting (turn queue, reaction, sheet) |
| [05 · Phone party and portal](05-phone-party.png) | Party vote, the point-buy character creator, choosing a subclass after a missed week, and trading |
| [06 · Phone joining each week](06-phone-join.png) | One-tap "Continue as…" for a remembered phone, the "Who are you?" roster for a new one, and "Pick your seat" mid-game to take back an offline character (Platform v2 §3.1–3.2) |

Sample party: Kara (Jess), Aldric (Dave, away this session), Wren (Priya), Ilyra (Sam, leader this
session) and Tamsin (Marcus). Sefa (Alex) is a new recruit in the creator.
