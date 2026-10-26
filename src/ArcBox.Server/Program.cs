using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Server;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Redline;
using ArcBox.Server.Hubs;
using ArcBox.Server.Rooms;
using Counter.Logic;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RoomOptions>(builder.Configuration.GetSection("Rooms"));
builder.Services.AddKeyedSingleton<TimeProvider>(RoomManager.ClockKey, TimeProvider.System);
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<IClientSender, HubClientSender>();
builder.Services.AddSingleton<IGameRegistry, GameRegistry>();
builder.Services.AddArcGame<CounterGame>();
builder.Services.AddRedline(builder.Configuration);
builder.Services.AddHostedService<RoomSweeper>();

// Singleton so the per-connection buckets outlive each invocation.
builder.Services.AddSingleton<RateLimitHubFilter>();
builder.Services
    .AddSignalR(o =>
    {
        o.MaximumReceiveMessageSize = 256 * 1024;
        o.AddFilter<RateLimitHubFilter>();
    })
    .AddJsonProtocol(o => o.PayloadSerializerOptions = new JsonSerializerOptions(ArcJson.Options));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<ArcHub>("/hub", o => o.AllowStatefulReconnects = true);
app.MapGet("/healthz", () => "ok");

app.Run();

public partial class Program;
