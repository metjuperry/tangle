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
    public async Task<bool> SaveGitHubUserAsync(long githubId, string login, string? name, string? avatar,
        string access, string refresh, DateTimeOffset expiry, CancellationToken ct)
    {
        var id = githubId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var db = await OpenAsync(ct);
        using var tx = db.BeginTransaction(deferred: false);
        var existing = await db.QuerySingleOrDefaultAsync<AccountInfo>(new CommandDefinition(
            "SELECT id AS Id,login AS Login,name AS Name,avatar_url AS AvatarUrl,disabled AS Disabled FROM user WHERE id=@id",
            new { id }, tx, cancellationToken: ct));
        if (existing?.Disabled == true) throw new UnauthorizedAccessException("User is disabled.");
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO user(id,github_id,login,name,avatar_url,created_at,last_seen_at)
            VALUES(@id,@githubId,@login,@name,@avatar,@now,@now)
            ON CONFLICT(id) DO UPDATE SET login=excluded.login,name=excluded.name,avatar_url=excluded.avatar_url,last_seen_at=excluded.last_seen_at;
            INSERT INTO user_token(user_id,access_token,refresh_token,expires_at,status)
            VALUES(@id,@access,@refresh,@expiry,'active')
            ON CONFLICT(user_id) DO UPDATE SET access_token=excluded.access_token,refresh_token=excluded.refresh_token,
              expires_at=excluded.expires_at,status='active';
            INSERT INTO user_ai(user_id) VALUES(@id) ON CONFLICT(user_id) DO NOTHING;
            """, new { id, githubId, login, name, avatar, access, refresh, expiry = expiry.ToString("O"),
                now = DateTimeOffset.UtcNow.ToString("O") }, tx, cancellationToken: ct));
        tx.Commit();
        return existing is null;
    }
    public async Task<AccountInfo?> AccountAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<AccountInfo>(new CommandDefinition("""
            SELECT u.id AS Id,u.login AS Login,u.name AS Name,u.avatar_url AS AvatarUrl,u.disabled AS Disabled,
              t.status AS TokenStatus,t.expires_at AS ExpiresAt FROM user u LEFT JOIN user_token t ON t.user_id=u.id WHERE u.id=@id
            """, new { id }, cancellationToken: ct));
    }
    public async Task<string?> EncryptedAccessAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT access_token FROM user_token WHERE user_id=@id AND status='active'",
            new { id }, cancellationToken: ct));
    }
    public async Task<UserToken?> TokenAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<UserToken>(new CommandDefinition("""
            SELECT access_token AS AccessToken,refresh_token AS RefreshToken,expires_at AS ExpiresAt,status AS Status
            FROM user_token WHERE user_id=@id
            """, new { id }, cancellationToken: ct));
    }
    public async Task<bool> RefreshTokenAsync(string id, string previous, string access, string refresh, DateTimeOffset expiry, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("""
            UPDATE user_token SET access_token=@access,refresh_token=@refresh,expires_at=@expiry,status='active'
            WHERE user_id=@id AND access_token=@previous AND status='active'
            """, new { id, previous, access, refresh, expiry = expiry.ToString("O") }, cancellationToken: ct)) == 1;
    }
    public async Task InvalidateTokenAsync(string id, string previous, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE user_token SET status='invalid' WHERE user_id=@id AND access_token=@previous",
            new { id, previous }, cancellationToken: ct));
    }
    public sealed record UserToken(string AccessToken, string? RefreshToken, string? ExpiresAt, string Status)
    {
        public UserToken() : this("", null, null, "") { }
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
    public sealed record AccountInfo(string Id, string Login, string? Name, string? AvatarUrl, bool Disabled)
    {
        public AccountInfo() : this("", "", null, null, false) { }
        public string? TokenStatus { get; init; }
        public string? ExpiresAt { get; init; }
    }
    private sealed record LegacyKey(string Id, string Name, string Hash, string CreatedAt, string? RevokedAt);
}
