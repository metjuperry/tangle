using Anthropic;
using Tangle.Ai;
using Tangle.Core;
using System.Net;
using System.Text.Json;

namespace Tangle.Ai.Evals;

public sealed class DeciderTests
{
    public static PrContext Context(string reference = "pr:example/repo#1") =>
        new(reference, "example/repo", "Connector metadata", "Description", "viewer", "2026-10-01T00:00:00Z",
            ["src/connector.cs"], new("me", "4 open review comments", 14, 7, null, false, "M", "agent"), ["Please fix"], []);
    [Fact]
    public void LookKeysIgnoreClockAgingButTrackPrChangesAndAuthoredContext()
    {
        var pr = Context() with { UpdatedAt = "2026-10-10T00:00:00Z" };
        var aged = pr with { Facts = pr.Facts with { AgeDays = 25, IdleDays = 21, Turn = "nobody", TurnReason = "no human activity for 21 days" } };
        var waiting = pr with { Facts = pr.Facts with { Turn = "them", TurnReason = "waiting on others" } };
        Assert.Equal(AiCacheKeys.Verdict(waiting), AiCacheKeys.Verdict(aged));
        Assert.Equal(AiCacheKeys.Verdict(pr), AiCacheKeys.Verdict(pr with { Facts = pr.Facts with { AgeDays = 99, IdleDays = 100 } }));
        Assert.NotEqual(AiCacheKeys.Verdict(pr), AiCacheKeys.Verdict(pr with { UpdatedAt = "2026-10-11T00:00:00Z" }));
        Assert.NotEqual(AiCacheKeys.Verdict(pr), AiCacheKeys.Verdict(pr with { Links = [new("sticky:goal", "Release", "required")] }));
        var pair = new PairContext(pr.Ref, "pr:example/repo#2", pr, Context("pr:example/repo#2"), null);
        Assert.Equal(AiCacheKeys.Pair(pair), AiCacheKeys.Pair(pair with { A = pr with { Facts = pr.Facts with { IdleDays = 99 } } }));
    }
    [Fact]
    public void LargePairSetsSplitByPayloadSizeWithoutDroppingPairs()
    {
        var pairs = Enumerable.Range(1, 45).Select(i => new PairContext(
            $"pr:example/repo#{i}", $"pr:example/repo#{i + 100}",
            Context($"pr:example/repo#{i}") with { Body = new string('x', 4000) },
            Context($"pr:example/repo#{i + 100}") with { Body = new string('y', 4000) }, null)).ToArray();
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(pairs.Take(20), HaikuDecider.Json)) > 60000);
        var batches = AiWorker.Batches(pairs);
        Assert.Equal(pairs, batches.SelectMany(b => b).ToArray());
        Assert.All(batches, batch =>
        {
            Assert.InRange(batch.Length, 1, 20);
            Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(HaikuDecider.SerializeInput(HaikuDecider.PairInput(batch))), 1, HaikuDecider.MaximumInputBytes);
        });
        Assert.Equal(3, AiWorker.Batches(pairs.Select(p => p with { A = Context(), B = Context() }).ToArray()).Count);
    }
    [Fact]
    public async Task RejectedInputHasZeroUsageAndNeverReachesProvider()
    {
        var handler = new RecordedHandler();
        var decider = new HaikuDecider(new AnthropicClient { ApiKey = "fake", HttpClient = new HttpClient(handler), MaxRetries = 0 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => decider.JudgePrAsync(Context() with { Body = new string('x', 60001) }, default));
        Assert.Equal("", handler.Request);
        Assert.Equal(0m, decider.LastUsage!.Cost);
    }
    [Fact]
    public void VerdictValidatesEnumsRequiredFieldsAndClampsPriority()
    {
        var verdict = HaikuDecider.ParseVerdict("""{"action":"fix","confidence":0.9,"priority":99,"reason":"4 open review comments","intent":"Add metadata"}""");
        Assert.Equal(NextAction.Fix, verdict.Action); Assert.Equal(5, verdict.Priority);
        Assert.Throws<InvalidOperationException>(() => HaikuDecider.ParseVerdict("""{"action":"delete","confidence":0.9,"priority":1,"reason":"x","intent":"x"}"""));
        Assert.Throws<JsonException>(() => HaikuDecider.ParseVerdict("""{"action":"fix"}"""));
        Assert.Throws<InvalidOperationException>(() => HaikuDecider.ParseVerdict("""{"action":"fix","confidence":2,"priority":1,"reason":"x","intent":"x"}"""));
    }
    [Fact]
    public void PairAnswersMustMatchExactRequestedPairs()
    {
        PairContext[] pairs = [new("pr:example/repo#1", "pr:example/repo#2", Context(), Context("pr:example/repo#2"), null)];
        var result = Assert.Single(HaikuDecider.ParsePairs("""{"pairs":[{"pairIndex":0,"related":0.5,"reason":"weak"}]}""", pairs));
        Assert.Equal(pairs[0].ARef, result.ARef);
        Assert.Equal(pairs[0].BRef, result.BRef);
        Assert.Throws<InvalidOperationException>(() => HaikuDecider.ParsePairs("""{"pairs":[]}""", pairs));
        Assert.Throws<InvalidOperationException>(() => HaikuDecider.ParsePairs("""{"pairs":[{"pairIndex":99,"related":0.9,"reason":"invented"}]}""", pairs));
    }
    [Fact]
    public void IndexedAnswersAllowReorderingButRejectDuplicatesAndMissingIndices()
    {
        PairContext[] pairs = [
            new("pr:example/repo#1", "pr:example/repo#2", Context(), Context("pr:example/repo#2"), null),
            new("pr:example/repo#1", "sticky:goal", Context(), null, "Goal")
        ];
        var results = HaikuDecider.ParsePairs("""{"pairs":[{"pairIndex":1,"related":0.9,"reason":"goal"},{"pairIndex":0,"related":0.8,"reason":"dependency"}]}""", pairs);
        Assert.Equal("sticky:goal", results[0].BRef);
        Assert.Equal("pr:example/repo#2", results[1].BRef);
        Assert.Throws<InvalidOperationException>(() => HaikuDecider.ParsePairs("""{"pairs":[{"pairIndex":0,"related":0.9,"reason":"one"},{"pairIndex":0,"related":0.8,"reason":"again"}]}""", pairs));
        Assert.Throws<JsonException>(() => HaikuDecider.ParsePairs("""{"pairs":[{"related":0.9,"reason":"missing index"}]}""", pairs));
    }
    [Fact]
    public async Task OfficialSdkSendsNamedModelAndStructuredOutputWithoutRealNetwork()
    {
        var handler = new RecordedHandler();
        var decider = new HaikuDecider(new AnthropicClient { ApiKey = "fake-not-secret", HttpClient = new HttpClient(handler), MaxRetries = 0 });
        var verdict = await decider.JudgePrAsync(Context(), default);
        Assert.Equal(NextAction.Fix, verdict.Action);
        using var request = JsonDocument.Parse(handler.Request);
        Assert.Equal("claude-haiku-5-5", request.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", request.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(100, decider.LastUsage!.InputTokens);
    }
    [Fact]
    public async Task OfficialSdkConstrainsPairIndicesAndPreservesOriginalEndpoints()
    {
        var handler = new RecordedHandler();
        var decider = new HaikuDecider(new AnthropicClient { ApiKey = "fake", HttpClient = new HttpClient(handler), MaxRetries = 0 });
        PairContext[] pairs = [new("pr:example/repo#1", "sticky:release", Context(), null, "Release")];
        var result = Assert.Single(await decider.JudgePairsAsync(pairs, default));
        Assert.Equal("pr:example/repo#1", result.ARef);
        Assert.Equal("sticky:release", result.BRef);
        using var request = JsonDocument.Parse(handler.Request);
        var indices = request.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema")
            .GetProperty("properties").GetProperty("pairs").GetProperty("items").GetProperty("properties")
            .GetProperty("pairIndex").GetProperty("enum");
        Assert.Equal(0, indices[0].GetInt32());
        Assert.Equal(1, indices.GetArrayLength());
    }
    [Fact]
    public void CandidatePairsSkipTiedAndCutAndHashesChangeWithLabels()
    {
        var a = Context(); var b = Context("pr:example/repo#2");
        var snapshot = new AiSnapshot([a, b], [new("sticky:release", "Release")], [new(a.Ref, b.Ref)], 1);
        Assert.Equal(2, AiWorker.Candidates(snapshot).Count);
        Assert.NotEqual(AiWorker.Hash("verdict", a), AiWorker.Hash("verdict", a with { Links = [new(b.Ref, b.Title, "same release")] }));
    }
    private sealed class RecordedHandler : HttpMessageHandler
    {
        public string Request { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = await request.Content!.ReadAsStringAsync(ct);
            using var parsed = JsonDocument.Parse(Request);
            var pairCall = parsed.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema")
                .GetProperty("properties").TryGetProperty("pairs", out _);
            var json = pairCall ? """{"pairs":[{"pairIndex":0,"related":0.9,"reason":"shared release"}]}""" :
                """{"action":"fix","confidence":0.9,"priority":4,"reason":"4 open review comments","intent":"Add metadata"}""";
            var body = JsonSerializer.Serialize(new { id = "msg_fixture", type = "message", role = "assistant",
                model = "claude-haiku-5-5", content = new[] { new { type = "text", text = json } },
                stop_reason = "end_turn", stop_sequence = (string?)null, usage = new { input_tokens = 100, output_tokens = 40 } });
            return new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
