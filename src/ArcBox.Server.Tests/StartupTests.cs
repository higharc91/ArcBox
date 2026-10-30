using System.Text.Json;
using ArcBox.Server.Tests.Storage;
using ArcBox.Server.Tests.Support;

namespace ArcBox.Server.Tests;

/// <summary>The server loads its stored state when it starts (spec §4), so bad data stops it there.</summary>
public sealed class StartupTests
{
    [Fact]
    public void A_corrupt_campaign_row_fails_the_server_at_startup()
    {
        var dir = Directory.CreateTempSubdirectory("arcbox-startup-");
        try
        {
            var options = StorageTestSupport.FileOptions(dir.FullName);
            using (var db = StorageTestSupport.NewDb(options))
            {
                db.Execute("""
                    INSERT INTO campaigns (id, code, game_id, name, owner, members_json, document, version, updated_utc)
                    VALUES ('cm_bad', 'BBBB', 'ledger', 'Bad', 'pr_m1', 'not json', '{"notes":[]}', 0, '2026-01-01T00:00:00Z')
                    """);
            }

            using var f = new ArcServerFactory(options.Path);

            // Starting the server (as the first request would) fails with the row's error, not later on a hub call. Today
            // the hosted RoomSweeper's dependencies (RoomManager, RoomServices) load the services as the host starts.
            var ex = Assert.ThrowsAny<Exception>(() => f.Server);
            Assert.Contains(Flatten(ex), e => e is JsonException);
        }
        finally
        {
            try { dir.Delete(true); } catch (IOException) { }
        }
    }

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            yield return e;
            if (e is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
