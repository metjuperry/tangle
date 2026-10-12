using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace Tangle.Data;

public sealed class PersonalKeys(ControlDatabase database, TimeProvider clock, IUserContext user)
{
    public async Task<CreatedKey> CreateAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("Key name must be 1–100 characters.");
        var id = Guid.NewGuid().ToString();
        var key = "tangle_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO personal_key VALUES(@id,@userId,@name,@hash,@at,NULL)",
            new { id, userId = user.UserId, name = name.Trim(), hash = Hash(key), at = clock.GetUtcNow().ToString("O") }, cancellationToken: ct));
        return new(id, name.Trim(), key);
    }
    public async Task<bool> ValidAsync(string key, CancellationToken ct)
    {
        if (key.Length is < 40 or > 100) return false;
        await using var db = await database.OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM personal_key WHERE key_hash=@hash AND user_id=@userId AND revoked_at IS NULL",
            new { hash = Hash(key), userId = user.UserId }, cancellationToken: ct)) == 1;
    }
    public async Task<IReadOnlyList<KeyInfo>> ListAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return (await db.QueryAsync<KeyInfo>(new CommandDefinition(
            "SELECT id AS Id,name AS Name,created_at AS CreatedAt,revoked_at AS RevokedAt FROM personal_key WHERE user_id=@userId ORDER BY created_at",
            new { userId = user.UserId }, cancellationToken: ct))).ToArray();
    }
    public async Task RevokeAsync(string id, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        if (await db.ExecuteAsync(new CommandDefinition("UPDATE personal_key SET revoked_at=@at WHERE id=@id AND user_id=@userId AND revoked_at IS NULL",
            new { id, userId = user.UserId, at = clock.GetUtcNow().ToString("O") }, cancellationToken: ct)) != 1)
            throw new ArgumentException("Active key not found.");
    }
    public static async Task<string?> ResolveAsync(ControlDatabase database, string key, CancellationToken ct)
    {
        if (key.Length is < 40 or > 100) return null;
        await using var db = await database.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT k.user_id FROM personal_key k JOIN user u ON u.id=k.user_id
            WHERE k.key_hash=@hash AND k.revoked_at IS NULL AND u.disabled=0
            """, new { hash = Hash(key) }, cancellationToken: ct));
    }
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
public sealed record CreatedKey(string Id, string Name, string Key);
public sealed record KeyInfo(string Id, string Name, string CreatedAt, string? RevokedAt);
