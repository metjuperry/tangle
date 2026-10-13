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
    private readonly object lifecycle = new();
    private readonly Dictionary<string, int> active = new(StringComparer.Ordinal);
    private readonly HashSet<string> deleting = new(StringComparer.Ordinal);
    public IDisposable Lease(string id)
    {
        lock (lifecycle)
        {
            if (deleting.Contains(id)) throw new UnauthorizedAccessException("Account is being deleted.");
            active[id] = active.GetValueOrDefault(id) + 1;
            return new LeaseHandle(() => { lock (lifecycle) active[id]--; });
        }
    }
    public async Task DeleteUserAsync(string id, CancellationToken ct)
    {
        Validate(id);
        if (id == "local") throw new ArgumentException("Local data cannot be deleted.");
        lock (lifecycle)
        {
            if (active.GetValueOrDefault(id) > 1) throw new InvalidOperationException("Another request or AI job is active. Wait before deleting.");
            if (!deleting.Add(id)) throw new InvalidOperationException("Deletion is already running.");
        }
        try
        {
            await control.DisableAsync(id, true, ct);
            opened.TryRemove(id, out _);
            var path = Path.Combine(directory, id + ".db");
            SqlitePoolClear();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
            var backups = Path.Combine(Path.GetDirectoryName(directory)!, "backups");
            if (Directory.Exists(backups))
                foreach (var file in Directory.EnumerateFiles(backups, id + "-*.db"))
                    File.Delete(file);
            await control.DeleteAsync(id, ct);
        }
        finally { lock (lifecycle) deleting.Remove(id); }
    }
    private static void SqlitePoolClear() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    private sealed class LeaseHandle(Action release) : IDisposable
    {
        private Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
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
            if (!await control.ActiveAsync(id, ct)) throw new UnauthorizedAccessException("User is disabled or missing.");
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
