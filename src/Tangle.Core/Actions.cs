namespace Tangle.Core;

public sealed record ActionPayload(string? Text = null, string? Reviewer = null, string? HeadSha = null);
public sealed record QueuedAction(string Id, string PrId, string Kind, string Payload, string Status,
    string CreatedAt, string? AppliedAt, string? Result)
{
    public QueuedAction() : this("", "", "", "", "", "", null, null) { }
}
public sealed record GitHubActionTarget(string Id, string HeadSha, string State, string Author, string Viewer);
public interface IGitHubActions
{
    Task<GitHubActionTarget> InspectAsync(string repo, int number, CancellationToken ct);
    Task<string> ApplyAsync(string repo, GitHubActionTarget target, string kind, ActionPayload payload, CancellationToken ct);
}
public sealed record DeckDecision(string Id, string PrId, string Choice, string? ActionId, string CreatedAt)
{
    public DeckDecision() : this("", "", "", null, "") { }
}
public sealed record ReceiptRound(string Id, string CreatedAt, IReadOnlyList<QueuedAction> Actions,
    IReadOnlyList<DeckDecision> Decisions);
