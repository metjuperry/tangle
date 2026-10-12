using System.Text;
using System.Text.Json;
using Tangle.Core;

namespace Tangle.Ai;

public sealed record DeciderStats(string Decider, int PairExamples, int VerdictExamples, double? Precision,
    double? Recall, double? Agreement, double? PairBrier, double? VerdictBrier, decimal? CostPer100,
    double? LatencyPer100Ms, IReadOnlyList<EvalDisagreement> Disagreements);
public sealed record EvalDisagreement(string Target, string Expected, string Predicted);
public sealed record EvaluationReport(int LabelledExamples, int Needed, int ExcludedSnoozes,
    int ReplayableExamples, IReadOnlyList<DeciderStats> Stats, string Markdown);

public sealed class EvaluationRunner(IEvaluationStore store, IAiStore aiStore, DeciderRegistry registry, SemaphoreSlim? gate = null) : IEvaluationReports
{
    private readonly SemaphoreSlim replayGate = gate ?? new(1, 1);
    public async Task<object> GetAsync(CancellationToken ct) => await ReportAsync(ct);
    public async Task<string> MarkdownAsync(CancellationToken ct) => (await ReportAsync(ct)).Markdown;
    public async Task<EvaluationReport> ReportAsync(CancellationToken ct) => Compute(await store.ReadAsync(ct));
    public static EvaluationReport Compute(EvaluationData data)
    {
        var labelled = data.Examples.Select(e => e.Id).Distinct().Count();
        var names = data.Examples.Select(e => e.Decider).Concat(data.Replays.Select(r => r.Decider)).Concat(data.Calls.Select(c => c.Decider)).Distinct().Order();
        var stats = new List<DeciderStats>();
        foreach (var name in names)
        {
            var originals = data.Examples.Where(e => e.Decider == name && e.Predicted is not null && e.Probability is not null).Select(e => (Example: e, Predicted: e.Predicted!, Probability: e.Probability!.Value));
            var replays = data.Replays.Where(r => r.Decider == name).Select(r =>
                (Example: data.Examples.FirstOrDefault(e => e.Id == r.ExampleId), r.Predicted, r.Probability)).Where(r => r.Example is not null)
                .Select(r => (Example: r.Example!, r.Predicted, r.Probability));
            var rows = replays.Concat(originals).GroupBy(r => r.Example.Id).Select(g => g.First()).ToArray();
            var pairs = rows.Where(r => r.Example.Kind == "pair").ToArray();
            var verdicts = rows.Where(r => r.Example.Kind == "verdict").ToArray();
            var tp = pairs.Count(r => r.Example.Expected == "related" && r.Predicted == "related");
            var fp = pairs.Count(r => r.Example.Expected != "related" && r.Predicted == "related");
            var fn = pairs.Count(r => r.Example.Expected == "related" && r.Predicted != "related");
            var calls = data.Calls.Where(c => c.Decider == name).ToArray();
            stats.Add(new(name, pairs.Length, verdicts.Length, tp + fp == 0 ? null : (double)tp / (tp + fp),
                tp + fn == 0 ? null : (double)tp / (tp + fn),
                verdicts.Length == 0 ? null : (double)verdicts.Count(r => r.Predicted == r.Example.Expected) / verdicts.Length,
                pairs.Length == 0 ? null : pairs.Average(r => Math.Pow(r.Probability - (r.Example.Expected == "related" ? 1 : 0), 2)),
                verdicts.Length == 0 ? null : verdicts.Average(r => Math.Pow(r.Probability - (r.Predicted == r.Example.Expected ? 1 : 0), 2)),
                calls.Length == 0 ? null : calls.Average(c => c.Cost) * 100,
                calls.Length == 0 ? null : calls.Average(c => (double)c.LatencyMs) * 100,
                rows.Where(r => r.Predicted != r.Example.Expected).Select(r => new EvalDisagreement(r.Example.Target, r.Example.Expected, r.Predicted)).ToArray()));
        }
        static string Percent(double? n) => n.HasValue ? $"{n:P1}" : "Not enough data";
        var markdown = new StringBuilder($"# Tangle evaluation\n\n{labelled} labelled examples; {Math.Max(0, 30 - labelled)} more needed for 30. {data.ExcludedSnoozes} Snooze choices excluded.\n\n");
        markdown.AppendLine("Metrics cover only labelled examples, not all possible PR relationships. Verdict Brier uses confidence in the selected action, not a full multiclass distribution.\n");
        markdown.AppendLine("| Decider | Pair samples | Verdict samples | Precision | Recall | Agreement | Pair Brier | Verdict Brier | Cost / 100 calls | Latency / 100 calls |");
        markdown.AppendLine("|---|---:|---:|---|---|---|---|---|---|---|");
        foreach (var s in stats)
        {
            markdown.AppendLine($"| {s.Decider} | {s.PairExamples} | {s.VerdictExamples} | {Percent(s.Precision)} | {Percent(s.Recall)} | {Percent(s.Agreement)} | {s.PairBrier?.ToString("F3") ?? "N/A"} | {s.VerdictBrier?.ToString("F3") ?? "N/A"} | {s.CostPer100?.ToString("C4") ?? "N/A"} | {s.LatencyPer100Ms?.ToString("F0") ?? "N/A"} ms |");
        }
        foreach (var s in stats.Where(s => s.Disagreements.Count > 0))
        {
            markdown.AppendLine($"\n## {s.Decider} disagreements\n");
            foreach (var item in s.Disagreements)
                markdown.AppendLine($"- {item.Target.Replace("\n", " ↔ ")}: you chose {item.Expected}; model chose {item.Predicted}.");
        }
        return new(labelled, Math.Max(0, 30 - labelled), data.ExcludedSnoozes,
            data.Examples.Where(e => e.InputJson is not null).Select(e => e.Id).Distinct().Count(), stats, markdown.ToString());
    }
    public async Task<int> ReplayAsync(string name, CancellationToken ct)
    {
        if (!await replayGate.WaitAsync(0, ct)) throw new InvalidOperationException("An evaluation replay is already running.");
        try { return await RunReplayAsync(name, ct); }
        finally { replayGate.Release(); }
    }
    private async Task<int> RunReplayAsync(string name, CancellationToken ct)
    {
        var decider = registry.Get(name);
        var data = await store.ReadAsync(ct);
        var count = 0;
        foreach (var example in data.Examples.GroupBy(e => e.Id).Select(g => g.First()))
        {
            if (data.Replays.Any(r => r.ExampleId == example.Id && r.Decider == name)) continue;
            if (!await store.MayReplayAsync(example, ct)) continue;
            var input = example.InputJson!;
            var hash = $"eval:{name}:{example.Id}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input)))}";
            var snapshot = await aiStore.SnapshotAsync(ct);
            var work = new AiWork("eval:" + name, example.Id, hash);
            if (await aiStore.LookedAsync(work, snapshot.Prs, ct)) continue;
            var reservation = await aiStore.ReserveAsync(hash, name, example.Kind == "pair" ? .20m : .02m, ct,
                "evaluation", [example.Target], [work], input);
            if (reservation is null) throw new InvalidOperationException("Monthly budget cannot cover the next evaluation call.");
            try
            {
                if (!await store.MayReplayAsync(example, ct))
                { await aiStore.FailAsync(reservation, "Opt-in revoked before replay", new(0, 0, 0, 0), ct); continue; }
                await ProviderExecution.Gate.WaitAsync(ct);
                try
                {
                string prediction; double probability;
                if (example.Kind == "verdict")
                {
                    var context = JsonSerializer.Deserialize<PrContext>(input, HaikuDecider.Json)!;
                    var verdict = await decider.JudgePrAsync(context, ct);
                    prediction = verdict.Action.ToString().ToLowerInvariant(); probability = verdict.Confidence;
                }
                else
                {
                    var pair = JsonSerializer.Deserialize<PairContext>(input, HaikuDecider.Json)!;
                    var judgment = (await decider.JudgePairsAsync([pair], ct)).Single();
                    prediction = judgment.Related > .75 ? "related" : "unrelated"; probability = judgment.Related;
                }
                await store.SaveReplayAsync(example, name, prediction, probability, reservation, decider.LastUsage!, ct);
                }
                finally { ProviderExecution.Gate.Release(); }
                count++;
            }
            catch (Exception e)
            { await aiStore.FailAsync(reservation, e.Message, decider.LastUsage, CancellationToken.None); throw; }
        }
        return count;
    }
}
