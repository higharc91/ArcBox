using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>How hard the encounter plays.</summary>
public enum Difficulty { Story, Normal, Hard }

/// <summary>A unit entering the battle: its sheet, who controls it, where it stands and what it carries.</summary>
public sealed record UnitPlacement(UnitId Id, UnitSheet Sheet, Controller Controller, Tile At,
    ImmutableArray<ItemStack> Bag, bool Protected = false);

/// <summary>Everything <see cref="Battle.Start"/> needs: the map, the units, objectives, triggers and rewards.</summary>
public sealed record BattleSetup(BattleMap Map, ImmutableArray<UnitPlacement> Units, ObjectivesDef Objectives,
    ImmutableArray<TriggerDef> Triggers, Difficulty Difficulty, int Xp, ImmutableArray<LootTableId> Loot);

/// <summary>Objective expression: a leaf string ("defeat_all", "survive:8", …) or {"any": […]} / {"all": […]}.
/// JSON: a bare string is a leaf. Task 8 evaluates defeat_all and party_out; Task 22 adds the rest.</summary>
[JsonConverter(typeof(ObjectiveExprJsonConverter))]
public sealed record ObjectiveExpr(string? Leaf, ImmutableArray<ObjectiveExpr> Any, ImmutableArray<ObjectiveExpr> All)
{
    /// <summary>A leaf such as <c>"defeat_all"</c>.</summary>
    public static ObjectiveExpr Of(string leaf) => new(leaf, [], []);
}

/// <summary>What wins and what loses the battle.</summary>
public sealed record ObjectivesDef(ObjectiveExpr Win, ObjectiveExpr Lose)
{
    /// <summary>Win by defeating every enemy; lose when the whole party is out.</summary>
    public static ObjectivesDef Default { get; } = new(ObjectiveExpr.Of("defeat_all"), ObjectiveExpr.Of("party_out"));
}

/// <summary>Writes an <see cref="ObjectiveExpr"/> leaf as a bare string and a group as <c>{"any": […]}</c> / <c>{"all": […]}</c>.</summary>
public sealed class ObjectiveExprJsonConverter : JsonConverter<ObjectiveExpr>
{
    /// <inheritdoc />
    public override ObjectiveExpr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return ObjectiveExpr.Of(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("An objective is a string or an object with \"any\" or \"all\".");

        ImmutableArray<ObjectiveExpr> any = [], all = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString();
            reader.Read();
            var items = JsonSerializer.Deserialize<ImmutableArray<ObjectiveExpr>>(ref reader, options);
            switch (key)
            {
                case "any": any = items; break;
                case "all": all = items; break;
                default: throw new JsonException($"Unknown objective key '{key}' (expected \"any\" or \"all\").");
            }
        }
        return new ObjectiveExpr(null, any, all);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ObjectiveExpr value, JsonSerializerOptions options)
    {
        if (value.Leaf is not null)
        {
            writer.WriteStringValue(value.Leaf);
            return;
        }
        writer.WriteStartObject();
        if (!value.Any.IsDefaultOrEmpty)
        {
            writer.WritePropertyName("any");
            JsonSerializer.Serialize(writer, value.Any, options);
        }
        if (!value.All.IsDefaultOrEmpty || value.Any.IsDefaultOrEmpty)
        {
            writer.WritePropertyName("all");
            JsonSerializer.Serialize(writer, value.All.IsDefault ? [] : value.All, options);
        }
        writer.WriteEndObject();
    }
}
