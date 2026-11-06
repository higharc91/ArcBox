namespace ArcBox.Host.Core.Tests;

public sealed class ContentPlanTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static InstalledFiles Version(string hash, params (string Path, long Bytes)[] files) =>
        new(hash, files.ToDictionary(f => f.Path, f => f.Bytes, StringComparer.Ordinal));

    private static InstalledVersion V(string pack, string hash, int hoursAgo, long bytes = 100, string game = "tactics") =>
        new(game, pack, hash, T0.AddHours(-hoursAgo), bytes);

    // ---- Sources ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_with_the_same_path_and_length_is_copied_from_the_newest_version_first()
    {
        var wanted = new[] { new PackFile("rules/world/ashen_ford.json", "aa", 10) };
        var installed = new[] { Version("new", ("rules/world/ashen_ford.json", 10)), Version("old", ("rules/world/ashen_ford.json", 10)) };

        Assert.Equal(["new", "old"], ContentPlan.Sources(wanted, installed)[0]);
    }

    [Fact]
    public void A_changed_length_a_new_file_or_a_moved_file_is_downloaded()
    {
        var wanted = new[]
        {
            new PackFile("a.json", "aa", 11),      // grew
            new PackFile("b.json", "bb", 5),       // new
            new PackFile("moved/c.png", "cc", 7),  // moved: (path, sha) is the key, so it's fetched again
        };
        var installed = new[] { Version("old", ("a.json", 10), ("c.png", 7)) };

        Assert.All(ContentPlan.Sources(wanted, installed), Assert.Empty);
    }

    [Fact]
    public void Paths_match_exactly()
    {
        var wanted = new[] { new PackFile("Maps/A.json", "aa", 1) };
        var installed = new[] { Version("old", ("maps/a.json", 1)) };

        Assert.Empty(ContentPlan.Sources(wanted, installed)[0]);
    }

    [Fact]
    public void Nothing_installed_downloads_everything_in_order()
    {
        var wanted = new[] { new PackFile("a", "1", 1), new PackFile("b", "2", 2) };

        var sources = ContentPlan.Sources(wanted, []);

        Assert.Equal(2, sources.Count);
        Assert.All(sources, Assert.Empty);
    }

    [Fact]
    public void Sources_model_every_candidate_has_the_files_path_and_length_in_install_order()
    {
        var random = new Random(7);
        for (var round = 0; round < 300; round++)
        {
            var paths = Enumerable.Range(0, 6).Select(i => $"p{i}").ToList();
            var installed = Enumerable.Range(0, random.Next(0, 4))
                .Select(v => Version($"v{v}", [.. paths.Where(_ => random.Next(2) == 0).Select(p => (p, (long)random.Next(1, 4)))]))
                .ToList();
            var wanted = paths.Where(_ => random.Next(3) > 0).Select(p => new PackFile(p, "x", random.Next(1, 4))).ToList();

            var sources = ContentPlan.Sources(wanted, installed);

            Assert.Equal(wanted.Count, sources.Count);
            for (var i = 0; i < wanted.Count; i++)
            {
                var expected = installed
                    .Where(v => v.Files.TryGetValue(wanted[i].Path, out var b) && b == wanted[i].Bytes)
                    .Select(v => v.Hash);
                Assert.Equal(expected, sources[i]);
            }
        }
    }

    // ---- Evict ---------------------------------------------------------------------------------------------

    [Fact]
    public void Keeps_the_newest_per_pack_with_the_current_counted_newest()
    {
        var versions = new[] { V("base", "a", 1), V("base", "b", 2), V("base", "c", 3), V("base", "d", 4) };
        var current = V("base", "d", 0);   // d was just used again

        var evicted = ContentPlan.Evict(versions, current, keepPerPack: 3, maxBytes: long.MaxValue);

        Assert.Equal(["c"], evicted.Select(v => v.Hash));
    }

    [Fact]
    public void The_size_limit_evicts_least_recently_used_across_packs()
    {
        var versions = new[]
        {
            V("base", "b1", 1, 100), V("base", "b2", 5, 100),
            V("extra", "e1", 2, 100), V("extra", "e2", 4, 100),
            V("base", "b0", 0, 100),
        };
        var current = V("base", "b0", 0, 100);

        // 500 bytes, 300 allowed: oldest first is b2 (5h) and e2 (4h).
        var evicted = ContentPlan.Evict(versions, current, keepPerPack: 3, maxBytes: 300);

        Assert.Equal(["b2", "e2"], evicted.Select(v => v.Hash));
    }

    [Fact]
    public void The_size_limit_never_takes_a_packs_newest_or_the_version_before_the_current()
    {
        var versions = new[]
        {
            V("base", "now", 0, 1000), V("base", "previous", 1, 1000), V("base", "older", 2, 1000),
            V("extra", "only", 10, 1000),
        };
        var current = V("base", "now", 0, 1000);

        var evicted = ContentPlan.Evict(versions, current, keepPerPack: 3, maxBytes: 0);

        Assert.Equal(["older"], evicted.Select(v => v.Hash));
    }

    [Fact]
    public void A_current_version_not_yet_listed_is_counted()
    {
        var versions = new[] { V("base", "a", 1, 100), V("base", "b", 2, 100) };
        var current = V("base", "new", 0, 100);

        Assert.Equal(["b"], ContentPlan.Evict(versions, current, keepPerPack: 3, maxBytes: 200).Select(v => v.Hash));
        Assert.Empty(ContentPlan.Evict(versions, current, keepPerPack: 3, maxBytes: 300));
    }

    [Fact]
    public void Same_pack_id_in_another_game_is_another_pack()
    {
        var versions = new[] { V("base", "x", 1, game: "redline"), V("base", "y", 2, game: "redline") };
        var current = V("base", "new", 0);

        Assert.Empty(ContentPlan.Evict(versions, current, keepPerPack: 2, maxBytes: long.MaxValue));
    }

    [Fact]
    public void Evict_model_holds_its_invariants()
    {
        var events = int.TryParse(Environment.GetEnvironmentVariable("CONTENT_MODEL_EVENTS"), out var n) ? n : 2000;
        var random = new Random(11);
        for (var round = 0; round < events; round++)
        {
            var packs = new[] { ("tactics", "base"), ("tactics", "extra"), ("redline", "base") };
            var versions = new List<InstalledVersion>();
            foreach (var (game, pack) in packs)
            {
                for (var i = random.Next(0, 6); i > 0; i--)
                {
                    versions.Add(new InstalledVersion(game, pack, $"{pack}{versions.Count}", T0.AddMinutes(-random.Next(0, 50)), random.Next(0, 200)));
                }
            }
            var (cg, cp) = packs[random.Next(packs.Length)];
            var current = random.Next(3) == 0 && versions.FirstOrDefault(v => v.GameId == cg && v.PackId == cp) is { } existing
                ? existing with { LastUsedUtc = T0 }
                : new InstalledVersion(cg, cp, $"cur{round}", T0, random.Next(0, 200));
            var keep = random.Next(1, 5);
            var max = (long)random.Next(0, 1500);

            var evicted = ContentPlan.Evict(versions, current, keep, max);

            bool Same(InstalledVersion a, InstalledVersion b) => a.GameId == b.GameId && a.PackId == b.PackId && a.Hash == b.Hash;
            var all = versions.Where(v => !Same(v, current)).Append(current).ToList();
            var left = all.Where(v => !evicted.Any(e => Same(e, v))).ToList();

            Assert.DoesNotContain(evicted, e => Same(e, current));
            Assert.All(evicted, e => Assert.Contains(all, v => Same(v, e)));
            Assert.Equal(evicted.Count, evicted.DistinctBy(e => (e.GameId, e.PackId, e.Hash)).Count());
            Assert.Equal(evicted.OrderBy(e => e.LastUsedUtc).ThenBy(e => e.Hash, StringComparer.Ordinal), evicted);

            foreach (var pack in all.GroupBy(v => (v.GameId, v.PackId)))
            {
                var kept = left.Where(v => (v.GameId, v.PackId) == pack.Key).ToList();
                Assert.InRange(kept.Count, 1, keep);   // never emptied, never over the count
                var newestOther = pack.Where(v => !Same(v, current)).OrderByDescending(v => v.LastUsedUtc).ThenBy(v => v.Hash, StringComparer.Ordinal).FirstOrDefault();
                if (pack.Key == (current.GameId, current.PackId))
                {
                    if (keep >= 2 && newestOther is not null)
                    {
                        Assert.Contains(left, v => Same(v, newestOther));   // a running game may still read it
                    }
                }
                else
                {
                    Assert.Contains(left, v => Same(v, newestOther!));
                }
            }

            // Over the limit only when nothing more may go: every survivor is a pack's newest, or the one before the current.
            if (left.Sum(v => v.Bytes) > max)
            {
                var deletable = left.Where(v =>
                {
                    var pack = left.Where(o => (o.GameId, o.PackId) == (v.GameId, v.PackId))
                        .OrderByDescending(o => Same(o, current)).ThenByDescending(o => o.LastUsedUtc).ThenBy(o => o.Hash, StringComparer.Ordinal).ToList();
                    var guarded = (v.GameId, v.PackId) == (current.GameId, current.PackId) ? 2 : 1;
                    return pack.IndexOf(v) >= guarded;
                });
                Assert.Empty(deletable);
            }

            // The size rule stops as soon as it can: putting back the last version it took would break the limit, and
            // it takes the least recently used first.
            var overCount = all.GroupBy(v => (v.GameId, v.PackId))
                .SelectMany(g => g.OrderByDescending(o => Same(o, current)).ThenByDescending(o => o.LastUsedUtc).ThenBy(o => o.Hash, StringComparer.Ordinal).Skip(keep))
                .ToList();
            var bySize = evicted.Where(e => !overCount.Any(o => Same(o, e))).ToList();
            if (bySize.Count > 0)
            {
                Assert.True(left.Sum(v => v.Bytes) + bySize[^1].Bytes > max, $"round {round}: {bySize[^1].Hash} was evicted though it fit");
                var deletableLeft = left.Where(v => !Same(v, current)).Where(v =>
                {
                    var pack = all.Where(o => (o.GameId, o.PackId) == (v.GameId, v.PackId))
                        .OrderByDescending(o => Same(o, current)).ThenByDescending(o => o.LastUsedUtc).ThenBy(o => o.Hash, StringComparer.Ordinal).ToList();
                    var guarded = (v.GameId, v.PackId) == (current.GameId, current.PackId) ? 2 : 1;
                    return pack.IndexOf(v) >= Math.Min(guarded, keep);
                });
                Assert.All(deletableLeft, v => Assert.True(v.LastUsedUtc >= bySize[^1].LastUsedUtc, $"round {round}: kept {v.Hash}, older than evicted {bySize[^1].Hash}"));
            }
        }
    }
}
