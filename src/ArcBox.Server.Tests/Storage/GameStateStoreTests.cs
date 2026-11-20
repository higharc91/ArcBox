using System.Text.Json;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Storage;

/// <summary>The <c>room_game_state</c> table behind <c>ISnapshotGame</c> (Meridian M6 plan, Task 1).</summary>
public sealed class GameStateStoreTests : IDisposable
{
    private readonly ArcDb _db = StorageTestSupport.NewMemoryDb();
    private readonly StorageWriter _writer;
    private readonly GameStateStore _store;
    private readonly ListLog _log = new();

    public GameStateStoreTests()
    {
        using (var connection = _db.Open())
        {
            Migrations.Apply(connection);
        }
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _writer = new StorageWriter(_db, time, NullLogger<StorageWriter>.Instance);
        _store = new GameStateStore(_db, _writer, time, _log);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _db.Dispose();
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private Task Flush() => _writer.FlushAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_snapshot_is_saved_loaded_and_deleted()
    {
        Assert.True(_store.Save("ABCD", "meridian", Json(new { turn = 3 })));
        Assert.Equal(3, _store.Load("ABCD", "meridian")!.Value.GetProperty("turn").GetInt32());   // before it's written
        await Flush();
        Assert.Equal(3, _store.Load("ABCD", "meridian")!.Value.GetProperty("turn").GetInt32());   // from the database
        Assert.Equal(1, _db.Scalar<int>("SELECT COUNT(*) FROM room_game_state"));

        _store.Delete("ABCD");
        Assert.Null(_store.Load("ABCD", "meridian"));
        await Flush();
        Assert.Null(_store.Load("ABCD", "meridian"));
        Assert.Equal(0, _db.Scalar<int>("SELECT COUNT(*) FROM room_game_state"));
    }

    [Fact]
    public async Task The_newest_write_wins_and_a_delete_beats_a_queued_save()
    {
        for (var turn = 1; turn <= 5; turn++)
        {
            _store.Save("ABCD", "meridian", Json(new { turn }));
        }
        await Flush();
        Assert.Equal(5, _store.Load("ABCD", "meridian")!.Value.GetProperty("turn").GetInt32());

        _store.Save("ABCD", "meridian", Json(new { turn = 6 }));
        _store.Delete("ABCD");   // the game ended while the save waited
        await Flush();
        Assert.Null(_store.Load("ABCD", "meridian"));
    }

    [Fact]
    public async Task Another_games_snapshot_is_not_handed_back()
    {
        _store.Save("ABCD", "counter", Json(new { n = 1 }));
        await Flush();
        Assert.Null(_store.Load("ABCD", "meridian"));
    }

    [Fact]
    public async Task An_oversized_snapshot_is_refused_and_the_previous_one_stands()
    {
        _store.Save("ABCD", "meridian", Json(new { turn = 1 }));
        Assert.False(_store.Save("ABCD", "meridian", Json(new { pad = new string('x', GameStateStore.MaxBytes) })));
        await Flush();
        Assert.Equal(1, _store.Load("ABCD", "meridian")!.Value.GetProperty("turn").GetInt32());
        Assert.Contains(_log.Lines, l => l.Contains("over"));
    }

    [Fact]
    public async Task Snapshots_of_rooms_that_are_gone_are_swept()
    {
        _db.Execute("""
            INSERT INTO rooms (code, host_token_hash, slots_json, next_slot_id, created_utc, updated_utc)
            VALUES ('KEEP', 'h', '[]', 1, 't', 't')
            """);
        _store.Save("KEEP", "meridian", Json(new { turn = 1 }));
        _store.Save("GONE", "meridian", Json(new { turn = 1 }));
        await Flush();
        Assert.Equal(1, _store.DeleteOrphans());
        Assert.NotNull(_store.Load("KEEP", "meridian"));
        Assert.Null(_store.Load("GONE", "meridian"));
    }

    private sealed class ListLog : Microsoft.Extensions.Logging.ILogger<GameStateStore>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id, TState state,
                                Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
