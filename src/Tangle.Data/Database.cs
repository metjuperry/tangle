using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Tangle.Data;

public sealed class Database(string path)
{
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<int> MigrateAsync(CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS migration (name TEXT PRIMARY KEY, applied_at TEXT NOT NULL);",
            cancellationToken: ct));
        using var transaction = connection.BeginTransaction(deferred: false);
        var applied = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM migration", transaction: transaction, cancellationToken: ct))).ToHashSet();
        var assembly = typeof(Database).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".migrations.", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        var count = 0;
        foreach (var name in names)
        {
            if (applied.Contains(name)) continue;
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO migration(name, applied_at) VALUES (@name, @at)",
                new { name, at = DateTimeOffset.UtcNow.ToString("O") }, transaction, cancellationToken: ct));
            count++;
        }
        transaction.Commit();
        return count;
    }
}
