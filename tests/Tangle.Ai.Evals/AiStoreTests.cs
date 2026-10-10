using Dapper;
using Microsoft.Data.Sqlite;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Ai.Evals;

public sealed class AiStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"tangle-ai-{Guid.NewGuid()}");
    private readonly Database database;
    private readonly SyncStore sync;
    private readonly AiStore store;
    public AiStoreTests()
    {
        database = new(Path.Combine(directory, "test.db")); sync = new(database);
        store = new(database, sync, new(database, TimeProvider.System), TimeProvider.System);
    }
    private async Task Seed()
    {
        await database.MigrateAsync();
        await sync.SaveSettingsAsync(new([], [new("allowed/repo", true, true), new("blocked/repo")]));
        PullRequest Pr(string repo, int n) => new($"{repo}#{n}", repo, n, "Metadata", new string('x', 5000), "viewer", true,
            "OPEN", false, $"feature-{n}", "main", "2026-10-01T00:00:00Z", "2026-10-09T00:00:00Z", null, 1, 0, 0,
            "MERGEABLE", null, null, [], [], [], [], $"{repo}-{n}", "viewer");
        await sync.SaveScopeAsync("involves:viewer repo:allowed/repo", [Pr("allowed/repo", 1), Pr("allowed/repo", 2), Pr("blocked/repo", 3)], "now", default);
        await using var db = await database.OpenAsync();
        await db.ExecuteAsync("""
            INSERT INTO link(id,a_ref,b_ref,source,status,label) VALUES('private-tie','pr:allowed/repo#1','pr:blocked/repo#3','me','tied','private context');
            """);
    }
    [Fact]
    public async Task OptInFiltersAllLinkedRepoDataAndBoundsBody()
    {
        await Seed();
        var snapshot = await store.SnapshotAsync(default);
        Assert.Equal(2, snapshot.Prs.Count);
        Assert.All(snapshot.Prs, p => Assert.Equal(4000, p.Body.Length));
        Assert.All(snapshot.Prs, p => Assert.Empty(p.Links));
        await sync.SaveSettingsAsync(new([], [new("allowed/repo"), new("blocked/repo")]));
        Assert.Empty((await store.SnapshotAsync(default)).Prs);
    }
    [Fact]
    public async Task BudgetReservationStopsBeforeSpendingAndCachePersists()
    {
        await Seed();
        await sync.SaveSettingsAsync((await sync.GetSettingsAsync()) with { AiBudget = .03m });
        var reservation = await store.ReserveAsync("hash", "haiku-5-5", .02m, default);
        Assert.NotNull(reservation);
        Assert.Null(await store.ReserveAsync("second", "haiku-5-5", .02m, default));
        var pr = (await store.SnapshotAsync(default)).Prs[0];
        await store.FinishAsync(reservation!, new(100, 20, .001m, 30), pr, new(NextAction.Fix, .9, 3, "reason", "intent"), null, default);
        await using var db = await database.OpenAsync();
        Assert.Equal("fix: reason", await db.ExecuteScalarAsync<string>("SELECT outcome FROM ai_call WHERE id=@id", new { id = reservation!.Id }));
        Assert.Equal(pr.Ref[3..], await db.ExecuteScalarAsync<string>("SELECT pr_id FROM ai_call WHERE id=@id", new { id = reservation.Id }));
        Assert.True(await store.CachedAsync("hash", default));
        Assert.Equal("fix", (await sync.GetBoardAsync()).First(c => c.Id == pr.Ref[3..]).Verdict!.Action);
    }
    [Fact]
    public async Task LowConfidenceCannotCreateStringAndCutsWin()
    {
        await Seed();
        var refs = new[] { "pr:allowed/repo#1", "pr:allowed/repo#2" };
        var reservation = await store.ReserveAsync("low", "haiku-5-5", .02m, default);
        await store.FinishAsync(reservation!, new(0, 0, 0, 1), null, null, [new(refs[0], refs[1], .75, "weak")], default);
        Assert.DoesNotContain(await sync.GetStringsAsync(), l => l.Source == "ai");
        await using var db = await database.OpenAsync();
        await db.ExecuteAsync("INSERT INTO link(id,a_ref,b_ref,source,status) VALUES('cut',@a,@b,'rule','cut')", new { a = refs[0], b = refs[1] });
        reservation = await store.ReserveAsync("high", "haiku-5-5", .02m, default);
        await store.FinishAsync(reservation!, new(0, 0, 0, 1), null, null, [new(refs[0], refs[1], .99, "strong")], default);
        Assert.Equal("cut", await db.ExecuteScalarAsync<string>("SELECT status FROM link WHERE id='cut'"));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
