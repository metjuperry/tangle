using Anthropic;
using Anthropic.Models.Messages;
using System.Diagnostics;
using System.Text.Json;
using Tangle.Core;

namespace Tangle.Ai;

public sealed class HaikuDecider(AnthropicClient client) : IDecider
{
    public const string ModelId = "claude-haiku-5-5";
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
    private static readonly string PairsSchema = """
        {"type":"object","properties":{"pairs":{"type":"array","items":{"type":"object",
        "properties":{"aRef":{"type":"string"},"bRef":{"type":"string"},"related":{"type":"number"},"reason":{"type":"string"}},
        "required":["aRef","bRef","related","reason"],"additionalProperties":false}}},
        "required":["pairs"],"additionalProperties":false}
        """;
    public async Task<PrJudgment> JudgePrAsync(PrContext pr, CancellationToken ct) =>
        ParseVerdict(await SendAsync("verdict", pr, VerdictSchema, 1024, ct));
    public async Task<IReadOnlyList<LinkJudgment>> JudgePairsAsync(IReadOnlyList<PairContext> pairs, CancellationToken ct) =>
        ParsePairs(await SendAsync("pairs", pairs, PairsSchema, 4096, ct), pairs);

    private async Task<string> SendAsync(string job, object input, string schema, int maxTokens, CancellationToken ct)
    {
        LastUsage = null;
        var serialized = JsonSerializer.Serialize(input, Json);
        if (serialized.Length > 60000) throw new InvalidOperationException("AI input exceeds the reserved-cost safety limit.");
        var watch = Stopwatch.StartNew();
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = ModelId, MaxTokens = maxTokens, System = Prompt(job),
            Messages = [new() { Role = Role.User, Content = serialized }],
            OutputConfig = new() { Format = new() { Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)! } }
        }, ct);
        var inputTokens = response.Usage.InputTokens;
        var outputTokens = response.Usage.OutputTokens;
        LastUsage = new(inputTokens, outputTokens, inputTokens * .10m / 1_000_000 + outputTokens * .50m / 1_000_000, watch.ElapsedMilliseconds);
        if (response.StopReason != StopReason.EndTurn)
            throw new InvalidOperationException($"Haiku did not complete its structured answer ({response.StopReason}).");
        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().Select(b => b.Text).ToArray();
        if (text.Length != 1) throw new InvalidOperationException("Haiku returned missing or multiple answer blocks.");
        return text[0];
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
        var allowed = expected.Select(p => (p.ARef, p.BRef)).ToHashSet();
        if (response.Pairs is null || response.Pairs.Count != expected.Count) throw new InvalidOperationException("Pair answer has missing entries.");
        foreach (var p in response.Pairs)
        {
            if (!allowed.Remove((p.ARef, p.BRef))) throw new InvalidOperationException("Unknown or duplicate pair in answer.");
            Probability(p.Related);
            if (string.IsNullOrWhiteSpace(p.Reason)) throw new InvalidOperationException("Pair answer needs a reason.");
        }
        return response.Pairs;
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
        public required IReadOnlyList<LinkJudgment> Pairs { get; init; }
    }
}
