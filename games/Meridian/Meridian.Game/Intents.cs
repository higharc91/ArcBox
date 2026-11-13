using System.Text.Json;
using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

/// <summary>Reads a phone's <see cref="CustomInput"/>. Malformed input is dropped, never thrown.</summary>
internal static class Intents
{
    public static bool TryRead(PlayerInput input, out string action, out JsonElement data)
    {
        action = "";
        data = default;
        try
        {
            var custom = input.Read<CustomInput>();
            action = custom.Action ?? "";
            data = custom.Data ?? default;
            return action.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A string property of the data, or null.</summary>
    public static string? Text(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    public static int? Number(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var p) && p.TryGetInt32(out var n) ? n : null;

    public static bool? Flag(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var p)
           && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;

    /// <summary>Every field any intent uses. One bag rather than a record per action keeps parsing in one place.</summary>
    private sealed record Bag(string? To = null, string? At = null, Side? Side = null, int? Rotation = null, string? Option = null,
                              string? Item = null, EqArray<string> Give = default, EqArray<string> Take = default, bool? Accept = null);

    /// <summary>
    /// The command a phone's intent stands for, or null for an unknown action or missing data. Targets are absolute, so a
    /// late or repeated intent is either still legal or rejected by the rules.
    /// </summary>
    public static Command? ToCommand(SeatId seat, string action, JsonElement data)
    {
        Bag bag;
        try
        {
            bag = data.ValueKind == JsonValueKind.Object ? data.Deserialize<Bag>(ArcJson.Options) ?? new Bag() : new Bag();
        }
        catch (JsonException)
        {
            return null;
        }

        return action switch
        {
            "moveTo" when bag.To is { } to => new MoveTo(seat, new CompartmentId(to)),
            "open" when bag is { At: { } at, Side: { } side } => new Open(seat, new CompartmentId(at), side),
            "orient" when bag.Rotation is { } r => new Orient(seat, r),
            "roll" => new Roll(seat),
            "choose" when bag.Option is { } o => new Choose(seat, o),
            "useItem" when bag.Item is { } i => new UseItem(seat, new CardId(i)),
            "useRoom" => new UseCompartment(seat),
            "offerTrade" when bag.To is { } to => new OfferTrade(seat, new SeatId(to),
                new TradeOffer(bag.Give.Select(c => new CardId(c)).ToEqArray(), bag.Take.Select(c => new CardId(c)).ToEqArray())),
            "answerTrade" when bag.Accept is { } a => new AnswerTrade(seat, a),
            "endTurn" => new EndTurn(seat),
            _ => null,
        };
    }

    /// <summary>The intent a phone would send for a command: the inverse of <see cref="ToCommand"/>, for bots and tests.</summary>
    public static (string Action, object? Data) FromCommand(Command command) => command switch
    {
        MoveTo m => ("moveTo", new { to = m.To.Value }),
        Open o => ("open", new { at = o.At.Value, side = o.Side }),
        Orient o => ("orient", new { rotation = o.Rotation }),
        Roll => ("roll", null),
        Choose c => ("choose", new { option = c.Option }),
        UseItem u => ("useItem", new { item = u.Item.Value }),
        UseCompartment => ("useRoom", null),
        OfferTrade t => ("offerTrade", new { to = t.To.Value, give = t.Offer.Give.Select(c => c.Value), take = t.Offer.Take.Select(c => c.Value) }),
        AnswerTrade a => ("answerTrade", new { accept = a.Accept }),
        EndTurn => ("endTurn", null),
        _ => throw new ArgumentException($"{command.GetType().Name} has no phone intent.", nameof(command)),
    };
}
