using System.Text.Json;
using System.Text.RegularExpressions;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Server.Campaigns;
using ArcBox.Server.Profiles;
using ArcBox.Server.Storage;
using ArcBox.Server.Tests.Storage;
using ArcBox.Server.Tests.TestGames;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Campaigns;

public sealed class CampaignServiceTests : IDisposable
{
    private readonly ArcDb _db = StorageTestSupport.NewMemoryDb();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly StorageWriter _writer;
    private readonly ErrorLog _log = new();
    private readonly CampaignService _campaigns;

    private readonly ProfileRecord _dave = new(new ProfileId("pr_dave"), "Dave");
    private readonly ProfileRecord _ann = new(new ProfileId("pr_ann"), "Ann");

    public CampaignServiceTests()
    {
        _writer = new StorageWriter(_db, _time, NullLogger<StorageWriter>.Instance);
        _campaigns = NewService();
    }

    public void Dispose()
    {
        _writer.Dispose();
        _db.Dispose();
    }

    private CampaignService NewService() =>
        new(_db, _writer, new CampaignRulesRegistry([new LedgerRules()]), _time, _log);

    private Task FlushAsync() => _writer.FlushAsync(TestContext.Current.CancellationToken);

    private CampaignInfo NewCampaign(string name = "Embers", ProfileRecord? owner = null) =>
        _campaigns.Create("ledger", name, owner ?? _dave);

    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Note(string text) => JsonSerializer.SerializeToElement(new { text });

    private static string Text(JsonElement e) => e.GetRawText();

    [Fact]
    public void Create_assigns_unique_4_letter_code_and_new_document()
    {
        var a = NewCampaign("One");
        var b = NewCampaign("Two");

        Assert.Matches(new Regex("^[BCDFGHJKLMNPQRSTVWXZ]{4}$"), a.Code);
        Assert.NotEqual(a.Code, b.Code);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal("ledger", a.GameId);
        Assert.Equal(_dave.Id, a.Owner);
        Assert.Equal(0, a.Version);
        var owner = Assert.Single(a.Members);
        Assert.Equal(new CampaignMember(_dave.Id, "Dave", CampaignRole.Owner, _time.GetUtcNow()), owner);
        Assert.Equal("""{"notes":[]}""", Text(_campaigns.DocumentOf(a.Id)));
        Assert.Equal(a, _campaigns.Find(a.Id));
        Assert.Equal(a, _campaigns.FindByCode(a.Code));
        Assert.Equal(a, _campaigns.FindByCode(a.Code.ToLowerInvariant()));
        Assert.Null(_campaigns.Find("nope"));
        Assert.Null(_campaigns.FindByCode("ZZZZ"));
    }

    [Fact]
    public void Create_trims_the_name_and_rejects_unknown_games()
    {
        Assert.Equal("Embers", NewCampaign("  Embers  ").Name);

        var e = Assert.Throws<CampaignException>(() => _campaigns.Create("redline", "Race", _dave));
        Assert.Equal("This game doesn't use campaigns.", e.Message);
    }

    [Fact]
    public void Create_rejects_bad_name_and_21st_owned_campaign()
    {
        foreach (var bad in new[] { "", "   ", new string('x', 33) })
        {
            var e = Assert.Throws<CampaignException>(() => _campaigns.Create("ledger", bad, _dave));
            Assert.Equal("Campaign names are 1–32 characters.", e.Message);
        }

        Assert.Equal(32, _campaigns.Create("ledger", new string('x', 32), _dave).Name.Length);
        for (var i = 1; i < 20; i++) NewCampaign($"C{i}");

        var full = Assert.Throws<CampaignException>(() => NewCampaign("One too many"));
        Assert.Equal("You already own 20 campaigns.", full.Message);

        // The limit is per owner.
        Assert.Equal("Fresh", NewCampaign("Fresh", _ann).Name);
    }

    [Fact]
    public void AddMember_rejects_ninth_member_and_duplicate_name()
    {
        var c = NewCampaign();
        var ann = _campaigns.AddMember(c.Id, _ann);
        Assert.Equal([_dave.Id, _ann.Id], ann.Members.Select(m => m.Profile));
        Assert.Equal(CampaignRole.Player, ann.Members[1].Role);

        // Adding someone who is already in changes nothing.
        Assert.Equal(ann, _campaigns.AddMember(c.Id, _ann));

        var dup = Assert.Throws<CampaignException>(() => _campaigns.AddMember(c.Id, new ProfileRecord(new ProfileId("pr_other"), "ann")));
        Assert.Equal("That name is taken.", dup.Message);

        for (var i = 3; i <= 8; i++) _campaigns.AddMember(c.Id, new ProfileRecord(new ProfileId($"pr_{i}"), $"P{i}"));
        Assert.Equal(8, _campaigns.Find(c.Id)!.Members.Count);

        var ninth = Assert.Throws<CampaignException>(() => _campaigns.AddMember(c.Id, new ProfileRecord(new ProfileId("pr_9"), "P9")));
        Assert.Equal("This campaign is full (8 members).", ninth.Message);

        var missing = Assert.Throws<CampaignException>(() => _campaigns.AddMember("nope", _ann));
        Assert.Equal("Campaign not found.", missing.Message);
    }

    [Fact]
    public void RemoveMember_removes_players_but_never_the_owner()
    {
        var c = NewCampaign();
        _campaigns.AddMember(c.Id, _ann);

        var after = _campaigns.RemoveMember(c.Id, _ann.Id);
        Assert.Equal([_dave.Id], after.Members.Select(m => m.Profile));
        Assert.Equal(after, _campaigns.RemoveMember(c.Id, _ann.Id)); // already gone

        Assert.Throws<CampaignException>(() => _campaigns.RemoveMember(c.Id, _dave.Id));
        Assert.Equal("Campaign not found.", Assert.Throws<CampaignException>(() => _campaigns.RemoveMember("nope", _ann.Id)).Message);
    }

    [Fact]
    public void ListForGame_lists_summaries_of_that_game_newest_first()
    {
        var a = NewCampaign("Old");
        _time.Advance(TimeSpan.FromMinutes(1));
        var b = NewCampaign("New");
        _campaigns.AddMember(b.Id, _ann);

        var list = _campaigns.ListForGame("ledger");
        Assert.Equal(["New", "Old"], list.Select(s => s.Name));
        Assert.Equal(new CampaignSummary(b.Id, b.Code, "New", 2, _time.GetUtcNow()), list[0]);
        Assert.Empty(_campaigns.ListForGame("redline"));
        Assert.NotNull(a);
    }

    [Fact]
    public void CheckOut_by_other_room_returns_null()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA");

        Assert.NotNull(lease);
        Assert.Equal("AAAA", lease.RoomCode);
        Assert.Equal("AAAA", _campaigns.LeasedBy(c.Id));
        Assert.Null(_campaigns.CheckOut(c.Id, "BBBB"));
        Assert.Equal("AAAA", _campaigns.LeasedBy(c.Id));
    }

    [Fact]
    public void CheckOut_same_room_returns_same_lease()
    {
        var c = NewCampaign();
        var first = _campaigns.CheckOut(c.Id, "AAAA");

        Assert.Same(first, _campaigns.CheckOut(c.Id, "AAAA"));
        Assert.Equal(c, first!.Info);
        Assert.Equal(Text(_campaigns.DocumentOf(c.Id)), Text(first.Document));
    }

    [Fact]
    public void CheckOut_of_unknown_campaign_throws()
    {
        Assert.Equal("Campaign not found.", Assert.Throws<CampaignException>(() => _campaigns.CheckOut("nope", "AAAA")).Message);
        Assert.Null(_campaigns.LeasedBy("nope"));
    }

    [Fact]
    public void Release_frees_for_other_room()
    {
        var c = NewCampaign();
        _campaigns.CheckOut(c.Id, "AAAA");

        _campaigns.Release(c.Id, "BBBB"); // not the holder: nothing happens
        Assert.Equal("AAAA", _campaigns.LeasedBy(c.Id));
        Assert.Null(_campaigns.CheckOut(c.Id, "BBBB"));

        _campaigns.Release(c.Id, "AAAA");
        Assert.Null(_campaigns.LeasedBy(c.Id));
        Assert.NotNull(_campaigns.CheckOut(c.Id, "BBBB"));
        Assert.Equal("BBBB", _campaigns.LeasedBy(c.Id));
    }

    [Fact]
    public void A_released_lease_ignores_saves()
    {
        var c = NewCampaign();
        var old = _campaigns.CheckOut(c.Id, "AAAA")!;
        _campaigns.Release(c.Id, "AAAA");

        old.Save(Doc("""{"notes":["late"]}"""));
        old.Checkpoint(Doc("""{"notes":["late"]}"""), "late");
        Assert.Equal("""{"notes":[]}""", Text(_campaigns.DocumentOf(c.Id)));
        Assert.Empty(_campaigns.Snapshots(c.Id));

        // Nor can it clobber the next holder's work.
        var next = _campaigns.CheckOut(c.Id, "BBBB")!;
        next.Save(Doc("""{"notes":["mine"]}"""));
        old.Save(Doc("""{"notes":["late"]}"""));
        Assert.Equal("""{"notes":["mine"]}""", Text(_campaigns.DocumentOf(c.Id)));
    }

    [Fact]
    public async Task Save_bumps_version_and_survives_restart()
    {
        var c = NewCampaign("Embers");
        _campaigns.AddMember(c.Id, _ann);
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;

        lease.Save(Doc("""{"notes":["one"]}"""));
        Assert.Equal(1, lease.Info.Version);
        Assert.Equal("""{"notes":["one"]}""", Text(lease.Document));
        lease.Save(Doc("""{"notes":["one","two"]}"""));
        Assert.Equal(2, lease.Info.Version);
        await FlushAsync();

        var restarted = NewService();
        var loaded = restarted.Find(c.Id)!;
        Assert.Equal(2, loaded.Version);
        Assert.Equal(c.Code, loaded.Code);
        Assert.Equal("Embers", loaded.Name);
        Assert.Equal(_dave.Id, loaded.Owner);
        Assert.Equal(lease.Info.Members, loaded.Members);
        Assert.Equal("""{"notes":["one","two"]}""", Text(restarted.DocumentOf(c.Id)));
        Assert.Equal(c.Id, restarted.FindByCode(c.Code)!.Id);
        Assert.Null(restarted.LeasedBy(c.Id)); // leases live only in memory
        Assert.Equal(_campaigns.Snapshots(c.Id), restarted.Snapshots(c.Id));
    }

    [Fact]
    public async Task Ten_saves_coalesce_to_one_write()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;
        lease.Save(Doc("""{"notes":[]}""")); // the first save also records an autosave snapshot
        await FlushAsync();

        var before = _writer.WritesExecuted;
        for (var i = 0; i < 10; i++) lease.Save(Doc($$"""{"notes":["{{i}}"]}"""));
        await FlushAsync();

        Assert.Equal(1, _writer.WritesExecuted - before);
        Assert.Equal("""{"notes":["9"]}""", _db.Scalar<string>($"SELECT document FROM campaigns WHERE id = '{c.Id}'"));
        Assert.Equal(11, _db.Scalar<int>($"SELECT version FROM campaigns WHERE id = '{c.Id}'"));
    }

    [Fact]
    public void Autosave_snapshot_at_most_every_10_minutes()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;

        lease.Save(Doc("""{"notes":["a"]}"""));
        var first = Assert.Single(_campaigns.Snapshots(c.Id));
        Assert.Equal("Autosave", first.Label);
        Assert.Equal(1, first.Version);
        Assert.Equal(_time.GetUtcNow(), first.CreatedUtc);

        _time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        lease.Save(Doc("""{"notes":["b"]}"""));
        Assert.Single(_campaigns.Snapshots(c.Id));

        _time.Advance(TimeSpan.FromSeconds(1));
        lease.Save(Doc("""{"notes":["c"]}"""));
        Assert.Equal([3L, 1L], _campaigns.Snapshots(c.Id).Select(s => s.Version)); // newest first

        _time.Advance(TimeSpan.FromMinutes(5));
        lease.Save(Doc("""{"notes":["d"]}"""));
        Assert.Equal(2, _campaigns.Snapshots(c.Id).Count);
    }

    [Fact]
    public void A_checkpoint_resets_the_autosave_clock()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;
        lease.Checkpoint(Doc("""{"notes":["a"]}"""), "End of session 1");
        _time.Advance(TimeSpan.FromMinutes(5));
        lease.Save(Doc("""{"notes":["b"]}"""));

        var only = Assert.Single(_campaigns.Snapshots(c.Id));
        Assert.Equal("End of session 1", only.Label);
    }

    [Fact]
    public async Task Checkpoint_always_snapshots_and_prunes_to_50()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;

        for (var i = 1; i <= 55; i++) lease.Checkpoint(Doc($$"""{"notes":["{{i}}"]}"""), $"Cp {i}");

        var snapshots = _campaigns.Snapshots(c.Id);
        Assert.Equal(50, snapshots.Count);
        Assert.Equal("Cp 55", snapshots[0].Label);
        Assert.Equal("Cp 6", snapshots[^1].Label);
        Assert.Equal(55, lease.Info.Version);

        await FlushAsync();
        Assert.Equal(50, _db.Scalar<int>($"SELECT COUNT(*) FROM campaign_snapshots WHERE campaign_id = '{c.Id}'"));
        Assert.Equal("Cp 6", _db.Scalar<string>($"SELECT label FROM campaign_snapshots WHERE campaign_id = '{c.Id}' ORDER BY id LIMIT 1"));
        Assert.Equal(snapshots, NewService().Snapshots(c.Id));
    }

    [Fact]
    public async Task Oversized_save_is_ignored()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;
        lease.Save(Doc("""{"notes":["ok"]}"""));
        await FlushAsync();

        var big = Doc($$"""{"notes":["{{new string('x', 2 * 1024 * 1024)}}"]}""");
        lease.Save(big);
        lease.Checkpoint(big, "Too big");

        Assert.Equal(1, lease.Info.Version);
        Assert.Equal("""{"notes":["ok"]}""", Text(lease.Document));
        Assert.Equal(["Autosave"], _campaigns.Snapshots(c.Id).Select(s => s.Label));
        Assert.Equal(2, _log.Errors.Count);
        Assert.Contains("2 MB", _log.Errors[0]);
        var before = _writer.WritesExecuted;
        await FlushAsync();
        Assert.Equal(before, _writer.WritesExecuted);
    }

    [Fact]
    public void Document_size_is_utf8_bytes_and_exactly_2_MB_is_allowed()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;
        const string wrapper = """{"notes":[""]}""";
        var room = CampaignService.MaxDocumentBytes - wrapper.Length;

        lease.Save(Doc($$"""{"notes":["{{new string('x', room)}}"]}"""));
        Assert.Equal(1, lease.Info.Version);

        // The same number of characters, but 'é' is two bytes in UTF-8.
        lease.Save(Doc($$"""{"notes":["{{new string('é', room)}}"]}"""));
        Assert.Equal(1, lease.Info.Version);
        Assert.Single(_log.Errors);
    }

    [Fact]
    public void RunCommand_statuses()
    {
        var c = NewCampaign();
        _campaigns.AddMember(c.Id, _ann);
        var stranger = new ProfileId("pr_stranger");

        Assert.Null(_campaigns.RunCommand("ZZZZ", _dave.Id, "note", Note("hi"), 0));

        Assert.Equal(CommandStatus.NotMember, _campaigns.RunCommand(c.Code, stranger, "note", Note("hi"), 0)!.Status);

        var stale = _campaigns.RunCommand(c.Code, _dave.Id, "note", Note("hi"), 7)!;
        Assert.Equal(CommandStatus.VersionConflict, stale.Status);
        Assert.Equal(0, stale.Info.Version);
        Assert.Equal("""{"notes":[]}""", Text(stale.View));

        var rejected = _campaigns.RunCommand(c.Code, _dave.Id, "note", Note(""), 0)!;
        Assert.Equal(CommandStatus.Rejected, rejected.Status);
        Assert.Equal("Write something.", rejected.Error);
        Assert.Equal(0, _campaigns.Find(c.Id)!.Version);

        var ok = _campaigns.RunCommand(c.Code.ToLowerInvariant(), _ann.Id, "note", Note("hi"), 0)!;
        Assert.Equal(CommandStatus.Ok, ok.Status);
        Assert.Null(ok.Error);
        Assert.Equal(1, ok.Info.Version);
        Assert.Equal("""{"notes":["hi"]}""", Text(ok.View));
        Assert.Equal("""{"notes":["hi"]}""", Text(_campaigns.DocumentOf(c.Id)));

        var lease = _campaigns.CheckOut(c.Id, "AAAA");
        Assert.NotNull(lease);
        Assert.Equal(CommandStatus.SessionInProgress, _campaigns.RunCommand(c.Code, _dave.Id, "note", Note("again"), 1)!.Status);
    }

    [Fact]
    public async Task A_command_is_persisted_and_the_next_one_needs_the_new_version()
    {
        var c = NewCampaign();
        Assert.Equal(CommandStatus.Ok, _campaigns.RunCommand(c.Code, _dave.Id, "note", Note("one"), 0)!.Status);
        Assert.Equal(CommandStatus.VersionConflict, _campaigns.RunCommand(c.Code, _dave.Id, "note", Note("two"), 0)!.Status);
        Assert.Equal(CommandStatus.Ok, _campaigns.RunCommand(c.Code, _dave.Id, "note", Note("two"), 1)!.Status);
        await FlushAsync();

        var restarted = NewService();
        Assert.Equal(2, restarted.Find(c.Id)!.Version);
        Assert.Equal("""{"notes":["one","two"]}""", Text(restarted.DocumentOf(c.Id)));
    }

    [Fact]
    public void Command_after_checkout_is_SessionInProgress_and_document_unchanged()
    {
        var c = NewCampaign();
        // The phone loaded the portal at version 0 ...
        Assert.Equal(0, _campaigns.Find(c.Id)!.Version);

        // ... a room starts its session before the command arrives ...
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;

        // ... so the command is refused, and nothing changes.
        var outcome = _campaigns.RunCommand(c.Code, _dave.Id, "note", Note("late"), 0)!;
        Assert.Equal(CommandStatus.SessionInProgress, outcome.Status);
        Assert.Equal("""{"notes":[]}""", Text(_campaigns.DocumentOf(c.Id)));
        Assert.Equal(0, _campaigns.Find(c.Id)!.Version);
        Assert.Equal("""{"notes":[]}""", Text(lease.Document));
        Assert.Equal("""{"notes":[]}""", Text(outcome.View)); // the portal can still read
    }

    [Fact]
    public async Task Command_racing_a_checkout_either_wins_cleanly_or_is_refused()
    {
        for (var round = 0; round < 50; round++)
        {
            var c = NewCampaign($"Race{round}", new ProfileRecord(new ProfileId($"pr_r{round}"), "Dave"));
            CommandOutcome? outcome = null;
            CampaignLease? lease = null;
            var start = new ManualResetEventSlim();
            var command = Task.Run(() => { start.Wait(); outcome = _campaigns.RunCommand(c.Code, c.Owner, "note", Note("x"), 0); }, TestContext.Current.CancellationToken);
            var checkout = Task.Run(() => { start.Wait(); lease = _campaigns.CheckOut(c.Id, "AAAA"); }, TestContext.Current.CancellationToken);
            start.Set();
            await Task.WhenAll(command, checkout);

            Assert.NotNull(lease); // a command never takes the lease away
            var ledger = Text(_campaigns.DocumentOf(c.Id));
            if (outcome!.Status == CommandStatus.Ok) Assert.Equal("""{"notes":["x"]}""", ledger);
            else
            {
                Assert.Equal(CommandStatus.SessionInProgress, outcome.Status);
                Assert.Equal("""{"notes":[]}""", ledger);
            }
        }
    }

    [Fact]
    public void PortalView_is_the_rules_view_for_members_only()
    {
        var c = NewCampaign();
        Assert.Equal("""{"notes":[]}""", Text(_campaigns.PortalView(c.Id, _dave.Id)));
        Assert.Equal("Campaign not found.", Assert.Throws<CampaignException>(() => _campaigns.PortalView(c.Id, _ann.Id)).Message);
        Assert.Equal("Campaign not found.", Assert.Throws<CampaignException>(() => _campaigns.PortalView("nope", _dave.Id)).Message);
    }

    [Fact]
    public async Task Rollback_refused_while_leased_and_restores_otherwise()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;
        lease.Checkpoint(Doc("""{"notes":["one"]}"""), "End of session 1");
        lease.Save(Doc("""{"notes":["one","two"]}"""));
        var snapshot = Assert.Single(_campaigns.Snapshots(c.Id));

        Assert.False(_campaigns.Rollback(c.Id, snapshot.Id));
        Assert.Equal("""{"notes":["one","two"]}""", Text(_campaigns.DocumentOf(c.Id)));

        _campaigns.Release(c.Id, "AAAA");
        Assert.False(_campaigns.Rollback(c.Id, 9999));
        Assert.False(_campaigns.Rollback("nope", snapshot.Id));
        Assert.True(_campaigns.Rollback(c.Id, snapshot.Id));

        Assert.Equal("""{"notes":["one"]}""", Text(_campaigns.DocumentOf(c.Id)));
        Assert.Equal(3, _campaigns.Find(c.Id)!.Version); // forward, so a stale phone still conflicts
        Assert.Single(_campaigns.Snapshots(c.Id));       // the snapshot stays

        await FlushAsync();
        var restarted = NewService();
        Assert.Equal("""{"notes":["one"]}""", Text(restarted.DocumentOf(c.Id)));
        Assert.Equal(3, restarted.Find(c.Id)!.Version);
    }

    [Fact]
    public async Task Snapshot_ids_stay_unique_after_a_restart()
    {
        var c = NewCampaign();
        var lease = _campaigns.CheckOut(c.Id, "AAAA")!;
        lease.Checkpoint(Doc("""{"notes":["1"]}"""), "One");
        await FlushAsync();

        var restarted = NewService();
        var again = restarted.CheckOut(c.Id, "AAAA")!;
        again.Checkpoint(Doc("""{"notes":["2"]}"""), "Two");
        await FlushAsync();

        var ids = restarted.Snapshots(c.Id).Select(s => s.Id).ToList();
        Assert.Equal(2, ids.Distinct().Count());
        Assert.Equal(2, _db.Scalar<int>("SELECT COUNT(*) FROM campaign_snapshots"));
    }

    [Fact]
    public void Registry_finds_rules_by_game()
    {
        var registry = new CampaignRulesRegistry([new LedgerRules()]);
        Assert.IsType<LedgerRules>(registry.For("ledger"));
        Assert.Null(registry.For("redline"));
    }

    [Fact]
    public void LedgerRules_card_uses_the_members_display_name()
    {
        var c = _campaigns.AddMember(NewCampaign().Id, _ann);
        var card = new LedgerRules().CardFor(c, _campaigns.DocumentOf(c.Id), _ann.Id);
        Assert.Equal("Hero of Ann", card.Title);
        Assert.Equal("Lv 1", card.Subtitle);
    }

    [Fact]
    public void ForMember_lists_campaigns_the_profile_is_in_newest_first()
    {
        var embers = NewCampaign("Embers");
        _time.Advance(TimeSpan.FromMinutes(1));
        var frost = NewCampaign("Frost", _ann);
        _time.Advance(TimeSpan.FromMinutes(1));
        NewCampaign("Elsewhere", _ann);
        _campaigns.AddMember(embers.Id, _ann);
        _campaigns.AddMember(frost.Id, _dave);
        _time.Advance(TimeSpan.FromMinutes(1));
        _campaigns.AddMember(embers.Id, new ProfileRecord(new ProfileId("pr_bea"), "Bea")); // Embers changes last

        var mine = _campaigns.ForMember(_dave.Id);

        Assert.Equal(["Embers", "Frost"], mine.Select(c => c.Name));
        Assert.Equal((embers.Id, embers.Code, 3), (mine[0].Id, mine[0].Code, mine[0].Members));
        Assert.Equal(["Elsewhere", "Embers", "Frost"], _campaigns.ForMember(_ann.Id).Select(c => c.Name).Order());
        Assert.Empty(_campaigns.ForMember(new ProfileId("pr_nobody")));
    }

    private sealed class ErrorLog : ILogger<CampaignService>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error) lock (Errors) Errors.Add(formatter(state, exception));
        }
    }
}
