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
            Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(HaikuDecider.SerializeInput(batch)), 1, HaikuDecider.MaximumInputBytes);
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
        Assert.Single(HaikuDecider.ParsePairs("""{"pairs":[{"aRef":"pr:example/repo#1","bRef":"pr:example/repo#2","related":0.5,"reason":"weak"}]}""", pairs));
        Assert.Throws<InvalidOperationException>(() => HaikuDecider.ParsePairs("""{"pairs":[]}""", pairs));
        Assert.Throws<InvalidOperationException>(() => HaikuDecider.ParsePairs("""{"pairs":[{"aRef":"unknown","bRef":"unknown","related":0.9,"reason":"invented"}]}""", pairs));
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
            var json = """{"action":"fix","confidence":0.9,"priority":4,"reason":"4 open review comments","intent":"Add metadata"}""";
            var body = JsonSerializer.Serialize(new { id = "msg_fixture", type = "message", role = "assistant",
                model = "claude-haiku-5-5", content = new[] { new { type = "text", text = json } },
                stop_reason = "end_turn", stop_sequence = (string?)null, usage = new { input_tokens = 100, output_tokens = 40 } });
            return new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
