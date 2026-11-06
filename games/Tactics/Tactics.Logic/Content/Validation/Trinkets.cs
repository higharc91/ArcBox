using System.Text.Json;

namespace Tactics.Logic;

internal sealed partial class Validation
{
    public TrinketDef? Trinket(JsonElement e, string file, string ptr) => Guarded(file, ptr, () =>
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("hook", out var hook)
            && hook.ValueKind == JsonValueKind.String && !HookNames.Contains(hook.GetString()!))
        {
            Error(file, Pointer.Append(ptr, "hook"), $"Unknown hook '{hook.GetString()}'.");
            return null;
        }
        if (!EffectsIn(e, "effects", file, ptr, inSave: false)) return null;
        var def = Deserialize<TrinketDef>(e, file, ptr);
        if (def is null) return null;
        RejectHpPercent(def.Static, file, Pointer.Append(ptr, "static"));
        if (def.Rarity != "rare") Error(file, Pointer.Append(ptr, "rarity"), "A trinket must declare rare rarity.");
        if (string.IsNullOrWhiteSpace(def.Text)) Error(file, Pointer.Append(ptr, "text"), "A trinket needs a readable description.");
        if (def.Hook is null && !def.Effects.IsDefaultOrEmpty)
            Error(file, Pointer.Append(ptr, "effects"), "A trinket's effects need a hook to run on.");
        return def;
    });
}
