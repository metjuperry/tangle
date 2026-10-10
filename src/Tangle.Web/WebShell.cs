using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;

namespace Tangle.Web;

public static class WebShell
{
    public static void MapWebShell(this WebApplication app)
    {
        var provider = new ManifestEmbeddedFileProvider(typeof(WebShell).Assembly, "wwwroot");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
        app.Map("/api/{**path}", () => Microsoft.AspNetCore.Http.Results.NotFound());
        app.Map("/debug/{**path}", () => Microsoft.AspNetCore.Http.Results.NotFound());
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = provider });
    }
}
