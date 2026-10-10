namespace Tangle.Core;

public interface IMeasuredDecider : IDecider
{
    AiUsage? LastUsage { get; }
}
public sealed record EvaluationExample(string Id, string Kind, string Target, string Expected,
    string Decider, string? Predicted, double? Probability, string? InputJson, string LabelledAt);
public sealed record EvaluationPrediction(string ExampleId, string Decider, string Predicted,
    double Probability, decimal Cost, long LatencyMs)
{
    public EvaluationPrediction() : this("", "", "", 0, 0, 0) { }
}
public sealed record EvaluationData(IReadOnlyList<EvaluationExample> Examples,
    IReadOnlyList<EvaluationPrediction> Replays, IReadOnlyList<AiUsageRecord> Calls, int ExcludedSnoozes);
public sealed record AiUsageRecord(string Decider, decimal Cost, long LatencyMs)
{
    public AiUsageRecord() : this("", 0, 0) { }
}
public interface IEvaluationStore
{
    Task<EvaluationData> ReadAsync(CancellationToken ct);
    Task<bool> MayReplayAsync(EvaluationExample example, CancellationToken ct);
    Task SaveReplayAsync(EvaluationExample example, string decider, string predicted, double probability,
        AiReservation reservation, AiUsage usage, CancellationToken ct);
}
public interface IEvaluationReports
{
    Task<object> GetAsync(CancellationToken ct);
    Task<string> MarkdownAsync(CancellationToken ct);
    Task<int> ReplayAsync(string name, CancellationToken ct);
}
public sealed record UnexplainedVerdict(string InputHash, PrJudgment Judgment);
public interface IExplanationStore
{
    Task<UnexplainedVerdict?> UnexplainedAsync(string prId, CancellationToken ct);
    Task SaveAsync(string prId, string inputHash, PrJudgment judgment, AiReservation reservation, AiUsage usage, CancellationToken ct);
}
public interface IExplanationService { Task ExplainAsync(string prId, CancellationToken ct); }
