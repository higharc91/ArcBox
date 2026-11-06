namespace Tactics.Logic.Tests;

public sealed class AiRoleRoutingTests
{
    [Theory]
    [InlineData("controller", "control", "caster", "controller")]
    [InlineData("guardian", "defender", "melee", "guardian")]
    [InlineData("control", "caster", "ranged", "caster")]
    public void Explicit_specialist_roles_take_priority_without_changing_general_casters(
        string first, string second, string third, string expected)
        => Assert.Equal(expected, AiProfile.IdForRoles([first, second, third]));
}
