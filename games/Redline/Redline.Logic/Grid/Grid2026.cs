using System.Text.Json;
using ArcBox.Sdk;

namespace Redline.Logic;

public static class Grid2026
{
    public const string ResourceName = "Redline.Logic.Data.grid2026.json";
    public const int TeamCount = 11;
    public const int DriversPerTeam = 2;
    public const double MinAiSkill = 0.85;
    public const double MaxAiSkill = 1.15;

    private static readonly Lazy<Grid> Cached = new(LoadCore);

    /// <summary>The embedded 2026 grid. Parsed and validated once, then cached.</summary>
    public static Grid Load() => Cached.Value;

    /// <summary>Returns <paramref name="grid"/> when it is a well-formed field; throws <see cref="InvalidDataException"/> otherwise.</summary>
    public static Grid Validate(Grid grid)
    {
        if (grid is null || grid.Teams is null)
            throw new InvalidDataException("Grid has no teams.");
        if (grid.Teams.Count != TeamCount)
            throw new InvalidDataException($"Grid must have {TeamCount} teams, found {grid.Teams.Count}.");

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<int>();
        foreach (var team in grid.Teams)
        {
            if (team?.Drivers is null || team.Drivers.Count != DriversPerTeam)
                throw new InvalidDataException($"Team '{team?.Id}' must have {DriversPerTeam} drivers.");
            if (team.Livery is null)
                throw new InvalidDataException($"Team '{team.Id}' has no livery.");
            foreach (var d in team.Drivers)
            {
                if (string.IsNullOrWhiteSpace(d.Code))
                    throw new InvalidDataException($"Driver '{d.Id}' has no code.");
                if (!codes.Add(d.Code))
                    throw new InvalidDataException($"Duplicate driver code '{d.Code}'.");
                if (!numbers.Add(d.Number))
                    throw new InvalidDataException($"Duplicate car number {d.Number}.");
                if (double.IsNaN(d.AiSkill) || d.AiSkill < MinAiSkill || d.AiSkill > MaxAiSkill)
                    throw new InvalidDataException($"Driver '{d.Code}' aiSkill {d.AiSkill} is outside [{MinAiSkill}, {MaxAiSkill}].");
            }
        }
        return grid;
    }

    private static Grid LoadCore()
    {
        using var stream = typeof(Grid2026).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded resource '{ResourceName}' not found.");
        var grid = JsonSerializer.Deserialize<Grid>(stream, ArcJson.Options)
            ?? throw new InvalidDataException("grid2026.json is empty.");
        return Validate(grid);
    }
}
