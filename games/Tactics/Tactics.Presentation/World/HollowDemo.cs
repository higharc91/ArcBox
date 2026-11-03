using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>The real bounty locations and stamped fights, through the TV's normal renderer. No campaign is written.</summary>
public static class HollowDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string root)
    {
        var rules = RulesPack.Load([Path.Combine(root, "rules")], ArtManifests.Load(root));
        var lore = LoreBook.Load(root);
        var basis = ThingsDemo.Steps()[0].View with { PartyLevel = 2 };
        var now = DateTimeOffset.UtcNow;
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];
        var party = classes.Select((cls, i) => new WalkerView("pr_member" + (i + 1), cls, "characters.class_" + cls, true, i == 0)).ToImmutableArray();
        var steps = new List<ThingsDemoStep>();
        var seq = 0;
        ExploreView Explore(string region, Tile at, int epoch) => basis.Explore! with
        {
            Region = region, Name = rules.RegionFor(region)!.Def.Name, Epoch = epoch, Seq = ++seq,
            At = at, Facing = new Tile(at.X + 1, at.Y + 1), Party = party, Leader = party[0].Id, Legs = [],
            Things = [.. rules.InteractablesIn(region).Where(t => t.Hidden is null && t.Id != "hollow_ember_claw")
                .Select(t => ThingView.Of(t, Interactables.Initial(t), new HashSet<string>(["ch1_ford_settled", "sq_ashmaw_posted", "sq_ashmaw_accepted", "sq_ashmaw_entered"])))],
            ThingEvents = null, Cast = [], Markers = [], Plugs = null, Traders = null, QuestMarkers = null,
            PeopleFlags = ["ch1_ford_settled", "sq_ashmaw_posted", "sq_ashmaw_accepted", "sq_ashmaw_entered"],
            QuestEvent = null, QuestLog = null, Pinned = null,
        };
        void Add(string name, double time, TacticsHostView view, double shot = 1)
            => steps.Add(new(name, time, view, [(shot, name)], false));
        TacticsHostView World(ExploreView explore) => basis with { Phase = nameof(TacticsPhase.Exploration), Explore = explore, Battle = null, Story = null, Notice = null };
        var board = Explore("ashen_ford", new(37, 61), 1);
        Add("hollow-bounty-board", 0, World(board));
        Add("hollow-cliff-entrance", 5, World(Explore("ashen_ford", new(131, 99), 2)));
        Add("hollow-arrival", 10, World(Explore("cinder_hollow", new(5, 2), 3)));
        var grisk = Explore("cinder_hollow", new(41, 10), 4) with { Party = party.Add(new("grisk-preview", "Grisk", "monsters.kobold_foreman", true, false)) };
        Add("hollow-grisk-world", 16, World(grisk));
        Add("hollow-grisk-walk", 19, World(grisk with { Seq = (seq += 2), At = new(43, 10), Facing = new(44, 10), Legs = [new(seq - 1, new(42, 10), 700), new(seq, new(43, 10), 700)] }), .8);
        AddFight("hollow_diggings", "grisk", "grisk.dig_faster", "hollow-grisk-fight", 24, new(8, 5));
        Add("hollow-safe-fire", 35, World(Explore("cinder_hollow", new(28, 33), 6)));
        var ash = Explore("cinder_hollow", new(14, 32), 7) with { Party = party.Add(new("ashmaw-preview", "Ashmaw", "monsters.ashmaw", true, false)) };
        Add("hollow-ashmaw-world", 40, World(ash));
        Add("hollow-ashmaw-walk", 43, World(ash with { Seq = (seq += 2), At = new(16, 32), Facing = new(17, 32), Legs = [new(seq - 1, new(15, 32), 850), new(seq, new(16, 32), 850)] }), 1);
        AddFight("hollow_lair", "ashmaw", "ashmaw.cinder_roar", "hollow-ashmaw-fight", 48, new(4, 6));
        var quest = lore.Quests.Single(q => q.Id == "sq_ashmaw");
        var gifts = classes.Select((cls, i) => new TrinketGiftView(party[i].Id, party[i].Name, cls, quest.Rewards.Trinkets![cls], rules.Trinkets[quest.Rewards.Trinkets![cls]].Name, true)).ToImmutableArray();
        var returned = Explore("ashen_ford", new(44, 64), 9);
        Add("hollow-return-to-dagna", 58, World(returned), 10);
        Add("hollow-trinkets-presented", 72, World(returned with
        { QuestEvent = new(1, quest.Id, quest.Name, "complete", DateTimeOffset.MaxValue, 60, 300, gifts) }), 5);
        return steps;

        void AddFight(string encounter, string monster, string ability, string label, double time, Tile at)
        {
            ImmutableArray<PartyMember> members = [.. classes.Select((cls, i) => new PartyMember(new UnitId(party[i].Id),
                SheetBuilder.DefaultCharacter(cls, "human", cls, null, 2, rules), new AiControlled("companion")))];
            var state = Battle.Start(Encounters.Setup(encounter, members, Difficulty.Normal, rules), rules, 74).State;
            var stamp = rules.RegionFor("cinder_hollow")!.Def.Stamps.Single(s => s.Map == encounter);
            var map = rules.MapFor(encounter)!;
            var occupied = state.Units.Values.Where(u => u.Id.Value != monster && u.Id.Value != party[0].Id).Select(u => u.Pos).ToHashSet();
            bool Free(Tile tile) => map.InBounds(tile) && !map[tile].Flags.HasFlag(TileFlags.Impassable)
                && !map.Props.Any(p => p.At == tile && p.Blocks)
                && !occupied.Any(p => Math.Max(Math.Abs(p.X - tile.X), Math.Abs(p.Y - tile.Y)) < 2);
            at = (from y in Enumerable.Range(1, map.Height - 2) from x in Enumerable.Range(1, map.Width - 2)
                  let tile = new Tile(x, y) where Free(tile) && Free(new(x + 2, y))
                  orderby Math.Abs(x - at.X) + Math.Abs(y - at.Y), y, x select tile).First();
            var heroAt = new Tile(at.X + 2, at.Y);
            var units = state.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite,
                u.Id.Value == monster ? at : u.Id.Value == party[0].Id ? heroAt : u.Pos,
                u.Hp, u.Sheet.MaxHp, 0, "Up", 0, [], null)).ToImmutableArray();
            var battle = new BattleView(encounter, state.MapId, 1, state.Order, new UnitId(monster), units, [], null,
                BoardChanges.Of(state), state.Objectives.Current, Name: rules.Encounters[encounter].Name,
                Stage: new("cinder_hollow", stamp.At, stamp.Height, [.. party.Select(p => p.Id)]));
            var world = Explore("cinder_hollow", new Tile(stamp.At.X + heroAt.X, stamp.At.Y + heroAt.Y), encounter == "hollow_lair" ? 8 : 5);
            var view = basis with { Phase = nameof(TacticsPhase.Battle), Explore = world, Battle = battle, Story = null, Notice = null };
            Add(label, time, view, 3);
            var used = new AbilityUsed(new(monster), new(ability), new Target(Unit: new UnitId(monster)), rules.Abilities[new AbilityId(ability)].Presentation);
            Add(label + "-action", time + 5, view with { Battle = battle with { Beats = [new Beat(1, new UnitId(monster), [used])] } }, .35);
            if (monster == "ashmaw")
            {
                var shake = new AbilityId("ashmaw.ash_shake");
                var tiles = Areas.Tiles(rules.Abilities[shake].Target.Area!, at, at,
                    new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, []));
                var zone = new Zone(1, new UnitId(monster), "burning", tiles, 2, false, false, [], Surface: true);
                var phase = battle with
                {
                    Units = [.. units.Select(u => u.Id.Value == monster ? u with { Hp = u.MaxHp / 2 } : u)],
                    Board = BoardChanges.Of(state) with { Zones = [new(1, "burning", tiles)] },
                    Beats = [new Beat(2, new UnitId(monster),
                        [new AbilityUsed(new(monster), shake, new Target(Unit: new UnitId(monster)), rules.Abilities[shake].Presentation),
                         new ZoneCreated(zone)])],
                };
                Add(label + "-burning", time + 8, view with { Battle = phase }, 1.4);
            }
            else
            {
                // Show the held pick through an actual attack pose as well as his self-directed shout.
                var target = new UnitId(party[0].Id);
                var attack = new AbilityUsed(new(monster), new("common.attack"), new Target(Unit: target), new("monsters.attack", null, null));
                Add(label + "-pick", time + 8, view with { Battle = battle with
                {
                    Units = [.. units.Select(u => u.Id == target ? u with { At = new(at.X + 1, at.Y) } : u)],
                    Beats = [new Beat(2, new UnitId(monster), [attack, new AttackResolved(new(monster), target, true, false)])],
                } }, .35);
            }
        }
    }
}
