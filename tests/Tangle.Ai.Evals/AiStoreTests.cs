using Dapper;
using Microsoft.Data.Sqlite;
using Tangle.Core;
using Tangle.Data;
using Tangle.Ai;
using Anthropic;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

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
    public async Task WorkerMakesZeroCallsOnUnchangedSyncAndOnlyRejudgesChangedPrAndPair()
    {
        await Seed();
        var handler = new CountingHandler();
        var worker = new AiWorker(store, new HaikuDecider(new AnthropicClient
        { ApiKey = "fake", HttpClient = new HttpClient(handler), MaxRetries = 0 }), new Events(), NullLogger<AiWorker>.Instance, true);
        await worker.RunAsync(default);
        Assert.Equal(3, handler.Calls); // Two verdicts, one negative pair assessment.
        await sync.RecomputeAsync();
        await worker.RunAsync(default);
        Assert.Equal(3, handler.Calls);
        var restartedStore = new AiStore(database, sync, new(database, TimeProvider.System), TimeProvider.System);
        var restartedWorker = new AiWorker(restartedStore, new HaikuDecider(new AnthropicClient
        { ApiKey = "fake", HttpClient = new HttpClient(handler), MaxRetries = 0 }), new Events(), NullLogger<AiWorker>.Instance, true);
        await restartedWorker.RunAsync(default);
        Assert.Equal(3, handler.Calls);
        await using var db = await database.OpenAsync();
        await db.ExecuteAsync("UPDATE pr SET title='Changed metadata',updated_at='2099-10-10T00:00:00Z' WHERE id='allowed/repo#1'");
        await restartedWorker.RunAsync(default);
        Assert.Equal(5, handler.Calls); // Changed PR verdict and its pair, not the unchanged PR.
    }
    [Fact]
    public async Task LookStateIsPerPairAndFailureDoesNotAutomaticallyRepeatPaidWork()
    {
        await Seed();
        var snapshot = await store.SnapshotAsync(default);
        var pair = new PairContext(snapshot.Prs[0].Ref, snapshot.Prs[1].Ref, snapshot.Prs[0], snapshot.Prs[1], null);
        var work = AiCacheKeys.Pair(pair);
        var reservation = await store.ReserveAsync("failed-pair", "haiku-5-5", .02m, default, "pairs", ["pair"], [work]);
        await store.FailAsync(reservation!, "malformed provider answer", new(100, 20, .001m, 5), default);
        Assert.True(await store.LookedAsync(work, snapshot.Prs, default));
        Assert.False(await store.LookedAsync(AiCacheKeys.Pair(pair with { B = pair.B! with { Title = "Changed" } }), snapshot.Prs, default));
        var unsent = AiCacheKeys.Verdict(snapshot.Prs[0]);
        reservation = await store.ReserveAsync("unsent", "haiku-5-5", .02m, default, "verdict", [snapshot.Prs[0].Ref], [unsent]);
        await store.FailAsync(reservation!, "cancelled before dispatch", new(0, 0, 0, 0), default);
        Assert.False(await store.LookedAsync(unsent, snapshot.Prs, default));
    }
    [Fact]
    public async Task SuccessfulLegacyVerdictIsAdoptedWithoutPaidRerun()
    {
        await Seed();
        var pr = (await store.SnapshotAsync(default)).Prs[0];
        var reservation = await store.ReserveAsync("legacy-prompt", "haiku-5-5", .02m, default, "verdict", [pr.Ref]);
        await store.FinishAsync(reservation!, new(100, 20, .001m, 5), pr, new(NextAction.Fix, .9, 3, "reason", "intent"), null, default);
        Assert.True(await store.LookedAsync(AiCacheKeys.Verdict(pr), [pr], default));
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
    private sealed class Events : ILiveEvents { public void Publish(string type, object data) { } }
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var pairCall = body.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema")
                .GetProperty("properties").TryGetProperty("pairs", out _);
            var text = pairCall ? """{"pairs":[{"pairIndex":0,"related":0.2,"reason":"unrelated"}]}""" :
                """{"action":"fix","confidence":0.9,"priority":4,"reason":"Review needs attention","intent":"Add metadata"}""";
            var response = JsonSerializer.Serialize(new { id = "msg_fake", type = "message", role = "assistant",
                model = HaikuDecider.ModelId, content = new[] { new { type = "text", text } },
                stop_reason = "end_turn", stop_sequence = (string?)null, usage = new { input_tokens = 100, output_tokens = 20 } });
            return new(HttpStatusCode.OK) { Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
