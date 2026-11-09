using ArcBox.Sdk.Content;
using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>A short line, filtered before the per-person persisted rotation is applied.</summary>
public sealed record BarkLine(string Id, string Text, string Mood, string? Direction,
    ImmutableArray<string> Requires = default, ImmutableArray<string> Unless = default, int Weight = 1)
{
    public bool Eligible(IReadOnlySet<string> flags)
        => (Requires.IsDefault || Requires.All(flags.Contains)) && (Unless.IsDefault || !Unless.Any(flags.Contains));
}

/// <summary>One consistent speaker with a deterministic rotation over eligible lines.</summary>
public sealed record BarkSet(string Speaker, ImmutableArray<BarkLine> Lines)
{
    public BarkLine? Pick(IReadOnlySet<string> flags, long counter)
    {
        var eligible = Lines.Where(l => l.Eligible(flags)).ToArray();
        return eligible.Length == 0 ? null : eligible[(int)((counter % eligible.Length + eligible.Length) % eligible.Length)];
    }

    /// <summary>Authored battle weights on the cosmetic beat counter; town rotations and battle RNG stay unchanged.</summary>
    public BarkLine? PickWeighted(IReadOnlySet<string> flags, long counter)
    {
        var eligible = Lines.Where(l => l.Eligible(flags)).ToArray();
        if (eligible.Length == 0) return null;
        if (eligible.Any(l => l.Weight < 1)) throw new InvalidDataException("Bark weights must be positive.");
        var total = eligible.Sum(l => (long)l.Weight);
        var at = (counter % total + total) % total;
        foreach (var line in eligible)
        {
            if (at < line.Weight) return line;
            at -= line.Weight;
        }
        return null;
    }
}

/// <summary>Town barks keyed by named character, or by townsfolk preset.</summary>
public sealed class BarkBook(ImmutableDictionary<string, BarkSet> sets, ImmutableDictionary<string, BarkSet>? battle = null)
{
    public static BarkBook Empty { get; } = new(ImmutableDictionary<string, BarkSet>.Empty);
    internal bool Has(string key) => sets.ContainsKey(key);
    public BarkSet? For(RegionPerson person) => sets.GetValueOrDefault(person.Bark ?? person.Character ?? person.Preset);
    /// <summary>Authored battle barks, separate from town rotations.</summary>
    public BarkSet? ForBattle(string speaker, string moment) => battle?.GetValueOrDefault(speaker + ":" + moment);
    public IEnumerable<string> LineIds => sets.Values.SelectMany(s => s.Lines).Select(l => l.Id);

    internal static BarkBook Load(string lore, ImmutableArray<string> files, IFileSource source)
    {
        var result = ImmutableDictionary.CreateBuilder<string, BarkSet>(StringComparer.Ordinal);
        var battle = ImmutableDictionary.CreateBuilder<string, BarkSet>(StringComparer.Ordinal);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var file in files.Where(f => f.StartsWith("barks/", StringComparison.Ordinal)))
        {
            var path = Path.Combine(lore, file);
            try
            {
                using var doc = JsonDocument.Parse(source.ReadAllText(path));
                var root = doc.RootElement;
                if (file == "barks/townsfolk.json")
                {
                    foreach (var preset in root.GetProperty("presets").EnumerateObject())
                        result.Add(preset.Name, new BarkSet(preset.Value.GetProperty("speaker").GetString()!,
                            preset.Value.GetProperty("lines").Deserialize<ImmutableArray<BarkLine>>(options)));
                }
                else
                {
                    var speaker = root.GetProperty("speaker").GetString()!;
                    foreach (var moment in root.GetProperty("events").EnumerateObject())
                    {
                        var set = new BarkSet(speaker, moment.Value.Deserialize<ImmutableArray<BarkLine>>(options));
                        if (moment.Name == "town") result.Add(speaker, set);
                        else battle.Add(speaker + ":" + moment.Name, set);
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidDataException($"{path}: invalid town bark data: {ex.Message}", ex);
            }
        }
        return new BarkBook(result.ToImmutable(), battle.ToImmutable());
    }
}
