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
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = provider });
    }
}
