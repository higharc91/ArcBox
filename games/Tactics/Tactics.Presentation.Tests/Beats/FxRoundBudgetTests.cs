using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

public sealed class FxRoundBudgetTests
{
    [Fact]
    public void Six_hero_chapter_one_rounds_preserve_rules_and_stay_within_the_proposed_cinematic_budget()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var art = FxArt.Load(Pack.Root);
        string[] classes = ["fighter", "cleric", "rogue", "wizard", "barbarian", "bard"];
        ImmutableArray<PartyMember> party = [.. classes.Select((cls, i) => new PartyMember(new($"hero{i + 1}"),
            SheetBuilder.DefaultCharacter(cls, "human", cls, null, 1, rules), new AiControlled("companion")))];
        var setup = Encounters.Setup("river_crossing", party, Difficulty.Normal, rules);
        var rows = new List<object>();
        var complete = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var state = Battle.Start(setup, rules, seed).State;
            var totals = new Dictionary<(int Round, BattleCinematics Mode), double>();
            var carried = new Dictionary<BattleCinematics, CutInFrame?>();
            var actions = 0;
            while (state.Outcome is null && state.Round <= 3 && actions++ < 300)
            {
                var actor = state.Active!.Value;
                var plan = Ai.Plan(state, actor, rules);
                var advanced = false;
                foreach (var command in plan.Commands)
                {
                    var result = Battle.Apply(state, command, rules);
                    if (result.Rejected is not null) break; // a path interrupted by an OA requires a fresh plan
                    Measure(state, result, actor);
                    state = result.State;
                    advanced = true;
                    Assert.Null(state.Pending); // AI controls use the engine's real reaction policy
                    if (state.Outcome is not null || state.Active != actor
                        || command is Move move && state.Units[actor].Pos != move.Path[^1]) break;
                }
                if (!advanced && state.Outcome is null && state.Active == actor)
                {
                    var result = Battle.Apply(state, new EndTurn(actor), rules);
                    Assert.Null(result.Rejected);
                    Measure(state, result, actor);
                    state = result.State;
                }
            }
            Assert.True(actions < 300);
            for (var round = 1; round < Math.Min(state.Round, 4); round++)
            {
                var full = totals.GetValueOrDefault((round, BattleCinematics.Full));
                var automatic = totals.GetValueOrDefault((round, BattleCinematics.Short));
                Assert.Equal(0, totals.GetValueOrDefault((round, BattleCinematics.Off)));
                Assert.InRange(automatic, 0, 3.0 + 1e-6); // measured fixture bounds; the owner chooses the product cap
                Assert.InRange(full, automatic, 3.3 + 1e-6);
                rows.Add(new { Seed = seed, Round = round, Full = full, AutoSixPlayers = automatic, Off = 0 });
                complete++;
            }

            void Measure(BattleState before, Resolution result, UnitId actor)
            {
                var old = View(before, []);
                var beat = new Beat(actions, actor, result.Events);
                var next = View(result.State, [beat]);
                var script = BeatScript.Of(beat, old, next, art);
                var terrain = new BoardTerrain(setup.Map, old.Board);
                foreach (var mode in Enum.GetValues<BattleCinematics>())
                {
                    var playback = CutInPlayback.For(beat, old, script, rules, terrain, mode, incoming: carried.GetValueOrDefault(mode));
                    totals[(before.Round, mode)] = totals.GetValueOrDefault((before.Round, mode)) + (playback?.Extra ?? 0);
                    carried[mode] = playback?.HoldsForReaction == true ? playback.Frame(playback.Length) : null;
                }
            }
        }
        Assert.True(complete >= 20);
        if (Environment.GetEnvironmentVariable("ARCBOX_FX_BUDGET_REPORT") is { Length: > 0 } path)
            File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));

        BattleView View(BattleState s, ImmutableArray<Beat> beats) => new("budget", setup.Map.Id, s.Round, s.Order, s.Active,
            [.. s.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos,
                u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [], null))], beats, null, BoardChanges.Of(s));
    }
}
