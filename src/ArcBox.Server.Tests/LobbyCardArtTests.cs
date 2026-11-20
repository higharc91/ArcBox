using System.Net;
using ArcBox.Sdk;
using ArcBox.Server.Content;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Meridian;
using ArcBox.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcBox.Server.Tests;

/// <summary>A game's title image on its TV lobby card (#405): resolved by the server to a file the content endpoint serves.</summary>
public sealed class LobbyCardArtTests
{
    private static GameManifest Manifest(string? art) => new("game", "Game", "A game.", 1, 4, CardArt: art);

    private static ContentPackRegistry Packs(params string[] files)
        => new([new ContentPack("game", "base", "h4sh", "/nowhere", [.. files.Select(f => new ContentFile(f, "00", 1))])]);

    [Fact]
    public void Art_the_base_pack_holds_becomes_its_immutable_url()
        => Assert.Equal("/content/game/base/h4sh/ui/logo.ab12.png",
                        GameRegistry.WithCardArtUrl(Manifest("ui/logo.ab12.png"), Packs("ui/logo.ab12.png")).CardArt);

    [Fact]
    public void Art_the_pack_doesnt_hold_or_no_pack_or_no_art_gives_a_text_card()
    {
        Assert.Null(GameRegistry.WithCardArtUrl(Manifest("ui/missing.png"), Packs("ui/logo.ab12.png")).CardArt);
        Assert.Null(GameRegistry.WithCardArtUrl(Manifest("ui/logo.ab12.png"), null).CardArt);
        Assert.Null(GameRegistry.WithCardArtUrl(Manifest(null), Packs("ui/logo.ab12.png")).CardArt);
        Assert.Null(GameRegistry.WithCardArtUrl(Manifest(""), Packs("ui/logo.ab12.png")).CardArt);
    }

    [Fact]
    public void Meridian_names_the_ui_kits_logo_and_nothing_when_the_kit_is_missing()
    {
        using var dir = new TempDir();
        Assert.Null(MeridianServiceCollectionExtensions.LogoPath(dir.Path));   // no kit
        Directory.CreateDirectory(Path.Combine(dir.Path, "ui"));
        File.WriteAllText(Path.Combine(dir.Path, "ui", "manifest.json"), "not json");
        Assert.Null(MeridianServiceCollectionExtensions.LogoPath(dir.Path));   // a broken manifest
        File.WriteAllText(Path.Combine(dir.Path, "ui", "manifest.json"), """{ "ui": { "logo": { "file": "logo.f00d.png" } } }""");
        Assert.Equal("ui/logo.f00d.png", MeridianServiceCollectionExtensions.LogoPath(dir.Path));
    }

    /// <summary>The shipped pack, end to end: the URL the TV is given serves the PNG; a game without art has none.</summary>
    [Fact]
    public async Task The_shipped_meridian_card_art_is_served_and_other_games_have_none()
    {
        await using var server = new ArcServerFactory(withTactics: false, withMeridian: true);
        var manifests = server.Services.GetRequiredService<IGameRegistry>().Manifests;
        var meridian = manifests.Single(m => m.Id == "meridian");
        Assert.StartsWith("/content/meridian/base/", meridian.CardArt);
        Assert.EndsWith(".png", meridian.CardArt);
        Assert.All(manifests.Where(m => m.Id != "meridian"), m => Assert.Null(m.CardArt));   // REDLINE and the rest as today

        using var response = await server.CreateClient().GetAsync(meridian.CardArt, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("arcbox-cardart-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
