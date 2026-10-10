using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace Tangle.Data;

public sealed class PersonalKeys(Database database, TimeProvider clock)
{
    public async Task<CreatedKey> CreateAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("Key name must be 1–100 characters.");
        var id = Guid.NewGuid().ToString();
        var key = "tangle_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO personal_key VALUES(@id,@name,@hash,@at,NULL)",
            new { id, name = name.Trim(), hash = Hash(key), at = clock.GetUtcNow().ToString("O") }, cancellationToken: ct));
        return new(id, name.Trim(), key);
    }
    public async Task<bool> ValidAsync(string key, CancellationToken ct)
    {
        if (key.Length is < 40 or > 100) return false;
        await using var db = await database.OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM personal_key WHERE key_hash=@hash AND revoked_at IS NULL",
            new { hash = Hash(key) }, cancellationToken: ct)) == 1;
    }
    public async Task<IReadOnlyList<KeyInfo>> ListAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return (await db.QueryAsync<KeyInfo>(new CommandDefinition(
            "SELECT id AS Id,name AS Name,created_at AS CreatedAt,revoked_at AS RevokedAt FROM personal_key ORDER BY created_at",
            cancellationToken: ct))).ToArray();
    }
    public async Task RevokeAsync(string id, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        if (await db.ExecuteAsync(new CommandDefinition("UPDATE personal_key SET revoked_at=@at WHERE id=@id AND revoked_at IS NULL",
            new { id, at = clock.GetUtcNow().ToString("O") }, cancellationToken: ct)) != 1)
            throw new ArgumentException("Active key not found.");
    }
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
public sealed record CreatedKey(string Id, string Name, string Key);
public sealed record KeyInfo(string Id, string Name, string CreatedAt, string? RevokedAt);
