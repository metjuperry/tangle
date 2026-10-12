using System.Collections.Concurrent;
using System.Globalization;

namespace Tangle.Data;

public interface IUserContext
{
    string UserId { get; }
    string Login { get; }
    Database Database { get; }
}
public sealed class UserContext(UserDatabases databases) : IUserContext
{
    private string? userId;
    private string? login;
    private Database? database;
    public string UserId => userId ?? "local";
    public string Login => login ?? "local";
    public Database Database => database ?? databases.GetOpened(UserId);
    public void Set(string id, string name)
    {
        if (userId is not null && userId != id) throw new InvalidOperationException("A request cannot switch users.");
        database = databases.GetOpened(id);
        userId = id; login = name;
    }
}
public sealed class UserDatabases(string directory, string localPath, ControlDatabase control, TimeProvider clock)
{
    private sealed record Entry(Database Database, DateTimeOffset LastUsed);
    private readonly ConcurrentDictionary<string, Entry> opened = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);
    private static void Validate(string id)
    {
        if (id != "local" && (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0
            || id != number.ToString(CultureInfo.InvariantCulture)))
            throw new ArgumentException("User id must be a positive numeric GitHub identity.");
    }
    public async Task<Database> OpenAsync(string id, CancellationToken ct = default)
    {
        Validate(id);
        if (!await control.ActiveAsync(id, ct)) throw new UnauthorizedAccessException("User is disabled or missing.");
        var gate = gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (opened.TryGetValue(id, out var entry))
            {
                opened[id] = entry with { LastUsed = now };
                return entry.Database;
            }
            foreach (var idle in opened.Where(pair => pair.Key != "local" && now - pair.Value.LastUsed > TimeSpan.FromMinutes(30)))
                opened.TryRemove(idle.Key, out _);
            var db = new Database(id == "local" ? localPath : Path.Combine(directory, id + ".db"));
            await db.MigrateAsync(ct);
            await new SyncStore(db, clock).RecomputeAsync(ct);
            await control.ImportKeysAsync(db, id, ct);
            opened[id] = new(db, now);
            return db;
        }
        finally { gate.Release(); }
    }
    public Database GetOpened(string id)
    {
        Validate(id);
        return opened.TryGetValue(id, out var entry) ? entry.Database : throw new InvalidOperationException("Resolve the user before opening stores.");
    }
}
