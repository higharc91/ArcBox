using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Sdk.Tests;

public class HostViewPatchTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement RoundTrip(string from, string to, out string patch)
    {
        var p = HostViewPatch.Diff(J(from), J(to))!;
        patch = p.ToJsonString();
        var applied = HostViewPatch.Apply(J(from), J(patch));
        Assert.True(HostViewPatch.SameJson(J(to), applied), $"patch {patch} gave {applied.GetRawText()}, not {to}");
        return applied;
    }

    [Fact]
    public void Nothing_changed_is_an_empty_patch()
    {
        RoundTrip("""{"a":1,"b":[1,2],"c":{"d":null}}""", """{"a":1,"b":[1,2],"c":{"d":null}}""", out var patch);
        Assert.Equal("{}", patch);
    }

    [Fact]
    public void A_changed_field_is_all_the_patch_carries()
    {
        RoundTrip("""{"seq":1,"big":"xxxxxxxxxxxxxxxxxxxxxxxx","at":{"x":1,"y":2}}""", """{"seq":2,"big":"xxxxxxxxxxxxxxxxxxxxxxxx","at":{"x":1,"y":3}}""", out var patch);
        Assert.Equal("""{"seq":2,"at":{"y":3}}""", patch);
    }

    [Fact]
    public void Null_is_a_value_and_a_missing_property_is_deleted()
    {
        RoundTrip("""{"notice":"hi","gold":5}""", """{"notice":null}""", out var patch);
        Assert.Equal("""{"notice":null,"$delete":["gold"]}""", patch);
        RoundTrip("""{"notice":null}""", """{"notice":"back"}""", out _);
    }

    [Fact]
    public void Arrays_of_items_with_ids_patch_by_id()
    {
        var many = string.Join(",", Enumerable.Range(0, 12).Select(i => $$"""{"id":"t{{i}}","kind":"chest","state":"closed","name":"A chest by the well"}"""));
        var opened = many.Replace("""{"id":"t3","kind":"chest","state":"closed""", """{"id":"t3","kind":"chest","state":"open""");
        RoundTrip($$"""{"things":[{{many}}]}""", $$"""{"things":[{{opened}}]}""", out var patch);
        Assert.Equal("""{"things":{"$keyed":"id","$set":{"t3":{"state":"open"}}}}""", patch);

        // Removed, added and reordered items carry the new order.
        var changed = many.Replace("""{"id":"t5","kind":"chest","state":"closed","name":"A chest by the well"},""", "") + """,{"id":"new","kind":"lever"}""";
        RoundTrip($$"""{"things":[{{many}}]}""", $$"""{"things":[{{changed}}]}""", out patch);
        Assert.Contains("\"$order\":[", patch);
        Assert.Contains("\"new\":{\"id\":\"new\",\"kind\":\"lever\"}", patch);
        Assert.DoesNotContain("\"t5\"", patch);
        // A short new array goes whole: smaller than a keyed patch.
        RoundTrip($$"""{"things":[{{many}}]}""", """{"things":[{"id":"t0"}]}""", out patch);
        Assert.Equal("""{"things":[{"id":"t0"}]}""", patch);
    }

    [Fact]
    public void Arrays_without_distinct_ids_go_whole()
    {
        RoundTrip("""{"legs":[{"seq":1,"to":[1,2]}]}""", """{"legs":[{"seq":2,"to":[1,3]}]}""", out var patch);
        Assert.Equal("""{"legs":[{"seq":2,"to":[1,3]}]}""", patch);
        RoundTrip("""{"a":[{"id":"x"},{"id":"x"}]}""", """{"a":[{"id":"x","v":1},{"id":"x"}]}""", out _);
    }

    [Fact]
    public void A_property_named_like_the_patch_format_means_send_the_whole_view()
    {
        Assert.Null(HostViewPatch.Diff(J("""{"a":1}"""), J("""{"$delete":1}""")));
        Assert.Null(HostViewPatch.Diff(J("""{"a":1}"""), J("""{"a":[{"b":{"$set":2}}]}""")));
    }

    [Fact]
    public void A_patch_naming_an_item_the_view_lacks_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => HostViewPatch.Apply(J("""{"t":[{"id":"a"}]}"""), J("""{"t":{"$keyed":"id","$order":["a","b"]}}""")));
    }

    /// <summary>Random views, and random changes to them, always rebuild exactly. More seeds: HOSTPATCH_SEEDS.</summary>
    [Fact]
    public void Random_changes_always_rebuild_the_new_view()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("HOSTPATCH_SEEDS"), out var n) ? n : 300;
        for (var seed = 0; seed < seeds; seed++)
        {
            var random = new Random(seed);
            var view = RandomObject(random, 0, null);
            for (var step = 0; step < 20; step++)
            {
                var next = Mutate(random, view.DeepClone(), 0) as JsonObject ?? RandomObject(random, 0, null);
                var from = J(view.ToJsonString());
                var to = J(next.ToJsonString());
                var patch = HostViewPatch.Diff(from, to)!;
                var applied = HostViewPatch.Apply(from, J(patch.ToJsonString()));
                Assert.True(HostViewPatch.SameJson(to, applied), $"seed {seed} step {step}: patch {patch.ToJsonString()}");
                view = next;
            }
        }
    }

    private static JsonNode? RandomValue(Random r, int depth, bool keyed) => (depth > 3 ? r.Next(4) : r.Next(7)) switch
    {
        0 => null,
        1 => JsonValue.Create(r.Next(-5, 50)),
        2 => JsonValue.Create(r.Next(2) == 0),
        3 => JsonValue.Create(Words[r.Next(Words.Length)]),
        4 => RandomArray(r, depth),
        _ => RandomObject(r, depth, keyed ? $"k{r.Next(1000)}" : null),
    };

    private static readonly string[] Words = ["a", "chest", "Ann", "", "Bo's", "the well", "null", "{}", "$x"];
    private static readonly string[] Names = ["id", "kind", "state", "at", "seq", "notice", "gold", "legs", "party", "x"];

    private static JsonObject RandomObject(Random r, int depth, string? id)
    {
        var o = new JsonObject();
        if (id is not null)
            o["id"] = id;
        foreach (var name in Names.OrderBy(_ => r.Next()).Take(r.Next(1, 5)))
            if (name != "id" || id is null)
                o[name] = RandomValue(r, depth + 1, false);
        return o;
    }

    private static JsonArray RandomArray(Random r, int depth)
    {
        var keyed = r.Next(2) == 0;
        var a = new JsonArray();
        var used = new HashSet<string>();
        for (var i = r.Next(0, 6); i > 0; i--)
        {
            var item = keyed ? RandomObject(r, depth + 1, $"k{r.Next(8)}") : RandomValue(r, depth + 1, false);
            if (keyed && !used.Add(item!["id"]!.GetValue<string>()))
                continue;
            a.Add(item);
        }
        return a;
    }

    private static JsonNode? Mutate(Random r, JsonNode? node, int depth)
    {
        if (r.Next(6) == 0)
            return RandomValue(r, depth, false);
        switch (node)
        {
            case JsonObject o:
                foreach (var name in o.Select(p => p.Key).ToList())
                {
                    var roll = r.Next(5);
                    if (roll == 0) o.Remove(name);
                    else if (roll == 1 && name != "id") o[name] = Mutate(r, o[name]?.DeepClone(), depth + 1);
                }
                if (r.Next(3) == 0)
                    o[Names[r.Next(Names.Length)] is var n && n != "id" ? n : "extra"] = RandomValue(r, depth + 1, false);
                return o;
            case JsonArray a:
                var items = a.Select(i => i?.DeepClone()).ToList();
                if (items.Count > 0 && r.Next(3) == 0) items.RemoveAt(r.Next(items.Count));
                if (r.Next(3) == 0) items.Reverse();
                for (var i = 0; i < items.Count; i++)
                    if (r.Next(4) == 0 && items[i] is JsonObject obj)
                        items[i] = Mutate(r, obj, depth + 1) is JsonObject m && obj["id"] is { } id ? WithId(m, id.DeepClone()) : items[i];
                if (r.Next(3) == 0 && items.All(i => i is JsonObject { } x && x["id"] is JsonValue))
                    items.Add(RandomObject(r, depth + 1, $"n{r.Next(1000)}"));
                var result = new JsonArray();
                foreach (var item in items) result.Add(item);
                return result;
            default:
                return RandomValue(r, depth, false);
        }
    }

    private static JsonObject WithId(JsonObject o, JsonNode id)
    {
        o["id"] = id;
        return o;
    }
}
