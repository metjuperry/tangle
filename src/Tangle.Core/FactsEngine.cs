using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tangle.Core;

public sealed record PrFacts(string PrId, string Turn, string TurnReason, int AgeDays, int IdleDays,
    int? BehindBy, bool Conflicting, string Size, string Origin, string ComputedAt)
{
    public PrFacts() : this("", "", "", 0, 0, null, false, "", "", "") { }
}

public static partial class FactsEngine
{
    public static PrFacts Compute(PullRequest pr, DateTimeOffset now, IReadOnlyList<string> botLogins)
    {
        var humanEvents = pr.Events.Where(e => e.Kind is "commit" or "comment" or "review"
            && e.Actor is not null && !IsBot(e.Actor, botLogins)).ToArray();
        var threadActivity = pr.Threads.Where(t => t.LastAt is not null && t.LastActor is not null
            && !IsBot(t.LastActor, botLogins)).Select(t => At(t.LastAt!));
        var lastHuman = humanEvents.Select(e => At(e.At)).Concat(threadActivity)
            .Append(At(pr.CreatedAt)).Max();
        var idle = Days(lastHuman, now);
        var commits = pr.Events.Where(e => e.Kind == "commit").Select(e => At(e.At)).ToArray();
        var lastCommit = commits.Length == 0 ? At(pr.CreatedAt) : commits.Max();
        var openAsks = pr.Threads.Count(t => !t.IsResolved && t.LastActor is not null
            && !IsBot(t.LastActor, botLogins) && !Same(t.LastActor, pr.Viewer));
        var changesReview = humanEvents.Where(e => e.Kind == "review" && ReviewState(e) == "CHANGES_REQUESTED")
            .Select(e => At(e.At)).OrderDescending().Cast<DateTimeOffset?>().FirstOrDefault();
        var requested = pr.ReviewRequests.Any(r => r.Kind == "User" && Same(r.Reviewer, pr.Viewer));
        var reviewedSinceCommit = humanEvents.Any(e => e.Kind == "review" && Same(e.Actor, pr.Viewer) && At(e.At) >= lastCommit);
        var conflicting = pr.Mergeable == "CONFLICTING";
        var failing = pr.ChecksState is "FAILURE" or "ERROR";
        (string turn, string reason) = pr switch
        {
            { IsMine: true, IsDraft: true } => ("me", "finish or close the draft"),
            { IsMine: true } when failing || conflicting => ("me",
                failing && conflicting ? "fix failing checks and resolve conflicts" :
                failing ? "fix failing checks" : "resolve conflicts"),
            { IsMine: true } when openAsks > 0 => ("me", $"{openAsks} open review {(openAsks == 1 ? "comment" : "comments")}"),
            { IsMine: true, ReviewDecision: "CHANGES_REQUESTED" } when changesReview is null || lastCommit <= changesReview =>
                ("me", "address requested changes"),
            { IsMine: false } when requested && !reviewedSinceCommit => ("me", "your review is requested"),
            { IsMine: true, ReviewDecision: "APPROVED", ChecksState: "SUCCESS" } => ("me", "ready to merge"),
            _ when idle >= 21 => ("nobody", $"no human activity for {idle} days"),
            _ => ("them", Reviewers(pr, botLogins))
        };
        var lines = (long)pr.Additions + pr.Deletions;
        var size = lines switch { <= 10 => "XS", <= 100 => "S", <= 500 => "M", <= 1000 => "L", _ => "XL" };
        var text = pr.Body + "\n" + string.Join("\n", pr.Events.Where(e => e.Kind == "commit").Select(EventText));
        var origin = Same(pr.Author, "dependabot") || Same(pr.Author, "dependabot[bot]") ? "bot" :
            Same(pr.Author, "copilot") || Same(pr.Author, "copilot[bot]") || AgentMarker().IsMatch(text) ? "agent" :
            IsBot(pr.Author, botLogins) ? "bot" : "human";
        return new(pr.Id, turn, reason, Days(At(pr.CreatedAt), now), idle, null, conflicting,
            size, origin, now.ToString("O", CultureInfo.InvariantCulture));
    }

    public static bool IsBot(string? actor, IReadOnlyList<string> bots) =>
        actor is not null && bots.Contains(actor, StringComparer.OrdinalIgnoreCase);
    public static bool Same(string? a, string? b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static int Days(DateTimeOffset from, DateTimeOffset to) => Math.Max(0, (int)(to - from).TotalDays);
    private static string Reviewers(PullRequest pr, IReadOnlyList<string> bots)
    {
        var reviewers = pr.ReviewRequests.Where(r => !IsBot(r.Reviewer, bots)).Select(r => r.Reviewer).Order().ToArray();
        return reviewers.Length == 0 ? "waiting on others" : $"waiting on {string.Join(", ", reviewers)}";
    }

    public static string EventText(PrEvent e)
    {
        using var json = JsonDocument.Parse(e.Detail);
        var root = json.RootElement;
        if (root.TryGetProperty("body", out var body)) return body.GetString() ?? "";
        if (root.TryGetProperty("commit", out var commit) && commit.TryGetProperty("message", out var message))
            return message.GetString() ?? "";
        return "";
    }
    private static string? ReviewState(PrEvent e)
    {
        using var json = JsonDocument.Parse(e.Detail);
        return json.RootElement.TryGetProperty("state", out var state) ? state.GetString() : null;
    }

    [GeneratedRegex(@"Claude-Session:|Generated with Claude Code|Co-Authored-By:[^\r\n]*Claude", RegexOptions.IgnoreCase)]
    private static partial Regex AgentMarker();
}
