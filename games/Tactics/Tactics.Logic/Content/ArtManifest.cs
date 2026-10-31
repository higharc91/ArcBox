using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// Every key the art sets define: <c>"&lt;set&gt;.&lt;asset&gt;"</c> (<c>"signature.radiant_bolt"</c>,
/// <c>"characters.goblin_archer"</c>) and, for an asset with variants, <c>"&lt;set&gt;.&lt;asset&gt;.&lt;variant&gt;"</c>
/// (<c>"beasts.wolf.grey"</c>). Content's presentation keys and sprites must be among them.
/// </summary>
public sealed record ArtManifest([property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Keys);

/// <summary>Reads the art sets' manifests.</summary>
public static class ArtManifests
{
    /// <summary>
    /// Reads <c>&lt;set&gt;/manifest.json</c> for every set folder in <paramref name="contentBaseDir"/> (e.g.
    /// <c>content/tactics/base</c>; the <c>rules</c> folder is a rules pack, not an art set). The manifests differ in shape,
    /// so every entry of every top-level object is an asset (with its <c>"variants"</c>, when it has an object of them),
    /// and every name in a top-level list of names is one too (the characters set lists its presets that way). A manifest
    /// that can't be read is a <see cref="ContentError"/> for <c>"&lt;set&gt;/manifest.json"</c>; every one found is thrown
    /// together as a <see cref="ContentException"/>.
    /// </summary>
    public static ArtManifest Load(string contentBaseDir)
    {
        var keys = new List<string>();
        var errors = new List<ContentError>();
        foreach (var dir in Directory.GetDirectories(contentBaseDir).Order(StringComparer.Ordinal))
        {
            var set = Path.GetFileName(dir);
            var path = Path.Combine(dir, "manifest.json");
            if (set == "rules" || !File.Exists(path))
                continue;
            try
            {
                keys.AddRange(KeysOf(set, File.ReadAllBytes(path)));
            }
            catch (Exception ex) when (Validation.NonFatal(ex))
            {
                errors.Add(new ContentError($"{set}/manifest.json", "", Pointer.CleanMessage(ex.Message)));
            }
        }
        if (errors.Count > 0)
            throw new ContentException([.. errors]);
        return new ArtManifest(Ordinal.Set(keys));
    }

    /// <summary>The keys one set's manifest defines. Throws on JSON that isn't an object.</summary>
    private static List<string> KeysOf(string set, byte[] manifest)
    {
        var keys = new List<string>();
        using var doc = JsonDocument.Parse(manifest);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("An art manifest must be a JSON object.");
        foreach (var group in doc.RootElement.EnumerateObject())
        {
            if (group.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var asset in group.Value.EnumerateObject())
                {
                    keys.Add($"{set}.{asset.Name}");
                    if (asset.Value.ValueKind == JsonValueKind.Object && asset.Value.TryGetProperty("variants", out var variants)
                        && variants.ValueKind == JsonValueKind.Object)
                        keys.AddRange(variants.EnumerateObject().Select(v => $"{set}.{asset.Name}.{v.Name}"));
                }
            }
            else if (group.Value.ValueKind == JsonValueKind.Array)
            {
                keys.AddRange(group.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String && IsName(item.GetString()!))
                    .Select(item => $"{set}.{item.GetString()}"));
            }
        }
        return keys;
    }

    private static bool IsName(string text) => text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
