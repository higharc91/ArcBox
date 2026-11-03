# Gold and shops (#68)

Every player now has a persisted individual purse and inventory and can trade at Wenna's Remedies in Ashen Ford. Interacting with the keeper opens the shop on every phone. Purchases and sales change shared shop stock, the authenticated player's gold, and that character's bag in one save; other players' balances and bags stay unchanged. All gold rewards, including chests, split evenly among participating characters. Legacy shared-purse saves split their gold once without losing the total. The most recent 32 trade IDs survive restarts; a duplicate does not charge or grant another item. The shop visit resumes after restart or End session, and closes if the last phone disconnects.

The issue's A–L balance defaults are recorded in the [design](../../specs/2026-11-02-tactics-shops-and-gold-design.md#implementation-decisions-issue-68) and [handoff](../../plans/2026-10-31-tactics-overworld-cloud-handoff.md). No gameplay art or audio was generated. The [art brief](../../specs/2026-11-02-tactics-shops-art.json) supplies keys, prompts and stand-ins; the merchant portrait and town stall are existing assets.

The following table records the initial issue implementation, before the owner's individual-purse correction. Correction validation is recorded below.

| Validation | Result |
|---|---|
| Full solution tests | 5,738 passed; zero failed or skipped |
| Final affected Tactics game suite | 542 passed; zero failed or skipped |
| Web tests | 483 passed |
| Lore checker tests | 12 passed |
| .NET solution build | zero warnings, zero errors |
| Previous phone fixture bytes | unchanged; six new fixtures added |
| Godot shop demo | panel, portrait, purse, purchase, sale, gold pop and departure rendered |
| Live shop e2e | buy, idempotent retry, sell and leave passed |
| Chapter 1 force e2e, six phones | eight scenes, 94 lines, assault win, chapter card and walking on passed |

The live shop mode uses real SignalR bot phones and Godot. Bots fund the purchase by selling existing starting consumables, buy a potion, resend its trade ID, sell it back and leave. It does not inject gold or alter starting equipment. The isolated Chapter 1 force-branch run passed with **six phones**: 94 expected lines were each said once across eight scenes, the assault was won, the chapter card appeared, and the party walked on. The scanner validated the existing story graph, scene order, battle placement and screenshots. The run used `ARCBOX_PHONES=6 ARCBOX_LINE=0.1 TV_HOLD=1`; the faster bot taps shorten waiting without changing rules. The initial four-phone attempt repeatedly lost the assault and was stopped; a four-phone live completion is not claimed. An earlier concurrent six-phone attempt hit the existing 10-second TV-world startup deadline under CPU load; the isolated rerun loaded normally and passed.

The final game-suite rerun includes two extra regression tests added after the full solution run: the distinct initial TV/phone purse visibility rules and combined trader/interactable ordering. All existing fixture bytes are still unchanged.

Initial implementation screenshots below are from `--world-demo=shop`, before the individual-purse correction:

- [Trader marker](marker.png)
- [Shop and keeper portrait](shop.png)
- [Purchase](buy.png)
- [Sale and gold pop](gold-pop.png)

Commands (from the repository root with .NET 10 and Godot 4.7.2 .NET active):

```bash
dotnet build ArcBox.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test --solution ArcBox.slnx --no-build --max-parallel-test-modules 1
(cd web/play && npm test && npm run build)
python3 -m unittest discover -s tools/tactics-lore
ARCBOX_SHOP=1 tools/tv-e2e.sh --skip-build
ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force ARCBOX_PHONES=6 ARCBOX_LINE=0.1 TV_HOLD=1 tools/tv-e2e.sh --skip-build
```

For this cloud machine's software renderer, both live commands use `TV_RENDERING_DRIVER=opengl3 TV_ENGINE_ARGS='--rendering-method gl_compatibility --audio-driver Dummy'` and `ARCBOX_SERVER_ENV='Speech__Enabled=false'`. The web production build retains the existing Rollup annotation and large-chunk notices. Core build and test runs have no warnings.

A rules manifest/item-price change updates the rules hash; the existing compatibility path restarts an in-progress battle once. No save version bump is needed. New documents still omit default gold/shop fields. Haggling, rest-based refresh, equipment trading, companion-bag selling, fees and additional gold sinks remain outside this slice.

## Owner correction: individual purses

The owner selected one purse per player and even splitting for **all** gold rewards. Phone purchasing was already individual in
its inventory destination and is now individual in payment as well. Each phone sees its own balance and sellable bag; a player
with no gold cannot spend a wealthy teammate's balance. The TV shop lists individual balances, and the walk banner labels its
combined number as a total across player purses.

Rewards divide across saved battle participants for wins, or present characters for story and exploration. Integer remainders
go in stable profile order. Shared story payments divide across available balances without negative purses. Existing shared
gold migrates once across saved members; personal balances and inventories survive restart, reconnection and reseating.

The [live individual-purse screenshot](personal-purses.png) shows Bo, a follower, buying and selling from his own phone while
Ana, Cy and Dee keep their zero balances. It uses the normal Ashen Ford market and existing art.

Validation: 546 game tests, 2,338 TV presentation tests and 483 web tests passed; the full .NET solution build has zero warnings
or errors and the web production build passed. All existing phone fixture files remain byte-identical. The live run sold Bo's
starting potion for 25 gold, bought a smoke bomb for 25, retried the purchase ID without a second charge, sold it for 12, and
verified teammates' balances and sellable inventories stayed unchanged. The corrected live multiplayer run completed buy, idempotent retry, sell, leave, and End session successfully.
The harness now waits for every phone's new view before continuing, so the leader never submits Leave using a stale view ID.

## Economy follow-up: remake exploit and Chapter 1 rewards

The sell/remake exploit was reproduced with a regression test, then fixed. Every remake now carries the current inventory,
injuries and personal gold through a class/race change. It cannot restore sold or consumed starting supplies or delete purchased
items. Initial creation still grants its normal starting kit; free pre-battle remakes remain unlimited and only post-battle
remakes consume the one free remake.

Chapter 1 now awards these **party totals**, split among participating players:

| Source | Gold |
|---|---:|
| Mayor's request, closing node: council advance | 200 |
| Victory in either ford encounter | 120 |
| Reeve's chest, plus its existing potion | 60 |
| Smugglers' cache, plus its existing smoke bomb | 40 |

A four-player party receives 50 gold each from the mayor, enough for one healing potion each while stock lasts. Normal Chapter 1
play with both optional treasures awards 420 gold in total. Both story routes have the same victory reward, and defeats pay
nothing. Already-resolved story nodes and emptied treasure do not get retroactive rewards. Prices, stock, starting gold and item
loot are unchanged. No narration lines or audio assets changed.

The live shop scenario now completes the mayor's request first and checks the advance is split evenly across phones. A follower
then buys using that reward, retries the trade ID, sells and leaves while teammates' purses and sellable inventories stay unchanged.
Validation passed: 556 game tests, 1,044 logic tests, 483 phone tests and 12 lore-checker tests; the .NET solution build has zero
warnings/errors and the production web build passed with its existing Rollup notices. Prior phone fixture bytes remain unchanged.
The live run completed the mayor's scene, showed 50 gold on every phone, bought a healing potion from Ana's own purse as a follower while Bo led, retried the
purchase ID without another charge, sold it, left and ended the session. It checked other players' balances and sellable inventories
stayed unchanged. [Council-funded shop screenshot](council-funded-shop.png).

The fresh-campaign route helper now includes initial interactable obstacles, as the server does. The first live attempt correctly
paid the reward but repeatedly routed through the town well; after fixing the helper, the isolated run reached Wenna and passed.
No runtime walking or map layout was changed.
