using Microsoft.AspNetCore.StaticFiles;

namespace ArcBox.Server.Content;

/// <summary>Serves content packs to the TV and the phones (spec §3.6).</summary>
public static class ContentEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>
    /// <c>GET /content/{gameId}/index.json</c> lists a game's packs; <c>GET /content/{gameId}/{packId}/{hash}/{**path}</c>
    /// serves one file of a pack for good. A request is only answered if the game, pack, hash and file are all ones the
    /// registry scanned and the file's full path is inside the pack's directory; everything else is a plain 404.
    /// </summary>
    public static WebApplication MapContent(this WebApplication app)
    {
        app.MapGet("/content/{gameId}/index.json", (string gameId, ContentPackRegistry registry) =>
            Results.Json(registry.For(gameId).Select(p => new { packId = p.PackId, hash = p.Hash, files = p.Files })));

        app.MapGet("/content/{gameId}/{packId}/{hash}/{**path}", (
            string gameId, string packId, string hash, string? path, ContentPackRegistry registry, HttpResponse response) =>
        {
            if (string.IsNullOrEmpty(path) || registry.Find(gameId, packId, hash) is not { } pack) return Results.NotFound();

            // Only files the scan found: this refuses directories, "..", absolute paths and anything not hashed.
            if (!pack.Files.Any(f => f.Path == path)) return Results.NotFound();

            var root = Path.GetFullPath(pack.Directory) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return Results.NotFound();

            response.Headers.CacheControl = "public, max-age=31536000, immutable";
            var type = ContentTypes.TryGetContentType(full, out var found) ? found : "application/octet-stream";
            return Results.File(full, type, enableRangeProcessing: true);
        });

        return app;
    }
}
