using System.Text.Json;

namespace Tactics.Logic;

/// <summary>
/// <c>animate_dead</c> (args <c>{}</c>, aimed at a tile within the ability's range): raises a <see cref="Skeleton"/> for
/// the user (<see cref="Summons.Spawn"/>, for the whole battle) from the corpse of a dead enemy (not a summon) on the aimed
/// tile, if the tile is free. Each corpse rises once (it is marked <see cref="Animated"/>).
/// </summary>
public sealed class AnimateDead : INamedHook
{
    /// <summary>The monster that rises.</summary>
    public const string Skeleton = "skeleton";

    /// <summary>The mark on a corpse that has been raised.</summary>
    public static readonly ConditionId Animated = new("animated");

    /// <inheritdoc />
    public string Name => "animate_dead";

    /// <inheritdoc />
    public bool OnTile => true;

    /// <inheritdoc />
    public void Run(JsonElement args, EffectContext ctx)
    {
        if (ctx.Aim is not { } aim)
            return;
        var source = ctx.SourceUnit;
        var corpse = ctx.W.State.Units.Values.FirstOrDefault(u => u.Life == LifeState.Dead && u.Pos == aim && u.SummonedBy is null
                                                                 && Movement.Hostile(source, u) && u.Conditions.All(c => c.Id != Animated));
        if (corpse is null || !Motion.CanLand(ctx.W, aim, source.Id))
            return;
        ctx.W.Set(corpse with
        {
            Conditions = corpse.Conditions.Add(new ActiveCondition(Animated, source.Id, null, UntilSave: false, SaveDc: 0, Stat.Con,
                Concentration: false)),
        });
        Summons.Spawn(ctx.W, source.Id, Skeleton, aim, rounds: null, concentration: false);
    }

    /// <inheritdoc />
    public IEnumerable<string> Monsters(JsonElement args) => [Skeleton];

    /// <inheritdoc />
    public void Validate(JsonElement args, Action<string> error) => NamedHooks.RequireObject(Name, args, error);
}
