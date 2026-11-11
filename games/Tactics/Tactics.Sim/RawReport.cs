using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Sim;

/// <summary>Complete, deterministic measurements alongside the human-readable balance report.</summary>
public static class RawReport
{
    public static void Write(string path, SimConfig config, IReadOnlyList<SimBattle> battles)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            {
                using Stream output = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: true) : file;
                JsonSerializer.Serialize(output, new
                {
                    Config = config with { FixedParty = config.FixedParty.IsDefault ? [] : config.FixedParty },
                    Battles = battles,
                }, new JsonSerializerOptions { IncludeFields = true, Converters = { new JsonStringEnumConverter() } });
            }
            File.Move(temporary, full, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
