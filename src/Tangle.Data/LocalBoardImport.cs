using Dapper;
using Microsoft.Data.Sqlite;

namespace Tangle.Data;

public static class LocalBoardImport
{
    public static async Task<string> RunAsync(string sourcePath, string userDirectory, string id, string confirmLogin,
        ControlDatabase control, CancellationToken ct = default)
    {
        if (!long.TryParse(id, out var number) || number <= 0 || number.ToString() != id)
            throw new ArgumentException("Supply the numeric GitHub user id.");
        var account = await control.AccountAsync(id, ct) ?? throw new ArgumentException("Sign in once before importing.");
        if (account.Disabled || !string.Equals(account.Login, confirmLogin, StringComparison.Ordinal))
            throw new ArgumentException("Confirm the destination account's exact login.");
        if (!File.Exists(sourcePath)) throw new ArgumentException("Local database does not exist.");
        Directory.CreateDirectory(userDirectory);
        var destination = Path.Combine(userDirectory, id + ".db");
        if (Path.GetFullPath(sourcePath) == Path.GetFullPath(destination))
            throw new ArgumentException("Source and destination must be different files.");
        var backupDirectory = Path.Combine(Path.GetDirectoryName(userDirectory)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N");
        var backup = Path.Combine(backupDirectory, "local-" + suffix + ".db");
        var temp = Path.Combine(userDirectory, id + "." + suffix + ".import.db");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(sourcePath), Mode = SqliteOpenMode.ReadOnly }.ToString());
        await source.OpenAsync(ct);
        var viewers = (await source.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT viewer FROM pr WHERE viewer<>''", cancellationToken: ct))).ToArray();
        if (viewers.Any(viewer => !string.Equals(viewer, confirmLogin, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The source board belongs to a different GitHub account.");
        Snapshot(source, backup);
        Snapshot(source, temp);
        try
        {
            var copied = new Database(temp);
            await copied.MigrateAsync(ct);
            await using (var db = await copied.OpenAsync(ct))
            {
                await db.ExecuteAsync(new CommandDefinition("""
                    DELETE FROM personal_key;
                    UPDATE app_setting SET value='[]' WHERE key='apply_repositories';
                    UPDATE app_setting SET value='haiku-5-5' WHERE key IN ('ai_verdicts','ai_pairs','ai_explain');
                    """, cancellationToken: ct));
                await db.ExecuteAsync(new CommandDefinition("PRAGMA wal_checkpoint(TRUNCATE)", cancellationToken: ct));
            }
            if (File.Exists(destination))
            {
                using var existing = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = destination, Mode = SqliteOpenMode.ReadOnly }.ToString());
                await existing.OpenAsync(ct);
                Snapshot(existing, Path.Combine(backupDirectory, id + "-" + suffix + ".db"));
            }
            // The host must be stopped and hold no connections before replacing the destination.
            SqliteConnection.ClearAllPools();
            File.Move(temp, destination, overwrite: true);
            foreach (var file in new[] { destination + "-wal", destination + "-shm", temp + "-wal", temp + "-shm" })
                if (File.Exists(file)) File.Delete(file);
            await using (var db = await control.OpenAsync(ct))
                await db.ExecuteAsync(new CommandDefinition("UPDATE personal_key SET user_id=@id WHERE user_id='local'",
                    new { id }, cancellationToken: ct));
            return backup;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Snapshot(SqliteConnection source, string path)
    {
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        target.Open();
        source.BackupDatabase(target);
    }
}
