using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Tests.Support;

namespace ArcBox.Server.Tests.Content;

public sealed class ContentEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(string PackId, string Hash)> LedgerPackAsync(HttpClient http)
    {
        var index = await http.GetFromJsonAsync<JsonElement>("/content/ledger/index.json", Ct);
        var pack = index[0];
        return (pack.GetProperty("packId").GetString()!, pack.GetProperty("hash").GetString()!);
    }

    [Fact]
    public async Task Index_lists_packs()
    {
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();

        var response = await http.GetAsync("/content/ledger/index.json", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var index = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(1, index.GetArrayLength());
        var pack = index[0];
        Assert.Equal("base", pack.GetProperty("packId").GetString());
        Assert.Matches("^[0-9a-f]{64}$", pack.GetProperty("hash").GetString()!);
        var files = pack.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(["blob.xyzunknown", "hello.txt", "maps/arena.json"], files.Select(x => x.GetProperty("path").GetString()));
        Assert.Equal(5, files[1].GetProperty("bytes").GetInt64());
        Assert.Matches("^[0-9a-f]{64}$", files[1].GetProperty("sha256").GetString()!);

        // A game without packs has an empty index rather than an error.
        Assert.Equal("[]", await http.GetStringAsync("/content/counter/index.json", Ct));
    }

    [Fact]
    public async Task File_served_immutable()
    {
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();
        var (packId, hash) = await LedgerPackAsync(http);

        var response = await http.GetAsync($"/content/ledger/{packId}/{hash}/maps/arena.json", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"w\":8}", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());

        var text = await http.GetAsync($"/content/ledger/{packId}/{hash}/hello.txt", Ct);
        Assert.Equal("hello", await text.Content.ReadAsStringAsync(Ct));
        Assert.Equal("text/plain", text.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_extension_is_octet_stream()
    {
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();
        var (packId, hash) = await LedgerPackAsync(http);

        var response = await http.GetAsync($"/content/ledger/{packId}/{hash}/blob.xyzunknown", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("ledger", "base", false, "hello.txt")]            // wrong hash
    [InlineData("ledger", "nope", true, "hello.txt")]             // unknown pack
    [InlineData("nope", "base", true, "hello.txt")]               // unknown game
    [InlineData("ledger", "base", true, "missing.txt")]           // not a file in the pack
    [InlineData("ledger", "base", true, "maps")]                  // a directory
    [InlineData("ledger", "base", true, "maps/")]                 // a directory with a slash
    public async Task Unknown_hash_404(string gameId, string packId, bool realHash, string path)
    {
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();
        var real = (await LedgerPackAsync(http)).Hash;

        var url = $"/content/{gameId}/{packId}/{(realHash ? real : new string('0', 64))}/{path}";
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(url, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("..%2F..%2Fappsettings.json")]
    [InlineData("%2E%2E%2F%2E%2E%2Fappsettings.json")]
    [InlineData("%2E%2E/%2E%2E/appsettings.json")]
    [InlineData("..%2F..%2F..%2F..%2F..%2Fetc%2Fpasswd")]
    [InlineData("maps/..%2F..%2Fappsettings.json")]
    [InlineData("%2Fetc%2Fpasswd")]
    [InlineData("/etc/passwd")]
    [InlineData("..%5C..%5Cappsettings.json")]
    public async Task Traversal_404(string path)
    {
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();
        var (packId, hash) = await LedgerPackAsync(http);

        var response = await http.GetAsync($"/content/ledger/{packId}/{hash}/{path}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Host_gets_ContentRequired_on_select_start_and_resume()
    {
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();
        var (packId, hash) = await LedgerPackAsync(http);
        await using var host = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await using var phone = await TestClient.ConnectAsync(f);
        await phone.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));

        // Before a game is selected there is nothing to require.
        Assert.Equal(0, host.Unclaimed(ClientMethods.ContentRequired));

        await host.InvokeAsync("SelectGame", "ledger");
        AssertPacks(await host.WaitForArgsAsync(ClientMethods.ContentRequired), "ledger", (packId, hash));

        // Ledger uses campaigns: the VIP makes one.
        await phone.InvokeAsync<ProfileKey>("CreateCampaign", "Embers");
        await host.InvokeAsync("StartGame");
        AssertPacks(await host.WaitForArgsAsync(ClientMethods.ContentRequired), "ledger", (packId, hash));

        // A TV that reconnects is told again.
        await using var tv = await TestClient.ConnectAsync(f);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        AssertPacks(await tv.WaitForArgsAsync(ClientMethods.ContentRequired), "ledger", (packId, hash));

        // A game with no packs gets an empty list, every time.
        await using var host2 = await TestClient.ConnectAsync(f);
        var room2 = await host2.InvokeAsync<CreateRoomResult>("CreateRoom");
        await host2.InvokeAsync("SelectGame", "counter");
        AssertPacks(await host2.WaitForArgsAsync(ClientMethods.ContentRequired), "counter");
        await using var phone2 = await TestClient.ConnectAsync(f);
        await phone2.TakeSeatAsync(new TakeSeatRequest(room2.Code, NewName: "Bea"));
        await host2.InvokeAsync("StartGame");
        AssertPacks(await host2.WaitForArgsAsync(ClientMethods.ContentRequired), "counter");
    }

    [Fact]
    public async Task Rebuilt_room_tells_the_resuming_host()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("arcbox-content-db-").FullName, "arcbox.db");
        try
        {
            CreateRoomResult room;
            (string, string) pack;
            using (var a = new ArcServerFactory(file))
            {
                using var http = a.CreateClient();
                pack = await LedgerPackAsync(http);
                await using var host = await TestClient.ConnectAsync(a);
                room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
                await host.InvokeAsync("SelectGame", "ledger");
            }

            using var b = new ArcServerFactory(file);
            await using var tv = await TestClient.ConnectAsync(b);
            await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);

            AssertPacks(await tv.WaitForArgsAsync(ClientMethods.ContentRequired), "ledger", pack);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch (IOException) { }
        }
    }

    private static void AssertPacks(JsonElement[] args, string gameId, params (string PackId, string Hash)[] expected)
    {
        Assert.Equal(2, args.Length);
        Assert.Equal(gameId, args[0].GetString());
        Assert.Equal(expected, args[1].EnumerateArray()
            .Select(p => (p.GetProperty("packId").GetString()!, p.GetProperty("hash").GetString()!)));
    }
}
