using System.Text.Json;

namespace Tactics.Logic;

/// <summary>
/// <c>polymorph</c> (args <c>{}</c>): the target takes the <see cref="Sheep"/> sheet (<see cref="Transforms.Into"/>) with
/// <see cref="SheepTempHp"/> temporary HP, and the condition <see cref="Transforms.Marker"/> from the user: a WIS save
/// against the user's save DC at the end of each of its turns ends it, as does the user's concentration ending when the
/// ability needs concentration. It also reverts when the temporary HP run out. Emits <see cref="ConditionApplied"/>.
/// </summary>
public sealed class Polymorph : INamedHook
{
    /// <summary>The monster the target becomes.</summary>
    public const string Sheep = "sheep";

    /// <summary>The temporary HP the form has.</summary>
    public const int SheepTempHp = 10;

    /// <inheritdoc />
    public string Name => "polymorph";

    /// <inheritdoc />
    public void Run(JsonElement args, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Name).Id;
        Transforms.Into(ctx.W, target, NamedHooks.Monster(ctx, Name, Sheep), SheepTempHp);
        var unit = ctx.W.Unit(target);
        var marker = new ActiveCondition(Transforms.Marker, ctx.Source, RoundsLeft: null, UntilSave: true, ctx.SaveDc, Stat.Wis,
            Concentration: ctx.Ability?.Cost.Concentration == true, AppliedThisTurn: ctx.W.State.Active == ctx.Source);
        ctx.W.Set(unit with { Conditions = unit.Conditions.RemoveAll(c => c.Id == Transforms.Marker).Add(marker) });
        ctx.W.Emit(new ConditionApplied(target, Transforms.Marker));
    }

    /// <inheritdoc />
    public IEnumerable<string> Monsters(JsonElement args) => [Sheep];

    /// <inheritdoc />
    public void Validate(JsonElement args, Action<string> error) => NamedHooks.RequireObject(Name, args, error);
}
