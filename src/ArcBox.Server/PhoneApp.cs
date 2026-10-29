using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace ArcBox.Server;

/// <summary>Serves the phone app (<c>web/play</c>) and the dev page as static files.</summary>
public static class PhoneApp
{
    /// <summary>
    /// <c>Web:Root</c>, when set to an existing directory (relative paths resolve against the content root), is served
    /// instead of <c>wwwroot</c>: Development points it at <c>web/play/dist</c> so a fresh <c>npm run build</c> is live
    /// without copying. Unknown paths fall through to a plain 404.
    /// </summary>
    public static WebApplication UsePhoneApp(this WebApplication app)
    {
        var provider = WebRoot(app) is { } root ? new PhysicalFileProvider(root) : app.Environment.WebRootFileProvider;
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, OnPrepareResponse = SetCacheHeaders });
        // The campaign portal (spec §3.4) is a route of the phone app, so a campaign code is answered with index.html.
        app.MapGet("/c/{code:length(4)}", (string code, HttpContext http) =>
            code.All(char.IsAsciiLetter) ? ServeIndex(http, provider) : Results.NotFound());
        return app;
    }

    private static IResult ServeIndex(HttpContext http, IFileProvider provider)
    {
        var index = provider.GetFileInfo("index.html");
        if (!index.Exists) return Results.NotFound();
        http.Response.Headers[HeaderNames.CacheControl] = "no-cache";
        return Results.Stream(index.CreateReadStream(), "text/html", lastModified: index.LastModified,
            entityTag: null, enableRangeProcessing: false);
    }

    private static string? WebRoot(WebApplication app)
    {
        var configured = app.Configuration["Web:Root"];
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var full = Path.GetFullPath(configured, app.Environment.ContentRootPath);
        if (Directory.Exists(full)) return full;
        app.Logger.LogWarning("Web:Root {WebRoot} does not exist; serving wwwroot. Run `npm run build` in web/play.", full);
        return null;
    }

    /// <summary>Vite fingerprints everything under /assets/, so those never change; the HTML must always be re-checked.</summary>
    private static void SetCacheHeaders(StaticFileResponseContext ctx)
    {
        var path = ctx.Context.Request.Path;
        ctx.Context.Response.Headers[HeaderNames.CacheControl] =
            path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache";
    }
}
