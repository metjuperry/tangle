using Dapper;
using Microsoft.Data.Sqlite;

namespace Tangle.Data;

public sealed class ControlDatabase(string path)
{
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString());
        try { await db.OpenAsync(ct); return db; }
        catch { await db.DisposeAsync(); throw; }
    }
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS control_migration(name TEXT PRIMARY KEY,applied_at TEXT NOT NULL);
            """, cancellationToken: ct));
        using var tx = db.BeginTransaction(deferred: false);
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM control_migration WHERE name='0001'",
            transaction: tx, cancellationToken: ct)) == 0)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                CREATE TABLE user(id TEXT PRIMARY KEY,github_id INTEGER UNIQUE,login TEXT NOT NULL,name TEXT,
                  avatar_url TEXT,created_at TEXT NOT NULL,last_seen_at TEXT NOT NULL,disabled INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE user_token(user_id TEXT PRIMARY KEY REFERENCES user(id),access_token TEXT NOT NULL,
                  refresh_token TEXT,expires_at TEXT,status TEXT NOT NULL);
                CREATE TABLE personal_key(id TEXT PRIMARY KEY,user_id TEXT NOT NULL REFERENCES user(id),name TEXT NOT NULL,
                  key_hash TEXT NOT NULL UNIQUE,created_at TEXT NOT NULL,revoked_at TEXT);
                CREATE TABLE user_ai(user_id TEXT PRIMARY KEY REFERENCES user(id),mode TEXT NOT NULL DEFAULT 'off',
                  anthropic_key TEXT,monthly_budget TEXT NOT NULL DEFAULT '1',company_granted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE allowlist(kind TEXT NOT NULL,value TEXT NOT NULL,PRIMARY KEY(kind,value));
                INSERT INTO control_migration VALUES('0001',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """, transaction: tx, cancellationToken: ct));
        }
        tx.Commit();
    }
    public async Task EnsureLocalAsync(CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO user(id,login,created_at,last_seen_at) VALUES('local','local',@now,@now) ON CONFLICT(id) DO NOTHING;
            INSERT INTO user_ai(user_id) VALUES('local') ON CONFLICT(user_id) DO NOTHING;
            """, new { now = DateTimeOffset.UtcNow.ToString("O") }, cancellationToken: ct));
    }
    public async Task<bool> ActiveAsync(string id, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM user WHERE id=@id AND disabled=0",
            new { id }, cancellationToken: ct)) == 1;
    }
    public async Task<string?> FindLoginAsync(string login, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT id FROM user WHERE lower(login)=lower(@login) AND disabled=0 AND id<>'local'",
            new { login }, cancellationToken: ct));
    }
    public async Task ImportKeysAsync(Database source, string userId, CancellationToken ct = default)
    {
        await using var old = await source.OpenAsync(ct);
        var keys = await old.QueryAsync<LegacyKey>(new CommandDefinition(
            "SELECT id AS Id,name AS Name,key_hash AS Hash,created_at AS CreatedAt,revoked_at AS RevokedAt FROM personal_key", cancellationToken: ct));
        await using var db = await OpenAsync(ct);
        using var tx = db.BeginTransaction(deferred: false);
        foreach (var key in keys)
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO personal_key(id,user_id,name,key_hash,created_at,revoked_at)
                VALUES(@Id,@userId,@Name,@Hash,@CreatedAt,@RevokedAt) ON CONFLICT(id) DO NOTHING
                """, new { key.Id, userId, key.Name, key.Hash, key.CreatedAt, key.RevokedAt }, tx, cancellationToken: ct));
        tx.Commit();
    }
    private sealed record LegacyKey(string Id, string Name, string Hash, string CreatedAt, string? RevokedAt);
}
