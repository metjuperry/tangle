using Anthropic;
using Anthropic.Models.Messages;
using System.Diagnostics;
using System.Text.Json;
using Tangle.Core;

namespace Tangle.Ai;

public sealed class HaikuDecider(AnthropicClient client) : IMeasuredDecider
{
    public const string ModelId = "claude-haiku-5-5";
    public const int MaximumInputBytes = 60000;
    public string Name => "haiku-5-5";
    public AiUsage? LastUsage { get; private set; }
    public static string Prompt(string job)
    {
        using var reader = new StreamReader(typeof(HaikuDecider).Assembly.GetManifestResourceStream($"Tangle.Ai.prompts.{job}.v1.md")!);
        return reader.ReadToEnd();
    }
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static readonly string VerdictSchema = """
        {"type":"object","properties":{"action":{"type":"string","enum":["merge","fix","ping","wait","close","split"]},
        "confidence":{"type":"number"},"priority":{"type":"integer"},"reason":{"type":"string"},"intent":{"type":"string"}},
        "required":["action","confidence","priority","reason","intent"],"additionalProperties":false}
        """;
    public async Task<PrJudgment> JudgePrAsync(PrContext pr, CancellationToken ct) =>
        ParseVerdict(await SendAsync("verdict", pr, VerdictSchema, 1024, ct));
    public async Task<PrJudgment> ExplainAsync(PrContext pr, PrJudgment judgment, CancellationToken ct)
    {
        var input = new { pr, action = judgment.Action.ToString().ToLowerInvariant(), judgment.Confidence, judgment.Priority };
        var answer = ParseVerdict(await SendAsync("explain", input, VerdictSchema, 1024, ct));
        if (answer.Action != judgment.Action) throw new InvalidOperationException("Explanation attempted to change the chosen action.");
        return judgment with { Reason = answer.Reason, Intent = answer.Intent };
    }
    public async Task<IReadOnlyList<LinkJudgment>> JudgePairsAsync(IReadOnlyList<PairContext> pairs, CancellationToken ct)
    {
        if (pairs.Count == 0) return [];
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                pairs = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            pairIndex = new { type = "integer", @enum = Enumerable.Range(0, pairs.Count).ToArray() },
                            related = new { type = "number" },
                            reason = new { type = "string" }
                        },
                        required = new[] { "pairIndex", "related", "reason" },
                        additionalProperties = false
                    }
                }
            },
            required = new[] { "pairs" },
            additionalProperties = false
        });
        return ParsePairs(await SendAsync("pairs", PairInput(pairs), schema, 4096, ct), pairs);
    }
    public static object PairInput(IReadOnlyList<PairContext> pairs) =>
        pairs.Select((context, pairIndex) => new { pairIndex, context }).ToArray();

    private async Task<string> SendAsync(string job, object input, string schema, int maxTokens, CancellationToken ct)
    {
        LastUsage = new(0, 0, 0, 0);
        var serialized = SerializeInput(input);
        var watch = Stopwatch.StartNew();
        var parameters = new MessageCreateParams
        {
            Model = ModelId, MaxTokens = maxTokens, System = Prompt(job),
            Messages = [new() { Role = Role.User, Content = serialized }],
            OutputConfig = new() { Format = new() { Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)! } }
        };
        LastUsage = null;
        var response = await client.Messages.Create(parameters, ct);
        var inputTokens = response.Usage.InputTokens;
        var outputTokens = response.Usage.OutputTokens;
        LastUsage = new(inputTokens, outputTokens, inputTokens * .10m / 1_000_000 + outputTokens * .50m / 1_000_000, watch.ElapsedMilliseconds);
        if (response.StopReason != StopReason.EndTurn)
            throw new InvalidOperationException($"Haiku did not complete its structured answer ({response.StopReason}).");
        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().Select(b => b.Text).ToArray();
        if (text.Length != 1) throw new InvalidOperationException("Haiku returned missing or multiple answer blocks.");
        return text[0];
    }
    public static string SerializeInput(object input)
    {
        var serialized = JsonSerializer.Serialize(input, Json);
        if (System.Text.Encoding.UTF8.GetByteCount(serialized) > MaximumInputBytes)
            throw new InvalidOperationException("A single AI context exceeds the 60 KB request limit. No provider call was made.");
        return serialized;
    }
    public static PrJudgment ParseVerdict(string json)
    {
        var value = JsonSerializer.Deserialize<VerdictResponse>(json, Json) ?? throw new InvalidOperationException("Empty verdict.");
        if (!Enum.TryParse<NextAction>(value.Action, true, out var action) || !Enum.IsDefined(action))
            throw new InvalidOperationException("Invalid next action.");
        Probability(value.Confidence);
        if (string.IsNullOrWhiteSpace(value.Reason) || string.IsNullOrWhiteSpace(value.Intent))
            throw new InvalidOperationException("Verdict needs a reason and intent.");
        return new(action, value.Confidence, Math.Clamp(value.Priority, 1, 5), value.Reason, value.Intent);
    }
    public static IReadOnlyList<LinkJudgment> ParsePairs(string json, IReadOnlyList<PairContext> expected)
    {
        var response = JsonSerializer.Deserialize<PairsResponse>(json, Json) ?? throw new InvalidOperationException("Empty pair answer.");
        var seen = new HashSet<int>();
        var result = new List<LinkJudgment>();
        if (response.Pairs is null || response.Pairs.Count != expected.Count) throw new InvalidOperationException("Pair answer has missing entries.");
        foreach (var p in response.Pairs)
        {
            if (p.PairIndex < 0 || p.PairIndex >= expected.Count)
                throw new InvalidOperationException($"Unknown pair index {p.PairIndex}; expected 0–{expected.Count - 1}.");
            if (!seen.Add(p.PairIndex))
                throw new InvalidOperationException($"Duplicate pair index {p.PairIndex} in answer; no suggestions were saved.");
            Probability(p.Related);
            if (string.IsNullOrWhiteSpace(p.Reason)) throw new InvalidOperationException("Pair answer needs a reason.");
            var pair = expected[p.PairIndex];
            result.Add(new(pair.ARef, pair.BRef, p.Related, p.Reason));
        }
        return result;
    }
    private static void Probability(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1) throw new InvalidOperationException("Invalid confidence.");
    }
    public sealed record VerdictResponse
    {
        public required string Action { get; init; }
        public required double Confidence { get; init; }
        public required int Priority { get; init; }
        public required string Reason { get; init; }
        public required string Intent { get; init; }
    }
    public sealed record PairsResponse
    {
        public required IReadOnlyList<PairResponse> Pairs { get; init; }
    }
    public sealed record PairResponse
    {
        public required int PairIndex { get; init; }
        public required double Related { get; init; }
        public required string Reason { get; init; }
    }
}
