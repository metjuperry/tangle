namespace Tangle.Core;

public interface IDecider
{
    string Name { get; }
    Task<PrJudgment> JudgePrAsync(PrContext pr, CancellationToken ct);
    Task<IReadOnlyList<LinkJudgment>> JudgePairsAsync(IReadOnlyList<PairContext> pairs, CancellationToken ct);
}
public enum NextAction { Merge, Fix, Ping, Wait, Close, Split }
public sealed record PrJudgment(NextAction Action, double Confidence, int Priority, string? Reason, string? Intent);
public sealed record LinkJudgment(string ARef, string BRef, double Related, string? Reason);
public sealed record AiFact(string Turn, string TurnReason, int AgeDays, int IdleDays, int? BehindBy, bool Conflicting, string Size, string Origin);
public sealed record LinkedContext(string Ref, string Title, string? Label);
public sealed record PrContext(string Ref, string RepoId, string Title, string Body, string? Author,
    string CreatedAt, IReadOnlyList<string> Paths, AiFact Facts, IReadOnlyList<string> OpenComments,
    IReadOnlyList<LinkedContext> Links);
public sealed record PairContext(string ARef, string BRef, PrContext A, PrContext? B, string? StickyTitle);
public sealed record AiSnapshot(IReadOnlyList<PrContext> Prs, IReadOnlyList<AiSticky> Stickies,
    IReadOnlyList<AiPair> ExistingPairs, decimal Budget);
public sealed record AiSticky(string Ref, string Title);
public sealed record AiPair(string ARef, string BRef);
public sealed record AiUsage(long InputTokens, long OutputTokens, decimal Cost, long LatencyMs);
public sealed record AiReservation(string Id, string InputHash);
public sealed record StoredVerdict(string PrId, string Action, double Confidence, int Priority, string? Reason, string? Intent, string CreatedAt)
{
    public StoredVerdict() : this("", "", 0, 0, null, null, "") { }
}
public interface IAiStore
{
    Task<AiSnapshot> SnapshotAsync(CancellationToken ct);
    Task<bool> CachedAsync(string hash, CancellationToken ct);
    Task<AiReservation?> ReserveAsync(string hash, string decider, decimal maximumCost, CancellationToken ct);
    Task FinishAsync(AiReservation reservation, AiUsage usage, PrContext? pr, PrJudgment? verdict,
        IReadOnlyList<LinkJudgment>? links, CancellationToken ct);
    Task FailAsync(AiReservation reservation, string error, AiUsage? usage, CancellationToken ct);
}
public interface IAiQueue { void Enqueue(); }
