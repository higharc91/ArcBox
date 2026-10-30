using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArcBox.Sdk;

namespace Tactics.Logic;

/// <summary>What a content reference points at; see <see cref="Validation.Resolve"/>.</summary>
internal enum RefKind { Ability, Passive, AbilityOrPassive, Condition, Monster, Item, Weapon, Armor, Map, AiProfile, Art }

/// <summary>A reference from one content file to an id, checked once every pack is loaded.</summary>
internal sealed record ContentRef(RefKind Kind, string Id, string File, string Pointer);

/// <summary>A map as written and the engine's map built from it.</summary>
internal sealed record MapContent(MapDef Def, BattleMap Map);

/// <summary>
/// The checks behind <see cref="RulesPack.Load"/> (spec §4.10), for one load. <see cref="PackLoader"/> hands each element of
/// each file to the method for its kind, which deserialises it with <see cref="ContentOptions"/> (effects through
/// <see cref="EffectJsonConverter"/>) and checks what can be checked within the element; references to other ids are kept
/// and checked by <see cref="Resolve"/> when everything is loaded, and encounters by <see cref="CheckEncounters"/>. Every
/// problem is added to <see cref="Errors"/> with its file and JSON pointer; nothing throws.
/// </summary>
internal sealed partial class Validation(ArtManifest? art)
{
    /// <summary>The largest ability or effect range, in tiles.</summary>
    public const int MaxRange = 12;

    /// <summary>The largest area size.</summary>
    public const int MaxAreaSize = 10;

    private static readonly ImmutableSortedSet<string> HookNames = Ordinal.Set(
        typeof(HookKind).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!.Name));

    private static readonly int[] SubclassLevels = [3, 6, 10];

    private readonly List<ContentRef> _refs = [];
    private readonly HashSet<(RefKind, string)> _broken = [];

    /// <summary>Every error found so far, in the order found.</summary>
    public List<ContentError> Errors { get; } = [];

    /// <summary>Records an error.</summary>
    public void Error(string file, string pointer, string message) => Errors.Add(new ContentError(file, pointer, message));

    /// <summary>
    /// Notes that the element of this kind in <paramref name="e"/> failed to load, so references to its id are not reported
    /// as unknown on top of its own errors.
    /// </summary>
    public void Broken(RefKind kind, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            _broken.Add((kind, id.GetString()!));
    }

    // ---- After loading ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks every reference kept while loading. <paramref name="exists"/> answers for the merged content. AI profiles are
    /// checked only once some pack defines one, and art keys only when an art manifest was given; an art key must be
    /// <c>"&lt;set&gt;.&lt;asset&gt;"</c>.
    /// </summary>
    public void Resolve(Func<RefKind, string, bool> exists, bool anyAiProfiles)
    {
        foreach (var r in _refs)
        {
            if (_broken.Contains((r.Kind, r.Id))
                || (r.Kind == RefKind.AbilityOrPassive && (_broken.Contains((RefKind.Ability, r.Id)) || _broken.Contains((RefKind.Passive, r.Id)))))
                continue;
            var message = r.Kind switch
            {
                RefKind.Art when art is null => null,
                RefKind.Art when !r.Id.Contains('.', StringComparison.Ordinal) => $"Art key '{r.Id}' names no set (expected \"<set>.<asset>\").",
                RefKind.Art => art!.Keys.Contains(r.Id) ? null : $"Unknown art key '{r.Id}'.",
                RefKind.AiProfile when !anyAiProfiles => null,
                RefKind.AbilityOrPassive => exists(RefKind.Ability, r.Id) || exists(RefKind.Passive, r.Id)
                    ? null : $"Unknown ability or passive '{r.Id}'.",
                _ => exists(r.Kind, r.Id) ? null : $"Unknown {KindName(r.Kind)} '{r.Id}'.",
            };
            if (message is not null)
                Error(r.File, r.Pointer, message);
        }
    }

    private static string KindName(RefKind kind) => kind switch
    {
        RefKind.AiProfile => "AI profile",
        _ => kind.ToString().ToLowerInvariant(),
    };

    // ---- Plumbing ---------------------------------------------------------------------------------------------------

    private void Ref(RefKind kind, string? id, string file, string ptr)
    {
        if (id is not null)
            _refs.Add(new ContentRef(kind, id, file, ptr));
    }

    private void Refs(RefKind kind, IEnumerable<string> ids, string file, string ptr)
    {
        var i = 0;
        foreach (var id in ids)
            Ref(kind, id, file, Pointer.Append(ptr, i++));
    }

    /// <summary>
    /// The options content is read with: <see cref="ArcJson.Options"/>, except that a property no definition has is an
    /// error rather than ignored, so a typo such as <c>"efects"</c> can't silently drop part of a definition.
    /// </summary>
    internal static JsonSerializerOptions ContentOptions { get; } = CreateContentOptions();

    private static JsonSerializerOptions CreateContentOptions()
    {
        var options = new JsonSerializerOptions(ArcJson.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// False for the exceptions no loader should swallow (out of memory, stack overflow and the like); every other exception
    /// while reading content is reported as a <see cref="ContentError"/>.
    /// </summary>
    internal static bool NonFatal(Exception ex)
        => ex is not (OutOfMemoryException or StackOverflowException or InsufficientExecutionStackException or AccessViolationException);

    /// <summary>
    /// Runs one element's checks, keeping the references it found only when it loads; an element that fails keeps none,
    /// so its own errors aren't followed by unknown-id errors from a half-read element. An unexpected exception becomes an
    /// error at the element.
    /// </summary>
    private T? Guarded<T>(string file, string ptr, Func<T?> check)
    {
        var refs = _refs.Count;
        T? result;
        try
        {
            result = check();
        }
        catch (Exception ex) when (NonFatal(ex))
        {
            Error(file, ptr, $"This couldn't be checked: {ex.Message}");
            result = default;
        }
        if (result is null)
            _refs.RemoveRange(refs, _refs.Count - refs);
        return result;
    }

    /// <summary>
    /// Deserialises one element with <see cref="ContentOptions"/>, applies <paramref name="patch"/>, then fills absent
    /// lists, sets and maps with empty ones and reports absent required values and nulls (see <see cref="ContentShape"/>).
    /// Any exception becomes an error at the element's pointer (plus the path inside it, when known).
    /// </summary>
    private T? Deserialize<T>(JsonElement e, string file, string ptr, Func<T, T>? patch = null) where T : class
    {
        T? def;
        try
        {
            def = e.Deserialize<T>(ContentOptions);
        }
        catch (Exception ex) when (NonFatal(ex))
        {
            Error(file, ex is JsonException json ? Pointer.Combine(ptr, json.Path) : ptr, Pointer.CleanMessage(ex.Message));
            return null;
        }
        if (def is null)
        {
            Error(file, ptr, "The value is null.");
            return null;
        }
        if (patch is not null)
            def = patch(def);
        var errors = Errors.Count;
        ContentShape.Fix(def, file, ptr, this);
        return Errors.Count == errors ? def : null;
    }

    private static string JsonName(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}
