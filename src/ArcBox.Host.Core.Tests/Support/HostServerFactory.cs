using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using ArcBox.Server.Games;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArcBox.Host.Core.Tests.Support;

/// <summary>The real server in memory (minimal copy of ArcBox.Server.Tests' factory).</summary>
public sealed class HostServerFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // AddRedline reads this while Program registers services, so it must be a setting.
        builder.UseSetting("Redline:OpenTdb:Enabled", "false");
        // A private in-memory database, so no arcbox.db appears in the server's content root.
        builder.UseSetting("Storage:ConnectionString", $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        builder.ConfigureTestServices(services => services.AddArcGame<SignalEchoGame>());
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
