using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public sealed class UserResources
{
    private readonly ConcurrentDictionary<string, EventHub> events = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> sync = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> replay = new(StringComparer.Ordinal);
    public EventHub Events(string id) => events.GetOrAdd(id, _ => new());
    public SemaphoreSlim Sync(string id) => sync.GetOrAdd(id, _ => new(1, 1));
    public SemaphoreSlim Replay(string id) => replay.GetOrAdd(id, _ => new(1, 1));
}

public static class UserServices
{
    public static void AddUserData(this IServiceCollection services)
    {
        services.AddScoped<UserContext>();
        services.AddScoped<IUserContext>(s => s.GetRequiredService<UserContext>());
        services.AddScoped<Database>(s => s.GetRequiredService<IUserContext>().Database);
        services.AddSingleton<ControlDatabase>(s =>
        {
            var settings = s.GetRequiredService<RuntimeSettings>();
            return new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings.DatabasePath))!, "data", "control.db"));
        });
        services.AddSingleton<UserDatabases>(s =>
        {
            var settings = s.GetRequiredService<RuntimeSettings>();
            return new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings.DatabasePath))!, "data", "users"),
                settings.DatabasePath, s.GetRequiredService<ControlDatabase>(), s.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<UserResources>();
        services.AddScoped<EventHub>(s => s.GetRequiredService<UserResources>().Events(s.GetRequiredService<IUserContext>().UserId));
        services.AddScoped<ILiveEvents>(s => s.GetRequiredService<EventHub>());
    }
}
