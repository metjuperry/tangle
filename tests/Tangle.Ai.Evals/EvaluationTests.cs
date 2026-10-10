using System.Net;
using System.Text.Json;
using Tangle.Ai;
using Tangle.Core;
using Tangle.Data;
using Anthropic;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

namespace Tangle.Ai.Evals;

public sealed class EvaluationTests
{
    [Fact]
    public async Task ReplayUsesOriginalAllowedInputCachesResultsAndDoesNotOverwriteBoard()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tangle-replay-{Guid.NewGuid()}");
        try
        {
            var database = new Database(Path.Combine(directory, "test.db"));
            await database.MigrateAsync();
            var sync = new SyncStore(database);
            var authoring = new AuthoringStore(database, TimeProvider.System);
            var ai = new AiStore(database, sync, authoring, TimeProvider.System);
            var eval = new EvaluationStore(database, TimeProvider.System);
            await sync.SaveSettingsAsync(new([], [new("example/repo", true, true)]));
            var context = DeciderTests.Context();
            await using (var db = await database.OpenAsync())
            {
                await db.ExecuteAsync("""
                    INSERT INTO pr(id,repo_id,number,viewer,created_at,updated_at,body,title,head_ref,base_ref,additions,deletions)
                    VALUES('example/repo#1','example/repo',1,'viewer','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z','','Fixture','feature','main',1,0);
                    INSERT INTO verdict(pr_id,decider,action,confidence,input_hash,created_at,input_json)
                    VALUES('example/repo#1','haiku-5-5','fix',0.8,'before','2026-01-01T00:00:00Z',@input);
                    INSERT INTO decision(id,pr_id,choice,created_at) VALUES('label','example/repo#1','fix','2026-01-02T00:00:00Z');
                    """, new { input = JsonSerializer.Serialize(context, HaikuDecider.Json) });
            }
            var handler = new ReplayHandler();
            var haiku = new HaikuDecider(new AnthropicClient { ApiKey = "fake", HttpClient = new HttpClient(handler), MaxRetries = 0 });
            var registry = new DeciderRegistry(haiku, new(new HttpClient(handler), "fake"), new ConfigurationBuilder().Build());
            var runner = new EvaluationRunner(eval, ai, registry);
            Assert.Equal(1, await runner.ReplayAsync("haiku-5-5", default));
            Assert.Equal(0, await runner.ReplayAsync("haiku-5-5", default));
            Assert.Equal(1, handler.Calls);
            await using var verify = await database.OpenAsync();
            Assert.Equal("fix", await verify.ExecuteScalarAsync<string>("SELECT action FROM verdict"));
            Assert.Equal("merge", await verify.ExecuteScalarAsync<string>("SELECT predicted FROM evaluation_result"));
            Assert.Equal(0, (await runner.ReportAsync(default)).Stats.First(s => s.Decider == "haiku-5-5").Agreement);
            await sync.SaveSettingsAsync(new([], [new("example/repo")]));
            Assert.False(await eval.MayReplayAsync((await eval.ReadAsync(default)).Examples.Single(), default));
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void MetricsUseLabelledDataAndExactCalibration()
    {
        EvaluationExample Example(string id, string kind, string expected, string predicted, double probability) =>
            new(id, kind, id, expected, "haiku-5-5", predicted, probability, null, "now");
        var report = EvaluationRunner.Compute(new([
            Example("pair-1", "pair", "related", "related", .9),
            Example("pair-2", "pair", "unrelated", "related", .8),
            Example("pair-3", "pair", "related", "unrelated", .2),
            Example("verdict-1", "verdict", "fix", "fix", .8),
            Example("verdict-2", "verdict", "wait", "merge", .6)
        ], [], [new("haiku-5-5", .01m, 50), new("haiku-5-5", .03m, 150)], 2));
        var stats = Assert.Single(report.Stats);
        Assert.Equal(.5, stats.Precision); Assert.Equal(.5, stats.Recall); Assert.Equal(.5, stats.Agreement);
        Assert.Equal(.43, stats.PairBrier!.Value, 6);
        Assert.Equal(.2, stats.VerdictBrier!.Value, 6);
        Assert.Equal(2m, stats.CostPer100);
        Assert.Equal(10000, stats.LatencyPer100Ms);
        Assert.Equal(25, report.Needed);
        Assert.Equal(3, stats.Disagreements.Count);
        Assert.Contains("labelled examples", report.Markdown);
    }
    private sealed class ReplayHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var json = """{"action":"merge","confidence":0.7,"priority":3,"reason":"ready","intent":"intent"}""";
            var body = JsonSerializer.Serialize(new { id = "msg_test", type = "message", role = "assistant", model = HaikuDecider.ModelId,
                content = new[] { new { type = "text", text = json } }, stop_reason = "end_turn", stop_sequence = (string?)null,
                usage = new { input_tokens = 100, output_tokens = 20 } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
    [Fact]
    public async Task JevUsesDocumentedChoiceScoreAndNoulWithoutLiveCalls()
    {
        var handler = new FixtureHandler("jev-verdict.json");
        var jev = new JevDecider(new HttpClient(handler), "fake-key");
        var verdict = await jev.JudgePrAsync(DeciderTests.Context(), default);
        Assert.Equal(NextAction.Fix, verdict.Action);
        Assert.Equal(.8, verdict.Confidence);
        Assert.Equal(4, verdict.Priority);
        Assert.Null(verdict.Intent);
        using var request = JsonDocument.Parse(handler.Request);
        Assert.Equal("choice", request.RootElement.GetProperty("questions").GetProperty("action").GetProperty("type").GetString());
        Assert.Equal("score", request.RootElement.GetProperty("questions").GetProperty("priority").GetProperty("type").GetString());
        Assert.Equal(.000021m, jev.LastUsage!.Cost);
        handler.File = "jev-pair.json";
        var pair = new PairContext("pr:example/repo#1", "pr:example/repo#2", DeciderTests.Context(), DeciderTests.Context("pr:example/repo#2"), null);
        var result = Assert.Single(await jev.JudgePairsAsync([pair], default));
        Assert.Equal(.85, result.Related);
        Assert.Null(result.Reason);
    }
    [Fact]
    public async Task JevRejectsMalformedDistributionsAndUnrequestedAnswers()
    {
        var handler = new FixtureHandler("jev-verdict.json") { Corrupt = true };
        var jev = new JevDecider(new HttpClient(handler), "fake-key");
        await Assert.ThrowsAsync<InvalidOperationException>(() => jev.JudgePrAsync(DeciderTests.Context(), default));
    }
    private sealed class FixtureHandler(string file) : HttpMessageHandler
    {
        public string File { get; set; } = file;
        public bool Corrupt { get; init; }
        public string Request { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = await request.Content!.ReadAsStringAsync(ct);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var body = System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", File));
            if (Corrupt) body = body.Replace("\"fix\": 0.8", "\"fix\": 2.0");
            return new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
