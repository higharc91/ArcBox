using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class SurfaceModelTests
{
    [Theory]
    [InlineData(1)] [InlineData(7)] [InlineData(19)] [InlineData(37)]
    [InlineData(67)] [InlineData(101)] [InlineData(211)] [InlineData(397)]
    [InlineData(601)] [InlineData(997)] [InlineData(2003)] [InlineData(4099)]
    public void Random_surfaces_are_bounded_and_replay_after_every_command(int seed)
    {
        var random = new Random(seed);
        string[] materials = ["grass/lush", "grass/dry", "water/clear"];
        const int width = 6, height = 4;
        var bytes = new byte[width * height * 3];
        var fuel = new HashSet<Tile>();
        var webs = new List<Tile>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var tile = new Tile(x, y);
                var material = tile == new Tile(0, 0) || tile == new Tile(5, 3) ? 0 : random.Next(3);
                var i = (y * width + x) * 3;
                bytes[i + 1] = (byte)material;
                bytes[i + 2] = (byte)(material == 2 ? TileFlags.Water : TileFlags.None);
                if (material == 1) fuel.Add(tile);
                if (material != 2 && random.Next(4) == 0) webs.Add(tile);
            }
        var map = BattleMap.FromBytes("model", width, height, bytes, [.. materials], [], Ordinal.Map<ImmutableArray<Tile>>());
        var spells = new[] { "fire", "cold", "lightning" }.Select(type => new AbilityDef(new AbilityId("model." + type), type, 0,
            new CostDef(Action: 1), new TargetDef(TargetKind.Tile, 8, false, new AreaDef(AreaShape.Burst, 1)),
            [new DamageEffect(TestAbilities.Flat("4"), type, [])])).ToArray();
        var sheet = (TestSheets.Cleric with { MaxHp = 10000, Move = 8 }).Knowing(spells);
        var battle = TestBattle.On(map).WithEnvironment(BasePackTests.Rules.Environment).With(spells)
            .WithConditions(BasePackTests.Rules.Conditions[new ConditionId("prone")], BasePackTests.Rules.Conditions[new ConditionId("restrained")])
            .Party("a", sheet, new Tile(0, 0)).Enemy("b", sheet, new Tile(5, 3));
        var state = battle.Start((ulong)seed).State;
        if (webs.Count > 0)
        {
            var web = BasePackTests.Rules.Abilities[new AbilityId("wizard.web")].Effects.OfType<ZoneEffect>().Single();
            state = state with { Zones = [new Zone(0, new UnitId("a"), "web", [.. webs], 1000, false, true, web.Passives)], NextSerial = 1 };
        }
        var potentialFuel = fuel.Union(webs).ToHashSet();
        var burnt = new HashSet<Tile>();
        var tileRounds = new HashSet<(Tile, int)>();
        void Apply(Command command)
        {
            var saved = JsonSerializer.Deserialize<BattleState>(JsonSerializer.Serialize(state, ArcJson.Options), ArcJson.Options)!;
            var direct = battle.Apply(state, command);
            var loaded = battle.Apply(saved, command);
            Assert.Equal(JsonSerializer.Serialize(direct.State, ArcJson.Options), JsonSerializer.Serialize(loaded.State, ArcJson.Options));
            Assert.Equal(JsonSerializer.Serialize(direct.Events, ArcJson.Options), JsonSerializer.Serialize(loaded.Events, ArcJson.Options));
            foreach (var created in direct.Events.OfType<ZoneCreated>().Where(e => e.Zone.Kind == "burning"))
                foreach (var tile in created.Zone.Tiles)
                    Assert.True(burnt.Add(tile), $"Seed {seed}: {tile} burned twice");
            state = direct.State;
            Invariants.Check(state, battle.Rules);
            foreach (var tile in state.Zones.Where(z => z.Kind == "burning").SelectMany(z => z.Tiles))
                tileRounds.Add((tile, state.Round));
            Assert.True(tileRounds.Count <= 2 * potentialFuel.Count);
        }
        void PassReactions()
        {
            while (state.Pending is ReactionOffer offer)
                Apply(new React(offer.Reactor, offer.Options.Single(o => o.Id == Reactions.Pass)));
        }
        var count = int.TryParse(Environment.GetEnvironmentVariable("SURFACE_MODEL_EVENTS"), out var configured) ? configured : 128;
        for (var n = 0; n < count; n++)
        {
            PassReactions();
            var active = state.Active!.Value;
            Command command = random.Next(3) switch
            {
                0 => new Use(active, spells[random.Next(spells.Length)].Id, new Target(Tile: new Tile(random.Next(width), random.Next(height)))),
                1 => new Move(active, [state.Units[active].Pos, Surfaces.Neighbours(state.Units[active].Pos).ElementAt(random.Next(4))]),
                _ => new EndTurn(active),
            };
            if (Battle.Check(state, command, battle.Rules) is not null)
                command = new EndTurn(active);
            Apply(command);
        }
        PassReactions();
        // No further ignition: existing fronts have at most the remaining fuel graph's diameter plus two rounds.
        var scorched = state.Zones.Where(z => z.Kind == "scorched").SelectMany(z => z.Tiles).ToHashSet();
        var remaining = fuel.Except(scorched).Union(state.Zones.Where(z => z.Kind == "burning").SelectMany(z => z.Tiles)).ToHashSet();
        var limit = state.Round + Diameter(remaining) + 2;
        while (state.Round < limit)
        {
            PassReactions();
            Apply(new EndTurn(state.Active!.Value));
        }
        Assert.DoesNotContain(state.Zones, z => z.Kind == "burning");
    }

    private static int Diameter(HashSet<Tile> tiles)
    {
        var diameter = 0;
        foreach (var start in tiles)
        {
            var visited = new HashSet<Tile> { start };
            var queue = new Queue<(Tile, int)>();
            queue.Enqueue((start, 0));
            while (queue.TryDequeue(out var item))
            {
                diameter = Math.Max(diameter, item.Item2);
                foreach (var next in Surfaces.Neighbours(item.Item1))
                    if (tiles.Contains(next) && visited.Add(next)) queue.Enqueue((next, item.Item2 + 1));
            }
        }
        return diameter;
    }
}
