# Ashen Ford NPC brief (for issue #73)

Generated from the lore files on this branch: `content/tactics/base/lore/characters.json`, `barks/`, `scenes/ch1.json`, `arcs.json`, `factions.json` and `codex/vale.json`, plus the people in `rules/world/ashen_ford.json`. **The lore files are the source of truth.** If they change, regenerate this file or trust them over it. Write every new conversation, bark and quest line in each character's voice as described here, and keep to their wants, flaws and relationships.

**Secrets are for the writer only.** A side conversation may hint at a secret, but must never reveal it before the chapter whose main scenes do (check `scenes/ch*.json` and `arcs.json`). The secret is listed so you don't contradict it.

## Chapter 1 in brief

**The Ashen Ford** (levels [1, 2]). The Skarrow goblins hold the ford bridge and Emberwick will starve without the road east. The party decides whether the goblins are enemies or neighbours, and learns that the warm ground under the town is cooling.

| Beat | Kind | Summary |
|---|---|---|
| `ch1_mayors_request` | story | In Emberwick's square at dusk, Mayor Halden asks the party to reopen the ford before the new moon: "Will you clear them out, or will you talk to their chief?" The Hooded Stranger watches from the inn door. |
| `ch1_the_ford_choice` | choice | The party vote that opens the campaign, as in mockup 02. A Persuasion check can win more pay from the mayor first. |
| `ch1_ford_assault` | battle | By force: the party storms the ford at dawn. Mother Skarrow leads her clan's warriors herself, and dies on the bridge. |
| `ch1_river_crossing` | battle | After the parley, the clan's young hotheads, who answer to Vorga, refuse the truce and hold the crossing. |
| `ch1_old_maud` | discovery | On the burned bank the party meets Old Maud, who watched the ember fall a hundred years ago. "It didn't fall, dearie. It was thrown." She points at the Stranger and says she saw that face that night. |
| `ch1_granary` | story | Feeding the clan means opening the granaries, and the town council balks. Pressed, Halden admits he has known for six years that the ember under the ford is fading, and the warm fields with it. |
| `ch1_skarrow_song` | camp | At the clan's fire, Mother Skarrow sings the first verses of the clan's oldest song, about seven people who went over the Greywall and came back running. She won't sing the rest to strangers. Yet. |
| `ch1_the_ember` | story | Under the docks, the party finds the ford's ember in the riverbed, dim as a coal. The Ash Market has been chipping shards off it. A Kindled preacher named Maren Sorrel arrives in town offering to save it. The Stranger tells the party there are four more, that the next lies in Mirewood, and that this one will keep better near its sisters. The party takes the ford's ember with them: from here on, the party carries every ember it takes. |

Choices and the flags they set:
- `ch1_force`: "We'll clear the ford by force." sets `ch1_skarrow_slain`. The party storms the bridge. Mother Skarrow dies there, and the clan never forgives Emberwick.
- `ch1_parley`: "We'll go and parley with the chief." sets `ch1_skarrow_spared`. The party walks up the hill to Mother Skarrow's camp, finds starving children, and brokers a deal: the bridge for a share of the town's grain.

Scene flags set by Chapter 1 scenes: `ch1_met_maud` (ch1_maud), `ch1_halden_confessed` (ch1_granary), `ch1_ember_found` (ch1_docks). Side content may **read** any of these, but must never **set** a main-story choice's flags.

## Places (from the codex)

- **The Vale**: A long river valley ringed by the Greywall Mountains. For three hundred years it was the warmest country anyone knew of, kept so by the Hearthflame in Ember Keep. Since the Fall a century ago, it has been getting colder.
- **Emberwick**: A market town on the warm, grey ground of the Ashen Ford, built in the century since an ember fell into the river. Its fields grow through the winter, its inn is the Last Coal, and its only road east crosses the ford bridge. The ground is cooler than it used to be. Nobody says so.
- **The Ashen Ford**: The only crossing of the river on the road east. On the night of the Fall an ember plunged into the water here; the river boiled for a year and the banks burned grey. The water still steams on cold mornings.

## Factions

- **Emberwick** (`emberwick`): The town at the Ashen Ford and the party's home. Built on ground still warm from the ember in the river; frightened, stubborn, and about to have a very hard winter. Goals: Survive the winter.; Keep the road east open.; Keep the warm ground warm.
- **The Church of the Hearth** (`church_of_the_hearth`): The Vale's church, which teaches that the Hearthflame was a gift of the gods to a deserving people and that its loss is a judgement. Goals: Keep the faithful through the cold.; Recover the embers for the church.; Keep the oldest scriptures closed.
- **The Kindled** (`the_kindled`): A cult gathering the embers to relight the Hearthflame by feeding it lives. Its followers are mostly the hungry and the desperate, and many are genuinely kind. Goals: Gather all five embers in Ember Keep.; Relight the Hearthflame, whatever it costs.; Save the Vale.
- **The Skarrow Clan** (`skarrow_clan`): A goblin clan from the hills above the ford, driven down by the cold and the hunger. They hold the bridge because Emberwick locked its granaries. Goals: Feed the clan through the winter.; Take back the hunting grounds the Vale's farms swallowed in the Hearth Age.
- **The Ash Market** (`the_ash_market`): The black market in ember shards, run from Emberwick's river docks. Everyone uses it; nobody admits to it. Goals: Buy shards cheap and sell them dear.; Stay useful to whoever wins.

## Named characters in Ashen Ford

Each is placed in the region as a person with this preset. Only one person has each preset unless noted, so that person **is** the character. Give its people entry the `character` id.

### Halden Varrow, Mayor of Emberwick (`halden_varrow`)

- **Preset:** `mayor`. **Stands at:** (29, 62).
- **Role:** ally. **Faction:** emberwick.
- **Summary:** Emberwick's tired, decent mayor, who has to decide who goes hungry this winter.
- **Bio:** Halden inherited the mayoralty from his mother and has spent twenty years keeping Emberwick together with compromise, charm and borrowed grain. He is a good man who has made one terrible decision: years ago he learned the ember under the ford was fading and chose to say nothing, so that people would not leave. Now the goblins hold the bridge and he cannot hide it much longer.
- **Wants:** To get Emberwick through the winter without anyone learning what he knew.
- **Flaw:** He will always pick the lie that keeps the peace.
- **Secret (writer only):** He has known for six years that the warm ground is cooling, and he locked the granaries against the goblins rather than admit the town cannot feed itself and them. Revealed in the main story by: flag `ch1_halden_confessed`.
- **Voice:** Warm baritone, a little hoarse, a man who has given too many speeches. Reassuring until he is cornered, then quiet and fast. Sample: "The ford is the only road east. If those goblins hold the bridge past the new moon, Emberwick goes hungry this winter."
- **Relationship:** debt with Lady Celandine Marrowby: The Marrowbys lend the town grain; Halden owes Celandine more than the town can repay.
- **Relationship:** friend with Dagna Holt: His oldest friend, who suspects what he is hiding.
- **Relationship:** enemy with Mother Skarrow: He has never met her and has already decided she cannot be reasoned with.
- **Speaks in Chapter 1:** `ch1_ford` (The Ford Choice), `ch1_granary` (The Locked Granaries), `ch1_mayor` (Emberwick at Dusk)
- **Existing town barks** (`barks/halden_varrow.json`): "Emberwick has seen hard winters before. We'll see this one through, too." / "Good to see you back. Truly. The square's been quieter than I'd like." / "The council wants numbers and hope. I can only manage one at a time." / "Mind the frost by the well. That's new. That was never there before." / "Not now. Please. Whatever it is, not here, not in the square." / "Speeches, speeches. I've given so many I dream in them." / "If anyone asks, things are well in hand. Are they? No. Don't answer that."

### Bree Tallow, Keeper of the Last Coal (`bree_tallow`)

- **Preset:** `innkeeper`. **Stands at:** (25, 55).
- **Role:** ally. **Faction:** emberwick.
- **Summary:** The innkeeper who hears everything and feeds anyone who sits at her hearth.
- **Bio:** Bree runs the Last Coal, Emberwick's inn, and has done since her husband, Ned, drowned at the ford eleven years ago. The inn's hearth burns on an ember shard her grandmother bought from a Warden. She feeds the party between chapters, passes on rumours, and is the closest thing the town has to a conscience.
- **Wants:** For the people at her tables to be fed, warm and honest with each other.
- **Flaw:** She protects people from the truth when she thinks they cannot bear it.
- **Secret (writer only):** Her hearth shard has burned away almost every memory of her husband. She has started writing him down so she won't lose the rest. Revealed in the main story by: flag `ch2_bree_journal`.
- **Voice:** Alto, brisk and kind, a West-Country lilt. Talks while working; softens without slowing down. Sample: "Sit. Eat. Then you can tell me how bad it is."
- **Relationship:** family with Pip Ashdown: Has taken in Pip and pretends she hasn't.
- **Relationship:** unknown with Rook: Buys her shards from the Ash Market and doesn't ask.
- **Speaks in Chapter 1:** `ch1_ford` (The Ford Choice), `ch1_mayor` (Emberwick at Dusk)
- **Existing town barks** (`barks/bree_tallow.json`): "Sit. Eat. Then you can tell me how bad it is." / "Stew's hot, bread's yesterday's. Don't complain, my love, nobody else is feeding you." / "Boots off the benches. I'll not tell you twice. Well, I will, but I'll not like it." / "Pip! Where's that girl got to? Never mind. She'll turn up when she's hungry." / "Heard a thing at the bar you'll want to hear. Eat first."

### Mother Aubrine, Priest of the Hearth (`mother_aubrine`)

- **Preset:** `priest`. **Stands at:** (30, 55).
- **Role:** ally. **Faction:** church_of_the_hearth.
- **Summary:** Emberwick's priest: sincere, generous, and about to have her faith tested.
- **Bio:** Mother Aubrine has run Emberwick's chapel for thirty years, feeding the poor and burying the dead. She believes the Hearthflame was a gift and its loss a test the Vale can pass. She is not a hypocrite; she has never been shown the church's oldest scriptures. When the party brings her the truth, she has to decide whether her faith was in the flame or in the people around it.
- **Wants:** To keep her flock together and believing through the cold.
- **Flaw:** She mistakes certainty for courage.
- **Voice:** Clear mezzo, measured, used to filling a chapel. Gentle in private, unbending in public. Sample: "The flame was given to us. It is not ours to bargain with."
- **Relationship:** rival with Corvin Ashby: They have argued theology and history over the same table for twenty years.
- **Relationship:** enemy with Maren Sorrel: Maren preached from Aubrine's own pulpit once. Aubrine threw her out.
- **Speaks in Chapter 1:** `ch1_granary` (The Locked Granaries), `ch1_kindler` (The Woman in Grey)
- **Existing town barks** (`barks/mother_aubrine.json`): "The flame was given to us. We will be worthy of it, cold or warm." / "Come in out of the wind. There's soup at the back, and nobody counts the bowls." / "Pray if you like. If you'd rather not, sit anyway. The chapel doesn't mind." / "I buried two this week, old ones, cold in their beds. Pray it stays only the old." / "Corvin says the records have holes in them. Corvin says a great many things." / "Doubt is a draught under the door. Shut it, and keep the hearth." / "Mind how you speak in the square. Fear spreads faster than fever."

### Corvin Ashby, Archivist of Emberwick (`corvin_ashby`)

- **Preset:** `sage`. **Stands at:** (36, 58).
- **Role:** ally. **Faction:** emberwick.
- **Summary:** The town's scholar, who has noticed that whole years are missing from the records.
- **Bio:** Corvin keeps Emberwick's small archive and has spent his life reconstructing the history of the Vale from fragments. He has read by ember-lamp at night for thirty years, and he has gaps in his own memory he cannot explain. He is the first to suspect the ember-fire takes something, and he is the party's guide to the Vale's past.
- **Wants:** The whole history of the Vale, written down where nothing can burn it.
- **Flaw:** He will trade anyone's safety, including his own, for one more answer.
- **Secret (writer only):** Corvin's notebooks hold a page in his own handwriting about the cost of ember-fire. He doesn't remember writing it. Revealed in the main story by: flag `ch2_corvin_page`.
- **Voice:** Light, quick tenor, an anxious academic. Trails off mid-sentence when he has an idea. Sample: "There should be a record for every year. There isn't. Forty-one years are simply… not there."
- **Relationship:** unknown with Oswin Hale: Has spent his life reading the man's letters without knowing he is still alive.
- **Relationship:** student with Old Maud: The only person he trusts about the night of the Fall.
- **Existing town barks** (`barks/corvin_ashby.json`): "Ah, you're back! Good. I've found another gap. Well, not found. More... not found." / "Forty-one years, simply missing. Who loses forty-one years? Unless..." / "Don't touch that pile, it's in order. It doesn't look in order. It is." / "If you find anything written down out there, anything at all, bring it to me. Please." / "Thirty years in these records, and forty-one years are simply missing from them. Nobody else seems to mind." / "Old Maud says I ask the wrong questions. She's usually right, which is infuriating." / "Ink. Ink doesn't forget. That's the whole... yes. That's the whole point of ink." / "Mother Aubrine calls history a matter of faith. I call it a matter of paper."

### Dagna Holt, Captain of the Ford Watch (`dagna_holt`)

- **Preset:** `guard_captain`. **Stands at:** (45, 64).
- **Role:** ally. **Faction:** emberwick.
- **Summary:** The captain of Emberwick's Watch, now down to eight guards after losing the bridge.
- **Bio:** Dagna has commanded Emberwick's guard for twelve years, most of them spent breaking up tavern fights. When the Skarrow clan took the bridge she lost two guards and her nerve, and has not forgiven herself for either. She wants the bridge back by force, and she will be furious if the party makes peace instead, until she isn't.
- **Wants:** To make Emberwick safe, and to stop feeling like she failed it.
- **Flaw:** She wants someone to blame, and goblins are easiest.
- **Voice:** Low, flat, Northern; few words. A soldier's patience that runs out suddenly. Sample: "Two of mine died on that bridge. I'd like a reason that's better than hunger."
- **Relationship:** friend with Halden Varrow: Loyal, and beginning to doubt him.
- **Relationship:** enemy with Mother Skarrow: Wants her dead, at first.
- **Speaks in Chapter 1:** `ch1_bridge_cost` (What the Bridge Cost), `ch1_dawn` (Dawn at the Ford), `ch1_ford` (The Ford Choice), `ch1_granary` (The Locked Granaries), `ch1_mayor` (Emberwick at Dusk), `ch1_road_east` (The Road East), `ch1_young_ones` (The Young Ones)

### Tobin Fell, Smith of Emberwick (`tobin_fell`)

- **Preset:** `blacksmith`. **Stands at:** (36, 55).
- **Role:** npc. **Faction:** emberwick.
- **Summary:** The town smith, whose ember-forge is going cold.
- **Bio:** Tobin's forge has run on an ember shard for four generations, and it is fading. He makes and mends the party's gear between chapters. He is gruff, practical and frightened of what he'll be without his fire, more than he will ever say.
- **Wants:** To keep his forge lit.
- **Flaw:** Would buy a shard from anyone, no questions asked.
- **Voice:** Deep, rough bass, slow speech, answers questions with a grunt before the words. Sample: "Bring me the steel and I'll make it sing. Bring me a shard and I'll make it scream."
- **Relationship:** debt with Rook: Owes the Ash Market for his last shard.
- **Existing town barks** (`barks/tobin_fell.json`): "Mm. Bring me the steel. I'll see what's left in the fire." / "Forge is cold this morning. Colder than yesterday." / "Hm. That edge has seen work. Leave it with me." / "Don't ask where the last shard came from. I didn't." / "Mm. Good steel remembers the hammer. Better than I do, some days." / "Four Fells at this anvil before me. I'll not be the one lets it go out." / "Shut the door. You're letting the heat out. What's left of it."

### Pip Ashdown, The Ember Child (`pip_ashdown`)

- **Preset:** `child`. **Stands at:** (18, 56), (30, 59), (33, 59) (3 people share this preset; pick one and give only that one the `character` id). Suggested: (30, 59), by the square where she meets the party in `ch1_mayor`.
- **Role:** npc. **Faction:** emberwick.
- **Summary:** An orphan born on the warm ground who remembers things nobody ever taught her.
- **Bio:** Pip is nine, a human orphan who lives in Bree's back room and runs errands for coin. She was born on the burned bank of the ford, close to the ember, and she is ember-touched: she knows old songs, a dead man's way home, the layout of a keep she has never seen. She attaches herself to the party early, and follows it all the way to Ember Keep. She is the first proof that the fire keeps what it takes.
- **Wants:** To know whose memories she has.
- **Flaw:** Lies easily and well, to protect herself.
- **Secret (writer only):** Some of Pip's memories are Oswin Hale's from the night of the Fall. She remembers who stopped him. Revealed in the main story by: flag `ch4_pip_remembers`.
- **Voice:** A child's voice, bright and quick, suddenly old and slow when she is remembering something that isn't hers. Sample: "I know this song. I don't know how I know it."
- **Relationship:** family with Bree Tallow: Bree is the closest she has to a mother.
- **Relationship:** unknown with The Hooded Stranger: The Stranger watches her more closely than anyone.
- **Speaks in Chapter 1:** `ch1_docks` (Under the Docks), `ch1_mayor` (Emberwick at Dusk)
- **Existing town barks** (`barks/pip_ashdown.json`): "I can run that for you! Two coppers. One if you're nice. Three if you're not." / "I didn't take it. I was holding it. For you. Here." / "Bree says I'm not to bother you. I'm not bothering. I'm standing." / "I know a quicker way round the docks. Don't ask how. I just do."

### Old Maud, Who Saw the Ember Fall (`old_maud`)

- **Lore preset:** `old_maud`. **Placed world preset:** `hermit`, variant `old_maud`. **Stands at:** (130, 90).
- **Role:** npc. **Faction:** none.
- **Summary:** A hermit on the burned bank who was there the night the ember fell, a hundred years ago.
- **Bio:** Maud was a child of seven on the night of the Fall and watched the ember drop into the river. She is now more than a hundred years old and has never used ember-fire, and so she remembers that night better than anyone alive. The town thinks she is mad. She has been waiting for someone to ask her the right question.
- **Wants:** For someone to listen, before she dies.
- **Flaw:** Speaks in riddles out of a century of habit.
- **Voice:** Ancient, papery, unhurried; a cackle that turns into a cough. Perfectly lucid underneath. Sample: "It didn't fall, dearie. It was thrown."
- **Relationship:** mentor with Corvin Ashby: Tells him the truth in pieces, to see if he's ready.
- **Speaks in Chapter 1:** `ch1_maud` (Who Saw the Ember Fall)
- **Existing town barks** (`barks/old_maud.json`): "Sit down, dearie. I've waited a hundred years, I can wait while you sit." / "Warm hands and empty heads. Ask yourself why, dearie." / "The river remembers. Ask it nicely and it might tell you." / "You've the right sort of face for questions. Ask me a better one." / "Oh, never get old, dearie. It's dreadful. Wouldn't miss it, mind." / "Mind the bank. It's softer than it was. Everything's softer than it was." / "Never touched a shard. Not once. That's why there's still all of me."

### Lark, The Last Songkeeper (`lark`)

- **Preset:** `minstrel`. **Stands at:** (29, 58).
- **Role:** npc. **Faction:** none.
- **Summary:** A travelling minstrel who keeps the Wardens' songs, the only history the fire couldn't burn.
- **Bio:** Lark travels the Vale singing the old Warden songs, which have survived because nobody needs ember-fire to remember a tune. The songs hold the Vale's real history, half-hidden in verses nobody listens to. Lark turns up wherever the party is going next, which is no coincidence: Lark is following the Hooded Stranger. In Lark's Warden version of the Song of the Seven, the seven 'came home'; the Skarrow clan sings that they 'came back running'. Same seven, seen from different doorsteps.
- **Wants:** To find the last verse of the Song of the Seven, which no one has sung in two centuries.
- **Flaw:** Treats everything as a story, including other people's pain.
- **Voice:** Androgynous, musical, warm; slips into song mid-sentence. Never quite serious. Sample: "Seven went over the Greywall and seven came home. The song doesn't say what they brought, but it does say it was heavy."
- **Relationship:** unknown with The Hooded Stranger: Following them through the Vale, for reasons of their own.

### Lady Celandine Marrowby, Lady of the Upper Fields (`celandine_marrowby`)

- **Preset:** `noble`. **Stands at:** (41, 68).
- **Role:** rival. **Faction:** emberwick.
- **Summary:** The richest woman in Emberwick, an elf whose family has been rich since the night the Keep fell.
- **Bio:** Celandine owns the granaries, the mill and half of Emberwick's debts. She is charming, generous in public, and entirely willing to let the goblins and the poor starve so long as the town owes her for the winter. The Marrowby fortune began on the night of the Fall, more than a century ago. Celandine is old enough to remember that night, and she does not. She has lit her halls with ember-lamps for a hundred years.
- **Wants:** To own Emberwick outright by spring.
- **Flaw:** Cannot imagine a problem money will not solve.
- **Secret (writer only):** Celandine was a young Warden on the night of the Fall and fled the Keep with a chest of ember shards. She has burned away every memory of it, and the Marrowby fortune is stolen fire she doesn't know she stole. Revealed in the main story by: flag `ch3_marrowby_truth`.
- **Voice:** Smooth, cultured alto with an elven lilt; amused; never raises her voice, because she never needs to. Sample: "Of course the town can have grain. Everything has a price; I'm simply the one who remembers it."
- **Relationship:** debt with Halden Varrow: Holds the mayor's debts, and his silence.
- **Relationship:** unknown with Rook: Rook's best customer and worst secret.
- **Speaks in Chapter 1:** `ch1_granary` (The Locked Granaries)
- **Existing town barks** (`barks/celandine_marrowby.json`): "Of course you may. Everything has a price. Shall we discuss yours?" / "Emberwick is so charming in the cold. Everyone becomes so reasonable." / "Do come up to the house. The lamps are lovely this time of year. Every time of year, really." / "My family has been generous to this town for a century. The town does keep forgetting." / "Grain, coin and patience. I have a great deal of all three. Most people have none." / "The mayor owes me rather a lot. He's sweet about it. Debtors usually are." / "The old days? Don't ask me, darling. I find them dreadfully dull."

### Mother Skarrow, Chief of the Skarrow Clan (`mother_skarrow`)

- **Where the story leaves her:** she **dies on the bridge** in the force ending (`ch1_skarrow_slain`): give her person `unless: ["ch1_skarrow_slain"]`. After the parley (`ch1_skarrow_spared`) she stays at her camp in the hills. Her world person uses the generic `goblin_shaman` base with the `mother_skarrow` variant.

- **Lore preset:** `mother_skarrow`. **Placed world preset:** `goblin_shaman`, variant `mother_skarrow`. **Stands at:** (134, 24).
- **Role:** npc. **Faction:** skarrow_clan.
- **Summary:** The goblin chief who holds the bridge at the ford, because her clan is starving.
- **Bio:** Mother Skarrow has led her clan for forty winters and is the oldest goblin in the hills. She took the bridge because Emberwick locked its granaries, and she will give it back for food, not coin. Her clan never used ember-fire, so its songs remember the Vale before the Hearthflame and the seven who crossed the Greywall. Whether the party talks to her or kills her is the first choice of the campaign, and it is remembered.
- **Wants:** To feed her clan through the winter.
- **Flaw:** Has buried too many grandchildren to trust anyone from the valley.
- **Secret (writer only):** The clan's oldest song names the seven who crossed the Greywall and says what they took. Revealed in the main story by: flag `ch1_skarrow_spared`.
- **Voice:** Cracked, rasping, dry humour; speaks the valley tongue slowly and deliberately, as a courtesy, and the hill tongue fast. Sample: "You lock your grain and call us thieves. Sit. We will talk about who stole what."
- **Relationship:** family with Vorga: Her grandson, who left the clan to take by force what she will only bargain for.
- **Relationship:** enemy with Halden Varrow: The man who locked the granaries.
- **Speaks in Chapter 1:** `ch1_bridge_cost` (What the Bridge Cost), `ch1_dawn` (Dawn at the Ford), `ch1_parley` (The Camp in the Hills), `ch1_road_east` (The Road East), `ch1_song` (The Clan's Fire), `ch1_young_ones` (The Young Ones)


## Other characters (not placed in Ashen Ford)

They can be talked **about**, but they aren't in the town to talk to.

- **Vorga** (`vorga`, villain): A goblin warlord holding the Sunscar canyon bridge, sure the valley will never share. Not in Ashen Ford, although his preset (`goblin_spear`) is used by the ford's plain goblin warriors. In Chapter 1 the clan's young hotheads at the river crossing "answer to Vorga" (the `ch1_river_crossing` beat).
- **Rook** (`rook`, rival; preset `bandit_captain`): **not placed yet.** If a quest needs him, add one person with `character: "rook"` at the docks or the Ash Market (near `af_ash_market_tally`), shown by flag. Tobin owes the Ash Market for his last shard, which makes a natural hook for "A Cold Forge". The smuggler who runs the trade in ember shards, and who sells to everyone. Bio: Rook runs the Ash Market from Emberwick's docks with a crew of river smugglers. Practical and unsentimental, Rook will sell the party information, shards and favours, and the Kindled exactly the same. Rook knows the market is selling more shards than exist in the Vale, and is starting to be afraid of whoever is supplying them. Wants: To be on the winning side when the embers run out. Flaw: Loyal only to the deal. Voice: Dry, low, unhurried; a river accent; every sentence sounds like an offer.
- **Ysolde** (`ysolde`, npc): The fey envoy of Mirewood, who trades in favours, names and forgotten things.
- **Maren Sorrel** (`maren_sorrel`, villain): The leader of the Kindled, who believes she must relight the Hearthflame to save the Vale, at any price. **Appears in Chapter 1** (ch1_kindler) as a scripted visitor, not a placed person; side content can mention them but mustn't give them new lines before their scenes.
- **Ivo Rime** (`ivo_rime`, villain): A necromancer studying the cold ember in Frostpeak, and the dead it raises.
- **Oswin Hale** (`oswin_hale`, villain): The last Warden-Keeper, undead for a century, who tried to put the flame out on the night of the Fall.
- **Ser Halvard** (`halvard`, villain): Oswin's oath-bound champion, guarding a keep that no longer exists.
- **The Hooded Stranger** (`hooded_stranger`, npc): A masked traveller who knows far too much about the embers and keeps turning up where the party is going. **Appears in Chapter 1** (ch1_four_more, ch1_maud, ch1_mayor) as a scripted visitor, not a placed person; side content can mention them but mustn't give them new lines before their scenes.

## Townsfolk without a name (barks only)

These presets are scenery people. Write at least 3 unconditional bark lines each in `lore/barks/townsfolk.json`, plus flag-conditioned lines that react to Chapter 1: before and after the ford choice, force against parley, and after the ember is found. The descriptions are a starting point for their voice, in the town's mood: frightened, stubborn, a hard winter coming, the warm ground cooling.

- **`barmaid`** (2 placed): Works Bree's inn; hears everything and repeats half of it.
- **`child`** (3 placed): Emberwick's children. Excited by adventurers, scared of the goblins, repeat what grown-ups say. (One of the three may be Pip; the others aren't.)
- **`farmer`** (5 placed): Works the fields round Emberwick. Worried about the harvest and the cold creeping into the ground; plain-spoken.
- **`farmhand`** (3 placed): Young, tired, underpaid; gossips about who's hoarding grain.
- **`fisher`** (4 placed): Works the jetties on the steaming river; superstitious about the ember in the water, and noticing fewer fish.
- **`goblin_archer`** (3 placed): A Skarrow clan archer at the ford. Hungry and wary. After a parley, a grudging neighbour; after the force ending, gone (OW-07).
- **`goblin_cutthroat`** (2 placed): A sly Skarrow clan skirmisher who mocks the townsfolk. After a parley, a grudging neighbour; after the force ending, gone (OW-07).
- **`goblin_spear`** (6 placed): A Skarrow clan warrior at the ford. Hungry and wary. After a parley they're grudging neighbours; after the force ending they're gone (OW-07).
- **`guard`** (6 placed): One of Dagna's eight guards. Lost the bridge and is ashamed of it; loyal to the captain; nervous at night.
- **`merchant`** (6 placed): Market stallholder; prices are up, the road east is shut, and they blame the goblins (or, quietly, the Marrowbys).

## The three side quests against the lore

Check each against the beats above, so side content never spoils or contradicts the main story.

1. **"Where the Grain Went"** (Halden Varrow).
   - **Careful:** Halden's secret is that he **locked the granaries himself** and has known for six years that the ground is cooling. It's revealed in `ch1_granary` (`ch1_halden_confessed`).
   - **Before that flag:** the quest is about grain going missing (the slit sack, the Ash Market tally, the Marrowby loan), and Halden deflects. He "will always pick the lie that keeps the peace".
   - **After `ch1_halden_confessed`:** the ending can turn on that confession.
   - Celandine's secret (the Marrowby fortune comes from shards she carried out of the Keep) must **not** be revealed. A Persuasion or Insight success may only hint that the Marrowbys are hiding something old.
2. **"A Cold Forge"** (Tobin Fell).
   - Tobin's flaw is that he "would buy a shard from anyone, no questions asked". His relationship with the Ash Market is a debt for his last shard. That makes a clean moral choice: help him find fuel honestly (oil or dry wood from the docks), or put him in touch with Rook.
   - Either way, don't let the party relight his forge with a real ember before `ch1_ember_found`.
3. **"The Guard's Whistle"** (Pip Ashdown).
   - The whistle on Annet's grave (`af_grave_annet`) belonged to a guard who died holding the bridge. Pip is cheerful and breathless in her voice sample, and this quest is her quiet moment.
   - Pip's secret (Oswin Hale's memories, revealed in Chapter 4) must not surface. At most she "remembers" something she can't explain, which the lore already allows.

Every quest must work after force, after parley, and after a loss at the ford. Use the choice flags above.
