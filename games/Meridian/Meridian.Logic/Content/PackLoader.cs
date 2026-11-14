using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Content;

namespace Meridian.Logic;

/// <summary>
/// Loads <c>content/meridian/&lt;pack&gt;/rules/</c> through <see cref="IFileSource"/> and validates it. Throws
/// <see cref="ContentException"/> with every error found, so an author fixes a broken pack in one pass.
/// </summary>
public static class PackLoader
{
    public const string TilesFile = "rules/tiles.json";
    public const string CrewFile = "rules/crew.json";
    public const string BreachFile = "rules/breach.json";

    /// <summary>The words a briefing may run to (spec §11: briefings must stay readable on a phone).</summary>
    public const int BriefingWords = 120;

    public static readonly IReadOnlyList<(CardKind Kind, string File)> CardFiles =
    [
        (CardKind.Incident, "rules/cards/incidents.json"),
        (CardKind.Salvage, "rules/cards/salvage.json"),
        (CardKind.Anomaly, "rules/cards/anomalies.json"),
    ];

    /// <summary>
    /// ArcJson's options, made strict for authored content: a missing required field or a null where the record says
    /// non-null is an error rather than a silent null, and the <c>kind</c> discriminator may appear anywhere.
    /// </summary>
    internal static readonly JsonSerializerOptions ContentJson = new(ArcJson.Options)
    {
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record TilesFileJson(StartLayout Start, EqArray<TileDef> StartTiles, EqArray<TileDef> Tiles);
    private sealed record CrewFileJson(EqArray<CrewDef> Crew);
    private sealed record CardEntry(CardId Id, string Title, string Text,
                                    CheckBody? Check = null, ChoiceBody? Choice = null, ItemBody? Item = null);
    private sealed record CardFileJson(EqArray<CardEntry> Cards);

    public static RulesPack Load(IFileSource files, string root, MeridianRules? rules = null, ScenarioSet? scenarios = null)
    {
        scenarios ??= ScenarioRegistry.Default;
        rules ??= new MeridianRules();
        var errors = new List<ContentError>();

        var tiles = Read<TilesFileJson>(files, root, TilesFile, errors);
        var crew = Read<CrewFileJson>(files, root, CrewFile, errors);
        var cards = new List<CardDef>();
        foreach (var (kind, file) in CardFiles)
        {
            if (Read<CardFileJson>(files, root, file, errors) is { } read)
            {
                cards.AddRange(read.Cards.Select(c => new CardDef(c.Id, kind, c.Title, c.Text, c.Check, c.Choice, c.Item)));
            }
        }

        // The Breach table and scenario files are optional until a pack ships scenarios.
        var breach = files.Exists(Path.Combine(root, BreachFile)) ? Read<BreachTable>(files, root, BreachFile, errors) : BreachTable.Empty;
        var defs = new List<ScenarioDef>();
        foreach (var id in breach?.Scenarios ?? [])
        {
            if (Read<ScenarioDef>(files, root, ScenarioFile(id), errors) is { } def)
            {
                defs.Add(def);
            }
        }

        if (errors.Count > 0 || tiles is null || crew is null)
        {
            throw new ContentException(errors);
        }

        var pack = new RulesPack(rules, tiles.StartTiles.Items, tiles.Tiles.Items, tiles.Start, crew.Crew.Items, cards,
                                 breach, defs, scenarios);
        errors.AddRange(Validation.Check(pack));
        if (errors.Count == 0)
        {
            errors.AddRange(ScenarioRegistry.Check(pack));
        }
        return errors.Count > 0 ? throw new ContentException(errors) : pack;
    }

    public static string ScenarioFile(string id) => $"rules/scenarios/{id}.json";

    private static T? Read<T>(IFileSource files, string root, string relative, List<ContentError> errors) where T : class
    {
        var path = Path.Combine(root, relative);
        if (!files.Exists(path))
        {
            errors.Add(new ContentError(relative, "(file)", "is missing"));
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(files.ReadAllText(path), ContentJson)
                   ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException e)
        {
            errors.Add(new ContentError(relative, e.Path ?? "(root)", e.Message));
            return null;
        }
    }
}
