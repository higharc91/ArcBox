using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using ArcBox.Server.Campaigns;
using ArcBox.Server.Games;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArcBox.Host.Core.Tests.Support;

/// <summary>The real server in memory (minimal copy of ArcBox.Server.Tests' factory).</summary>
public sealed class HostServerFactory : WebApplicationFactory<Program>
{
    private readonly DirectoryInfo _contentRoot = Directory.CreateTempSubdirectory("arcbox-host-content-");

    /// <summary>
    /// The server's <c>Content:Root</c>: a private directory holding one pack for <see cref="SignalEchoGame"/>,
    /// <c>signal-echo/<see cref="PackId"/></c>, whose file names need URL-encoding.
    /// </summary>
    public string ContentRoot => _contentRoot.FullName;

    public const string PackId = "base";

    /// <summary>The pack's files (relative path with '/' separators → bytes), as seeded under <see cref="ContentRoot"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> PackFiles { get; } = new Dictionary<string, byte[]>
    {
        ["manifest.json"] = "{\"name\":\"base\"}"u8.ToArray(),
        ["maps/level one.json"] = "{\"tiles\":[1,2,3]}"u8.ToArray(),
        ["notes #1 100%.txt"] = "hash and percent in a name"u8.ToArray(),
        ["art/caf\u00e9.bin"] = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 31 % 251)).ToArray(),
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        SeedContent();
        builder.UseSetting("Content:Root", _contentRoot.FullName);
        // AddRedline reads this while Program registers services, so it must be a setting.
        builder.UseSetting("Redline:OpenTdb:Enabled", "false");
        // A private in-memory database, so no arcbox.db appears in the server's content root.
        builder.UseSetting("Storage:ConnectionString", $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        builder.ConfigureTestServices(services =>
        {
            services.AddArcGame<SignalEchoGame>();
            services.AddArcGame<CampaignTestGame>();
            services.AddCampaignRules<CampaignTestRules>();
        });
    }

    /// <summary>An <see cref="HttpClient"/> on this server (base address = the server) through the in-memory handler, or <paramref name="wrap"/> around it.</summary>
    public HttpClient CreateContentClient(Func<HttpMessageHandler, HttpMessageHandler>? wrap = null)
    {
        var handler = Server.CreateHandler();
        return new HttpClient(wrap is null ? handler : wrap(handler)) { BaseAddress = Server.BaseAddress };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            _contentRoot.Delete(true);
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless.
        }
    }

    private void SeedContent()
    {
        foreach (var (path, bytes) in PackFiles)
        {
            var full = Path.Combine(_contentRoot.FullName, SignalEchoGame.Id, PackId, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
    }

    /// <summary>A host client that talks to this server over LongPolling through the in-memory handler.</summary>
    public HostClient CreateHostClient() => new(Server.BaseAddress, () => Server.CreateHandler());

    /// <summary>A bare phone connection, as the web app would make.</summary>
    public async Task<HubConnection> ConnectPhoneAsync()
    {
        var server = Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hub"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions = new JsonSerializerOptions(ArcJson.Options))
            .Build();
        await connection.StartAsync();
        return connection;
    }

    public async Task<(HubConnection Phone, JoinResult Join)> JoinAsync(string code, string name)
    {
        var phone = await ConnectPhoneAsync();
        var join = await phone.InvokeAsync<JoinResult>("Join", code, name);
        return (phone, join);
    }
}
