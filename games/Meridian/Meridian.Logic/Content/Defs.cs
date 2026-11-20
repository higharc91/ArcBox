using System.Text.Json.Serialization;

namespace Meridian.Logic;

// Content definitions: what content/meridian/base/rules/*.json holds. These records are also the JSON shape (camelCase
// through ArcJson.Options), so there is no separate DTO layer except for card files (see PackLoader).

/// <summary>One trait's 8-step track. <see cref="Start"/> is a 1-based step.</summary>
public sealed record TraitTrack(EqArray<int> Values, int Start)
{
    public int ValueAt(int step) => Values[step - 1];
}

public sealed record TrackSet(TraitTrack Brawn, TraitTrack Agility, TraitTrack Tech, TraitTrack Nerve)
{
    public TraitTrack this[Trait trait] => trait switch
    {
        Trait.Brawn => Brawn,
        Trait.Agility => Agility,
        Trait.Tech => Tech,
        Trait.Nerve => Nerve,
        _ => throw new ArgumentOutOfRangeException(nameof(trait)),
    };
}

public sealed record CrewDef(CrewId Id, string Name, string Role, TrackSet Tracks);

/// <summary>A compartment tile (spec §2.3). <see cref="Hatches"/> are at rotation 0.</summary>
public sealed record TileDef(
    TileId Id,
    string Name,
    EqArray<DeckId> Decks,
    EqArray<Side> Hatches,
    CardKind? Symbol = null,
    int ExitCost = 0,
    TileRule? Rule = null,
    int Copies = 1);

/// <summary>A compartment's own rule.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HealRule), "heal")]
[JsonDerivedType(typeof(DropToRule), "dropTo")]
public abstract record TileRule;

/// <summary>Restores up to <paramref name="Steps"/> steps in <paramref name="Traits"/>, never above a trait's start.</summary>
public sealed record HealRule(TraitGroup Traits, int Steps, bool OncePerCrew = true) : TileRule;

/// <summary>A one-way drop to <paramref name="Deck"/>'s start compartment, added when the tile is placed.</summary>
public sealed record DropToRule(DeckId Deck) : TileRule;

/// <summary>A trait, or a group of them. Effects that name a group pick the trait within it (see Effects).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TraitGroup>))]
public enum TraitGroup
{
    [JsonStringEnumMemberName("brawn")] Brawn,
    [JsonStringEnumMemberName("agility")] Agility,
    [JsonStringEnumMemberName("tech")] Tech,
    [JsonStringEnumMemberName("nerve")] Nerve,
    [JsonStringEnumMemberName("physical")] Physical,
    [JsonStringEnumMemberName("mental")] Mental,
}

public static class TraitGroups
{
    public static EqArray<Trait> Members(this TraitGroup group) => group switch
    {
        TraitGroup.Brawn => [Trait.Brawn],
        TraitGroup.Agility => [Trait.Agility],
        TraitGroup.Tech => [Trait.Tech],
        TraitGroup.Nerve => [Trait.Nerve],
        TraitGroup.Physical => Traits.Physical,
        TraitGroup.Mental => Traits.Mental,
        _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };
}

/// <summary>
/// What a card band, choice option, item or compartment can do. Deliberately small: anything a scenario needs beyond
/// it is C# in that scenario (spec §4.1). A <see cref="IsTerminal">terminal</see> effect may only come last in a list,
/// because it can open a wait or end the turn.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Gain), "gain")]
[JsonDerivedType(typeof(Lose), "lose")]
[JsonDerivedType(typeof(DrawCard), "draw")]
[JsonDerivedType(typeof(MoveToTile), "moveTo")]
[JsonDerivedType(typeof(ExtraDice), "extraDice")]
[JsonDerivedType(typeof(Moves), "moves")]
[JsonDerivedType(typeof(EndTurnNow), "endTurn")]
public abstract record Effect
{
    [JsonIgnore]
    public virtual bool IsTerminal => false;
}

/// <summary>Gain steps, each to the lowest-step trait in the group (ties: the earlier trait).</summary>
public sealed record Gain(TraitGroup Traits, int Steps) : Effect;

/// <summary>Lose steps, each from the highest-step trait in the group (ties: the earlier trait).</summary>
public sealed record Lose(TraitGroup Traits, int Steps) : Effect;

public sealed record DrawCard(CardKind Card) : Effect
{
    public override bool IsTerminal => true;
}

/// <summary>Moves the crew member to the first placed compartment of that tile, at no cost. Nothing if none is placed.</summary>
public sealed record MoveToTile(TileId Tile) : Effect;

/// <summary>Passive only: extra dice on checks of that trait while the item is carried.</summary>
public sealed record ExtraDice(Trait Trait, int Dice) : Effect;

/// <summary>Adds (or removes) movement points this turn. Never below zero.</summary>
public sealed record Moves(int Delta) : Effect;

public sealed record EndTurnNow : Effect
{
    public override bool IsTerminal => true;
}

/// <summary>An outcome band of a check: totals from <see cref="Min"/> to <see cref="Max"/> (null: and above).</summary>
public sealed record Band(int Min, int? Max, string Text, EqArray<Effect> Effects = default)
{
    public bool Contains(int total) => total >= Min && (Max is null || total <= Max);
}

public sealed record CheckBody(Trait Trait, EqArray<Band> Bands)
{
    public Band BandFor(int total) => Bands.First(b => b.Contains(total));
}

/// <summary>One option of a choice card. An option with a <see cref="Check"/> rolls it after its own effects.</summary>
public sealed record ChoiceOption(string Id, string Label, string Text, EqArray<Effect> Effects = default,
                                  CheckBody? Check = null);

public sealed record ChoiceBody(EqArray<ChoiceOption> Options);

public sealed record ItemUse(string Label, EqArray<Effect> Effects, bool Single = false);

public sealed record ItemBody(EqArray<Effect> Passive = default, ItemUse? Use = null);

/// <summary>A card. Exactly one of the bodies is set: Incidents have a check or a choice; Salvage and Anomalies an item.</summary>
public sealed record CardDef(CardId Id, CardKind Kind, string Title, string Text,
                             CheckBody? Check = null, ChoiceBody? Choice = null, ItemBody? Item = null);

/// <summary>A start tile placed before the game (spec §2.3).</summary>
public sealed record StartPlacement(TileId Tile, DeckId Deck, int X, int Y, int Rotation = 0);

/// <summary>A link between two start placements, by index into <see cref="StartLayout.Place"/> (the lift).</summary>
public sealed record StartLink(int From, int To, bool OneWay = false);

/// <summary>The start layout. The crew begin in <see cref="Entry"/>.</summary>
public sealed record StartLayout(TileId Entry, EqArray<StartPlacement> Place, EqArray<StartLink> Links);
