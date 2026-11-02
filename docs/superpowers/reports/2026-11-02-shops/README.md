# Gold and shops (#68)

The party now has a persisted purse and can trade at Wenna's Remedies in Ashen Ford. Purchases and sales change stock, gold, and the owning character's bag in one save. The most recent 32 trade IDs survive restarts; a duplicate does not charge or grant another item. The shop visit resumes after restart or End session, and closes if the last phone disconnects.

The issue's A–L balance defaults are recorded in the [design](../../specs/2026-11-02-tactics-shops-and-gold-design.md#implementation-decisions-issue-68) and [handoff](../../plans/2026-10-31-tactics-overworld-cloud-handoff.md). No gameplay art or audio was generated. The [art brief](../../specs/2026-11-02-tactics-shops-art.json) supplies keys, prompts and stand-ins; the merchant portrait and town stall are existing assets.

| Validation | Result |
|---|---|
| Full solution tests | 5,738 passed; zero failed or skipped |
| Final affected Tactics game suite | 542 passed; zero failed or skipped; zero failed or skipped |
| Web tests | 483 passed |
| Lore checker tests | 12 passed |
| .NET solution build | zero warnings, zero errors |
| Previous phone fixture bytes | unchanged; six new fixtures added |
| Godot shop demo | panel, portrait, purse, purchase, sale, gold pop and departure rendered |
| Live shop e2e | buy, idempotent retry, sell and leave passed |
| Chapter 1 force e2e, six phones | eight scenes, 94 lines, assault win, chapter card and walking on passed |

The live shop mode uses real SignalR bot phones and Godot. Bots fund the purchase by selling existing starting consumables, buy a potion, resend its trade ID, sell it back and leave. It does not inject gold or alter starting equipment. The isolated Chapter 1 force-branch run passed with **six phones**: 94 expected lines were each said once across eight scenes, the assault was won, the chapter card appeared, and the party walked on. The scanner validated the existing story graph, scene order, battle placement and screenshots. The run used `ARCBOX_PHONES=6 ARCBOX_LINE=0.1 TV_HOLD=1`; the faster bot taps shorten waiting without changing rules. The initial four-phone attempt repeatedly lost the assault and was stopped; a four-phone live completion is not claimed. An earlier concurrent six-phone attempt hit the existing 10-second TV-world startup deadline under CPU load; the isolated rerun loaded normally and passed.

The final game-suite rerun includes two extra regression tests added after the full solution run: the distinct initial TV/phone purse visibility rules and combined trader/interactable ordering. All existing fixture bytes are still unchanged.

Screenshots are from `--world-demo=shop`, a deterministic existing courtyard used to review the normal host-view path:

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
