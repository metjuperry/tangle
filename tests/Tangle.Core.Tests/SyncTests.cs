using Dapper;
using Microsoft.Data.Sqlite;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Core.Tests;

public sealed class SyncTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"tangle-sync-{Guid.NewGuid()}");
    private readonly Database database;
    private readonly SyncStore store;
    public SyncTests()
    {
        Directory.CreateDirectory(directory);
        database = new(Path.Combine(directory, "test.db"));
        store = new(database);
    }
    private async Task Initialize()
    {
        await database.MigrateAsync();
        await store.SaveSettingsAsync(new([new("example")], []));
    }
    public static PullRequest Pr(int number = 1, string state = "OPEN", string hash = "first") =>
        new($"example/repo#{number}", "example/repo", number, "Recorded title", "Body", "viewer", true,
            state, false, "feature", "main", "2026-10-01T00:00:00Z", "2026-10-09T00:00:00Z",
            state == "OPEN" ? null : "2026-10-09T00:00:00Z", 10, 2, 1, "MERGEABLE", null, "SUCCESS",
            ["src/file.cs"], [new("2026-10-09T00:00:00Z", "viewer", "comment", "{\"body\":\"hello\"}")],
            [new($"thread-{number}", "src/file.cs", false, "reviewer", "2026-10-09T00:00:00Z", "Please fix")],
            [new("reviewer", "User")], hash, "viewer");
    private static PrPage Page(IReadOnlyList<PullRequest> prs, bool more = false, string? cursor = null) =>
        new(prs, more, cursor, 1, 4999, "2026-10-10T00:00:00Z");

    [Fact]
    public async Task PagesThenUsesIncrementalWindowAndRemovesClosedFromBoard()
    {
        await Initialize();
        var fake = new FakeReader([Page([Pr()], true, "next"), Page([Pr(2)]), Page([]), Page([Pr(state: "MERGED", hash: "merged")])]);
        var sync = new SyncService(fake, store, new FixedClock());
        Assert.Equal(2, (await sync.RunAsync(default)).Changed);
        Assert.Equal("next", fake.Requests[1].After);
        Assert.Equal(2, (await store.GetBoardAsync()).Count);
        Assert.Equal(1, (await sync.RunAsync(default)).Changed);
        Assert.Contains("updated:>=2026-10-09T11:59:59Z", fake.Requests[2].Query);
        Assert.Contains("is:closed", fake.Requests[3].Query);
        Assert.Single(await store.GetBoardAsync());
        Assert.Equal(2, (await store.GetBoardAsync(includeClosed: true)).Count);
    }

    [Fact]
    public async Task RepeatedSyncChangesNothingAndPreservesAllAuthoredRows()
    {
        await Initialize();
        var fake = new FakeReader([Page([Pr()]), Page([Pr()]), Page([])]);
        var sync = new SyncService(fake, store, new FixedClock());
        Assert.Equal(1, (await sync.RunAsync(default)).Changed);
        await using var db = await database.OpenAsync();
        await db.ExecuteAsync("""
            INSERT INTO sticky VALUES('sticky','Release','blue',NULL);
            INSERT INTO link VALUES('cut','pr:example/repo#1','sticky:sticky','rule','explicit','cut',NULL,NULL,'keep cut','now','now');
            INSERT INTO link VALUES('tie','pr:example/repo#1','pr:example/repo#2','me',NULL,'tied',NULL,NULL,'release','now','now');
            INSERT INTO note VALUES('note','pr:example/repo#1','keep this','now');
            INSERT INTO position VALUES('pr:example/repo#1',42,99,1);
            INSERT INTO action(id,pr_id,kind,payload,status,created_at,applied_at,result)
              VALUES('action','example/repo#1','comment','hello','queued','now',NULL,NULL);
            """);
        Assert.Equal(0, (await sync.RunAsync(default)).Changed);
        Assert.Equal(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM link"));
        Assert.Equal("cut", await db.ExecuteScalarAsync<string>("SELECT status FROM link WHERE id='cut'"));
        Assert.Equal("release", await db.ExecuteScalarAsync<string>("SELECT label FROM link WHERE id='tie'"));
        Assert.Equal("keep this", await db.ExecuteScalarAsync<string>("SELECT text FROM note"));
        Assert.Equal(42, await db.ExecuteScalarAsync<int>("SELECT x FROM position"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sticky"));
        Assert.Equal("queued", await db.ExecuteScalarAsync<string>("SELECT status FROM action"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pr_event"));
        Assert.False((await store.GetSettingsAsync()).Repos.Single().AiAllowed);
        Assert.Equal(1, await store.SaveScopeAsync("scope", [Pr(hash: "changed")], "next", default));
        Assert.Equal("cut", await db.ExecuteScalarAsync<string>("SELECT status FROM link WHERE id='cut'"));
        Assert.Equal("release", await db.ExecuteScalarAsync<string>("SELECT label FROM link WHERE id='tie'"));
        Assert.Equal("keep this", await db.ExecuteScalarAsync<string>("SELECT text FROM note"));
        Assert.Equal(42, await db.ExecuteScalarAsync<int>("SELECT x FROM position"));
    }

    [Fact]
    public async Task FailedLaterPageDoesNotAdvanceCursorOrWritePartialCache()
    {
        await Initialize();
        var fake = new FakeReader([Page([Pr()], true, "next")]);
        var sync = new SyncService(fake, store, new FixedClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.RunAsync(default));
        Assert.Null(await store.GetCursorAsync("involves:viewer org:example", default));
        Assert.Empty(await store.GetBoardAsync());
    }

    [Fact]
    public async Task ChangedCacheReplacesChildRowsWithoutTouchingAuthoredData()
    {
        await Initialize();
        await store.SaveScopeAsync("scope", [Pr()], "cursor", default);
        var changed = Pr(hash: "changed") with { Files = ["new.cs"], Threads = [], Events = [], ReviewRequests = [] };
        Assert.Equal(1, await store.SaveScopeAsync("scope", [changed], "next", default));
        await using var db = await database.OpenAsync();
        Assert.Equal("new.cs", await db.ExecuteScalarAsync<string>("SELECT path FROM pr_file"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM review_thread"));
    }

    [Fact]
    public async Task SettingsPersistAndPersonalAndIndividualRepoScopesAreUsed()
    {
        await Initialize();
        await store.SaveSettingsAsync(new([new("person", "user")], [new("other/repo")]));
        var fake = new FakeReader([Page([]), Page([])]);
        await new SyncService(fake, store, new FixedClock()).RunAsync(default);
        Assert.Contains("user:person", fake.Requests[0].Query);
        Assert.Contains("repo:other/repo", fake.Requests[1].Query);
        Assert.DoesNotContain("org:example", string.Join("", fake.Requests.Select(r => r.Query)));
    }

    [Fact]
    public async Task DisabledOwnerDoesNotFallBackToItsDiscoveredRepos()
    {
        await Initialize();
        await store.SaveScopeAsync("scope", [Pr()], "cursor", default);
        await store.SaveSettingsAsync(new([new("example", Watched: false)], [new("example/repo")]));
        Assert.Empty(await store.GetBoardAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SyncService(new FakeReader([]), store, new FixedClock()).RunAsync(default));
    }

    [Fact]
    public async Task ScopeChangesResetCacheCursorsSoReenabledReposGetFullSearch()
    {
        await Initialize();
        await store.SaveScopeAsync("scope", [Pr()], "cursor", default);
        var current = await store.GetSettingsAsync();
        await store.SaveSettingsAsync(current);
        Assert.Equal("cursor", await store.GetCursorAsync("scope", default));
        await store.SaveSettingsAsync(current with { Repos = [new("example/repo", false)] });
        Assert.Null(await store.GetCursorAsync("scope", default));
        Assert.Empty(await store.GetBoardAsync());
        await store.SaveSettingsAsync(current);
        Assert.Single(await store.GetBoardAsync());
    }

    [Fact]
    public async Task FactsAndRuleStringsAreAtomicAndCutsSurviveChangedSync()
    {
        await Initialize();
        var prs = new[] { Pr() with { Body = "#2" }, Pr(2) with { HeadRef = "other" } };
        await store.SaveScopeAsync("scope", prs, "cursor", default);
        var cards = await store.GetBoardAsync();
        Assert.All(cards, c => Assert.NotNull(c.Facts));
        var link = Assert.Single(await store.GetStringsAsync());
        Assert.Equal("explicit", link.Rule);
        await using var db = await database.OpenAsync();
        var created = await db.ExecuteScalarAsync<string>("SELECT created_at FROM link");
        await store.SaveScopeAsync("scope", prs, "next", default);
        Assert.Equal(created, await db.ExecuteScalarAsync<string>("SELECT created_at FROM link"));
        await db.ExecuteAsync("UPDATE link SET status='cut',decided_at='now',label='do not resurrect'");
        await store.SaveScopeAsync("scope", [prs[0] with { ContentHash = "changed" }, prs[1]], "next", default);
        Assert.Empty(await store.GetStringsAsync());
        Assert.Equal("cut", await db.ExecuteScalarAsync<string>("SELECT status FROM link"));
        Assert.Equal("do not resurrect", await db.ExecuteScalarAsync<string>("SELECT label FROM link"));
    }

    [Fact]
    public async Task InvalidFactInputRollsBackCacheAndCursor()
    {
        await Initialize();
        await Assert.ThrowsAsync<FormatException>(() => store.SaveScopeAsync("scope",
            [Pr() with { CreatedAt = "invalid" }], "cursor", default));
        Assert.Empty(await store.GetBoardAsync());
        Assert.Null(await store.GetCursorAsync("scope", default));
    }

    [Fact]
    public async Task BotSettingRecomputesExistingFactsWithoutNetwork()
    {
        await Initialize();
        var pr = Pr() with { Threads = [], Events = [
            new("2026-10-09T12:00:00Z", "custom-bot", "comment", """{"body":"automated"}""")
        ] };
        var fixedStore = new SyncStore(database, new FixedClock());
        await fixedStore.SaveScopeAsync("scope", [pr], "cursor", default);
        Assert.Equal(0, (await fixedStore.GetBoardAsync()).Single().Facts!.IdleDays);
        var settings = await fixedStore.GetSettingsAsync();
        await fixedStore.SaveSettingsAsync(settings with { BotLogins = [..settings.BotLogins, "custom-bot"] });
        Assert.Equal(8, (await fixedStore.GetBoardAsync()).Single().Facts!.IdleDays);
    }

    [Fact]
    public async Task OldM1ViewerIdentityIsRecoveredWithoutRewritingUserData()
    {
        await Initialize();
        var pr = Pr() with { IsMine = false, Author = "other", Viewer = "", Threads = [],
            ReviewRequests = [new("viewer", "User")] };
        await store.SaveScopeAsync("involves:viewer org:example", [pr], "cursor", default);
        Assert.Equal("your review is requested", (await store.GetBoardAsync()).Single().Facts!.TurnReason);
        await store.RecomputeAsync();
        Assert.Equal("me", (await store.GetBoardAsync()).Single().Facts!.Turn);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-09T12:00:00Z");
    }
    private sealed class FakeReader(IEnumerable<PrPage> pages) : IGitHubReader
    {
        private readonly Queue<PrPage> queue = new(pages);
        public List<(string Query, string? After)> Requests { get; } = [];
        public Task<string> GetViewerAsync(string owner, CancellationToken ct) => Task.FromResult("viewer");
        public Task<PrPage> SearchAsync(string owner, string query, string? after, string viewer, CancellationToken ct)
        {
            Requests.Add((query, after));
            if (queue.Count == 0) throw new InvalidOperationException("Simulated failed page.");
            return Task.FromResult(queue.Dequeue());
        }
    }
}
