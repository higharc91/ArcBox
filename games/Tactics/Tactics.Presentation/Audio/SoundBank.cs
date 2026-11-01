using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Audio;

/// <summary>A sound in <c>sfx/manifest.json</c>.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Files">Its files, relative to the pack's root: several take turns.</param>
/// <param name="Loop">It's a bed that runs until stopped.</param>
/// <param name="Group">The manifest's group, such as <c>spell</c> or <c>creature</c>.</param>
/// <param name="Use">What it's for, as the manifest says.</param>
public sealed record SoundEntry(string Id, ImmutableArray<string> Files, bool Loop, string Group, string Use);

/// <summary>A track in <c>music/manifest.json</c>.</summary>
/// <param name="Id">Its id.</param>
/// <param name="File">Its file, relative to the pack's root.</param>
/// <param name="Loop">It loops; a sting doesn't.</param>
public sealed record MusicTrack(string Id, string File, bool Loop);

/// <summary>What a unit's sound depends on.</summary>
/// <param name="Weapon">Its weapon: a rules id for a monster, a preset's layer for a character.</param>
/// <param name="Armoured">It wears plate or mail, so a blow rings.</param>
/// <param name="Family">The prefix of its creature sounds, as <c>goblin</c> in <c>goblin_attack</c>, or null.</param>
/// <param name="Heavy">It treads heavily.</param>
/// <param name="Flies">It flaps rather than steps.</param>
public sealed record UnitGear(string? Weapon, bool Armoured, string? Family, bool Heavy, bool Flies);

/// <summary>
/// What the pack holds for the ear: the sfx and music manifests, and what the rest of the pack says about who a sprite is. The
/// sfx manifest says what each sound is for only in its <c>use</c> text, so effects and creatures are read from that text,
/// which keeps a sound added to the pack from needing a change here.
/// </summary>
public sealed partial class SoundBank
{
    private static readonly ImmutableHashSet<string> Armour = ["plate", "chain"];

    private readonly string _root;
    private readonly Dictionary<string, SoundEntry> _sounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MusicTrack> _tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _families = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Id, string? Weapon)>> _monsters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnitGear> _gear = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BattleMap?> _maps = new(StringComparer.Ordinal);
    private ImmutableHashSet<string> _heavy = [];
    private ImmutableHashSet<string> _fliers = [];

    private SoundBank(string root) => _root = root;

    /// <summary>The ids of every sound.</summary>
    public IReadOnlyCollection<string> Ids => _sounds.Keys;

    /// <summary>Reads the manifests under the pack's root. Throws if one is missing or malformed.</summary>
    public static SoundBank Load(string packRoot)
    {
        var bank = new SoundBank(packRoot);
        bank.ReadSounds();
        bank.ReadTracks();
        bank.ReadMonsters();
        return bank;
    }

    /// <summary>The sound with this id, or null.</summary>
    public SoundEntry? Sound(string id) => _sounds.GetValueOrDefault(id);

    /// <summary>True if the pack has this sound.</summary>
    public bool Has(string id) => _sounds.ContainsKey(id);

    /// <summary>The track with this id, or null.</summary>
    public MusicTrack? Track(string id) => _tracks.GetValueOrDefault(id);

    /// <summary>The sound the manifest names for an art key, such as <c>fx.heal.holy</c>, or null.</summary>
    public string? ForEffect(string key)
    {
        if (_effects.TryGetValue(key, out var sound))
            return sound;
        var parts = key.Split('.');
        return parts.Length >= 2 ? _effects.GetValueOrDefault($"{parts[0]}.{parts[1]}") : null;
    }

    /// <summary>What's known about the unit drawn with <paramref name="sprite"/>. A sprite nothing knows has none of it.</summary>
    public UnitGear Gear(string? sprite)
    {
        if (sprite is null)
            return new UnitGear(null, false, null, false, false);
        if (!_gear.TryGetValue(sprite, out var gear))
            _gear[sprite] = gear = WorkOutGear(sprite);
        return gear;
    }

    /// <summary>The footstep for <paramref name="tile"/> of <paramref name="map"/>. A map that can't be read has the default one: a step never holds up a beat.</summary>
    public string Step(string map, Tile tile)
    {
        if (!_maps.TryGetValue(map, out var known))
        {
            var path = Path.Combine(_root, "rules", "maps", $"{map}.json");
            _maps[map] = known = File.Exists(path) ? BoardMap.Parse(File.ReadAllText(path)) : null;
        }
        return known is not null && known.InBounds(tile) ? SoundChoice.Step(known.Materials[known[tile].Material]) : SoundChoice.DefaultStep;
    }

    private UnitGear WorkOutGear(string sprite)
    {
        var parts = sprite.Split('.');
        var monsters = _monsters.GetValueOrDefault(sprite) ?? [];
        var (layer, torso) = parts is ["characters", var preset, ..] ? Preset(preset) : (null, null);
        var ids = monsters.Select(m => m.Id).ToArray();
        return new UnitGear(monsters.Select(m => m.Weapon).FirstOrDefault(w => w is not null) ?? layer, torso is not null && Armour.Contains(torso),
            ids.Select(id => Named(_families, id)).FirstOrDefault(f => f is not null), ids.Any(id => Listed(_heavy, id)), ids.Any(id => Listed(_fliers, id)));
    }

    private (string? Weapon, string? Torso) Preset(string id)
    {
        var path = Path.Combine(_root, "characters", "presets", $"{id}.json");
        if (!File.Exists(path))
            return (null, null);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var layers = doc.RootElement.GetProperty("layers");
        return (Layer(layers, "weapon"), Layer(layers, "torso"));
    }

    private static string? Layer(JsonElement layers, string name) => layers.TryGetProperty(name, out var value) ? value.GetString() : null;

    // The manifest says "bat" where the rules say "giant_bat".
    private static string? Named(Dictionary<string, string> names, string id) => names.GetValueOrDefault(id) ?? names.GetValueOrDefault(id.Split('_')[^1]);

    private static bool Listed(ImmutableHashSet<string> names, string id) => names.Contains(id) || names.Contains(id.Split('_')[^1]);

    private void ReadSounds()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "sfx", "manifest.json")));
        foreach (var sound in doc.RootElement.GetProperty("sounds").EnumerateObject())
        {
            var v = sound.Value;
            var entry = new SoundEntry(sound.Name, [.. v.GetProperty("files").EnumerateArray().Select(f => $"sfx/{f.GetString()}")],
                v.TryGetProperty("loop", out var loop) && loop.GetBoolean(), v.GetProperty("group").GetString() ?? "", v.GetProperty("use").GetString() ?? "");
            _sounds[entry.Id] = entry;
            if (entry.Group == "spell")
                ReadEffects(entry);
            else if (entry.Group == "creature" && entry.Id.EndsWith("_attack", StringComparison.Ordinal))
                foreach (var name in Words(entry.Use))
                    _families.TryAdd(name, entry.Id[..^"_attack".Length]);
        }
        _heavy = [.. Words(_sounds.GetValueOrDefault("step_heavy")?.Use ?? "")];
        _fliers = [.. Words(_sounds.GetValueOrDefault("flap_wings")?.Use ?? "")];
    }

    // An fx.name.* or a bare fx.name stands for every variant; the others for the one they name.
    private void ReadEffects(SoundEntry entry)
    {
        foreach (Match key in EffectKey().Matches(entry.Use))
            _effects.TryAdd(key.Value.EndsWith(".*", StringComparison.Ordinal) ? key.Value[..^2] : key.Value, entry.Id);
    }

    private static IEnumerable<string> Words(string text) => WordPattern().Matches(text).Select(m => m.Value);

    private void ReadTracks()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "music", "manifest.json")));
        foreach (var track in doc.RootElement.GetProperty("tracks").EnumerateObject())
            _tracks[track.Name] = new MusicTrack(track.Name, $"music/{track.Value.GetProperty("files")[0].GetString()}",
                track.Value.TryGetProperty("loop", out var loop) && loop.GetBoolean());
    }

    private void ReadMonsters()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "rules", "monsters"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var monster in doc.RootElement.EnumerateArray())
            {
                if (!monster.TryGetProperty("sprite", out var sprite) || sprite.GetString() is not { } key)
                    continue;
                var weapon = monster.TryGetProperty("weapon", out var w) && w.TryGetProperty("id", out var id) ? id.GetString() : null;
                if (!_monsters.TryGetValue(key, out var list))
                    _monsters[key] = list = [];
                list.Add((monster.GetProperty("id").GetString() ?? "", weapon));
            }
        }
    }

    [GeneratedRegex(@"fx\.[a-z_]+(?:\.(?:[a-z_]+|\*))?")]
    private static partial Regex EffectKey();

    [GeneratedRegex("[a-z_]+")]
    private static partial Regex WordPattern();
}
