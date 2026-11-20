namespace Meridian.Logic.Tests.Content;

/// <summary>Scenarios declare their tokens' and counters' looks (M7 plan, Task 1); the loader keeps them consistent.</summary>
public sealed class ScenarioLookTests
{
    private static IReadOnlyList<ContentError> Check(params ScenarioDef[] defs)
        => Validation.Check(BasePack.Pack.WithScenarios(BasePack.Pack.Scenarios, BasePack.Pack.Breach, defs));

    private static ScenarioDef Def(string id, Dictionary<string, TokenLook>? tokens = null, Dictionary<string, string?>? counters = null)
        => StubScenario.Def(id) with { Tokens = tokens, Counters = counters };

    [Fact]
    public void The_shipped_scenarios_declare_their_looks_consistently()
        => Assert.Empty(Validation.Check(BasePack.Pack));

    [Fact]
    public void A_kind_that_looks_different_in_two_scenarios_is_an_error()
    {
        var errors = Check(Def("one", new() { ["beacon"] = new("B", "#ffffff") }), Def("two", new() { ["beacon"] = new("B", "#000000") }));
        Assert.Contains(errors, e => e.Where == "tokens/beacon" && e.Message.Contains("one, two"));
        Assert.DoesNotContain(Check(Def("one", new() { ["beacon"] = new("B", "#ffffff") }), Def("two", new() { ["beacon"] = new("B", "#ffffff") })),
                              e => e.Where.StartsWith("tokens/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_counter_labelled_two_ways_is_an_error()
        => Assert.Contains(Check(Def("one", counters: new() { ["lit"] = "Lit" }), Def("two", counters: new() { ["lit"] = null })),
                           e => e.Where == "counters/lit");

    [Theory]
    [InlineData("", "#ffffff")]
    [InlineData("ABC", "#ffffff")]
    [InlineData("B", "white")]
    public void A_look_needs_a_short_label_and_a_hex_colour(string label, string colour)
        => Assert.Contains(Check(Def("one", new() { ["beacon"] = new(label, colour) })), e => e.Where == "tokens/beacon");

    [Fact]
    public void A_state_look_is_checked_too()
        => Assert.Contains(Check(Def("one", new() { ["beacon"] = new("B", "#ffffff", States: new Dictionary<string, TokenLook> { ["lit"] = new("L", "red") }) })),
                           e => e.Where == "tokens/beacon/lit");

    /// <summary>
    /// Every token kind and counter a scenario creates in play is declared in its own content, so the TV shows each with
    /// its look and label, and the art check lists it.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllScenarios.Ids), MemberType = typeof(AllScenarios))]
    public void Everything_a_scenario_places_is_declared_in_its_content(string scenario)
    {
        var def = BasePack.Pack.ScenarioDef(scenario);
        var kinds = new HashSet<string>();
        var counters = new HashSet<string>();
        foreach (var players in new[] { 3, 6 })
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var pack = ScenarioRun.Pack(scenario);
                var state = Rules.NewGame(pack, BotRunner.Seats(pack, players), seed);
                foreach (var step in BotRunner.PlayToEnd(pack, state, seed, 4000))
                {
                    if (step.Result.State.Board is { } board)
                    {
                        kinds.UnionWith(board.Tokens.Select(t => t.Kind));
                        counters.UnionWith(board.Counters.Select(c => c.Id));
                    }
                }
            }
        }
        var declared = def.Counters ?? new Dictionary<string, string?>();
        Assert.DoesNotContain(kinds, k => k != Token.DroppedItem && !(def.Tokens?.ContainsKey(k) ?? false));
        Assert.DoesNotContain(counters, c => !declared.ContainsKey(c) && !declared.Keys.Any(p => p.EndsWith('-') && c.StartsWith(p, StringComparison.Ordinal)));
    }
}
