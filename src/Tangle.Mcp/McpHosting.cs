using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Tangle.Mcp;

public static class McpHosting
{
    public static void AddTangleMcp(this IServiceCollection services) =>
        services.AddMcpServer().WithHttpTransport(options => options.Stateless = true).WithTools<TangleTools>();
    public static void MapTangleMcp(this WebApplication app) => app.MapMcp("/mcp").RequireAuthorization();
}
