using Dapper;
using Microsoft.Data.Sqlite;
using Tangle.Data;

namespace Tangle.Core.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task MigrationsCreateFullSchemaAndNeverRepeat()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tangle-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = new Database(Path.Combine(directory, "test.db"));
            Assert.Equal(4, await database.MigrateAsync());
            Assert.Equal(0, await database.MigrateAsync());
            await using var connection = await database.OpenAsync();
            var tables = (await connection.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")).ToArray();
            Assert.Equal(new[] { "action", "ai_cache", "ai_call", "app_setting", "fact", "link", "migration", "note", "organization",
                "position", "pr", "pr_event", "pr_file", "pr_review_request", "repo", "review_thread", "session",
                "sticky", "sync_state", "verdict" }, tables);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
