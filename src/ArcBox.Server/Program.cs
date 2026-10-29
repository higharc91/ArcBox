using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Server;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Redline;
using ArcBox.Server.Hubs;
using ArcBox.Server.Rooms;
using ArcBox.Server.Speech;
using ArcBox.Server.Storage;
using Counter.Logic;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RoomOptions>(builder.Configuration.GetSection("Rooms"));
builder.Services.AddKeyedSingleton<TimeProvider>(RoomManager.ClockKey, TimeProvider.System);
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<IClientSender, HubClientSender>();
builder.Services.AddSingleton<IGameRegistry, GameRegistry>();
builder.Services.AddArcGame<CounterGame>();
builder.Services.AddStorage(builder.Configuration);
builder.Services.AddSpeech(builder.Configuration);
builder.Services.AddRedline(builder.Configuration);
builder.Services.AddHostedService<RoomSweeper>();
// Rooms live in memory and die with the process, so don't wait the default 30 s for stateful-reconnect clients.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

builder.Services
    .AddSignalR(o =>
    {
        o.MaximumReceiveMessageSize = 256 * 1024;
        o.AddFilter<RateLimitHubFilter>();
    })
    .AddJsonProtocol(o => o.PayloadSerializerOptions = new JsonSerializerOptions(ArcJson.Options));

var app = builder.Build();

app.UsePhoneApp();
app.MapHub<ArcHub>("/hub", o => o.AllowStatefulReconnects = true);
app.MapSpeech();
app.MapHealthEndpoint();

app.Run();

public partial class Program;
