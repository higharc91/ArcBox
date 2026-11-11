using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using Tactics.Sim;

namespace Tactics.Logic.Tests;

public sealed class RawReportTests
{
    [Theory]
    [InlineData("measurements.json")]
    [InlineData("measurements.json.gz")]
    public void Raw_reports_keep_default_configuration_and_complete_measurements(string name)
    {
        var folder = Directory.CreateTempSubdirectory("arcbox-sim-export-");
        try
        {
            var config = new SimConfig(["test"], [1], 1, [4], 3, Difficulty.Normal);
            var battle = new SimBattle("test", 1, ["wizard"], 2, BattleOutcome.Won, 5, 1, 0,
                ImmutableSortedDictionary<string, ClassStats>.Empty,
                ImmutableSortedDictionary<AbilityId, (int Used, int Legal)>.Empty.Add(new("common.attack"), (2, 5)), 0,
                Environmental: new EnvironmentStats(3, 2, 4, 0, ImmutableSortedDictionary<string, int>.Empty.Add("ignite", 3)));
            var path = Path.Combine(folder.FullName, name);
            RawReport.Write(path, config, [battle]);
            using var file = File.OpenRead(path);
            using Stream input = name.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
            using var doc = JsonDocument.Parse(input);
            Assert.Empty(doc.RootElement.GetProperty("Config").GetProperty("FixedParty").EnumerateArray());
            Assert.Equal(3, doc.RootElement.GetProperty("Config").GetProperty("Seeds").GetInt32());
            var row = Assert.Single(doc.RootElement.GetProperty("Battles").EnumerateArray());
            Assert.Equal("Won", row.GetProperty("Outcome").GetString());
            Assert.Equal(3, row.GetProperty("Environmental").GetProperty("Reactions").GetInt32());
            Assert.Equal(2, row.GetProperty("Abilities").GetProperty("common.attack").GetProperty("Item1").GetInt32());
            Assert.Equal(5, row.GetProperty("Abilities").GetProperty("common.attack").GetProperty("Item2").GetInt32());
            Assert.Single(folder.GetFiles());
        }
        finally { folder.Delete(recursive: true); }
    }
}
