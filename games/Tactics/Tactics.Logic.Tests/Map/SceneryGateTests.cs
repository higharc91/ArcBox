using System.Collections.Immutable;
namespace Tactics.Logic.Tests.Map;

public sealed class SceneryGateTests
{
    [Fact]
    public void Gated_props_keep_value_equality_across_saved_state_round_trips()
    {
        var original = new PropPlacement("gate", new Tile(0, 0), true, Cover.Half,
            Requires: ["ch1_ford_settled"], Unless: ["ch1_skarrow_spared"]);
        var json = System.Text.Json.JsonSerializer.Serialize(original, ArcBox.Sdk.ArcJson.Options);
        var copy = System.Text.Json.JsonSerializer.Deserialize<PropPlacement>(json, ArcBox.Sdk.ArcJson.Options)!;
        Assert.Equal(original, copy);
        Assert.Equal(original.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(original, original with { Unless = ["ch1_skarrow_slain"] });
    }
    [Fact]
    public void Choosing_force_does_not_remove_the_checkpoint_until_the_battle_is_won()
    {
        var gate=new PropPlacement("gate",new Tile(0,0),true,Cover.Half,Unless:["ch1_ford_settled"]);
        Assert.True(gate.Present(new HashSet<string>{"ch1_skarrow_slain"}));
        Assert.False(gate.Present(new HashSet<string>{"ch1_skarrow_slain","ch1_ford_settled"}));
    }
    [Fact]
    public void Outcome_dressing_requires_both_a_won_crossing_and_the_matching_branch()
    {
        var force=new PropPlacement("wreckage",new Tile(0,0),false,Cover.None,Requires:["ch1_ford_settled","ch1_skarrow_slain"]);
        Assert.False(force.Present(new HashSet<string>{"ch1_skarrow_slain"}));
        Assert.False(force.Present(new HashSet<string>{"ch1_ford_settled","ch1_skarrow_spared"}));
        Assert.True(force.Present(new HashSet<string>{"ch1_ford_settled","ch1_skarrow_slain"}));
    }
    [Fact]
    public void Legacy_props_stay_present_without_story_flags()
    {
        Assert.True(new PropPlacement("crate",new Tile(0,0),true,Cover.Half).Present(ImmutableHashSet<string>.Empty));
    }
}
