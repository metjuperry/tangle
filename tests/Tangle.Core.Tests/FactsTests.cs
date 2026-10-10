using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Tangle.Core;
using Tangle.GitHub;

namespace Tangle.Core.Tests;

public sealed class FactsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
    private static readonly string[] Bots = ["dependabot", "dependabot[bot]", "github-actions[bot]", "custom-bot"];
    private static PullRequest Pr() => SyncTests.Pr() with
    {
        CreatedAt = "2026-09-01T12:00:00Z", Events = [], Threads = [], ReviewRequests = [],
        Viewer = "viewer", ChecksState = null
    };
    private static PrEvent Event(string kind, string actor, string at, string? state = null) =>
        new(at, actor, kind, JsonSerializer.Serialize(new { state, body = "review" }));
    private static PrFacts Facts(PullRequest pr) => FactsEngine.Compute(pr, Now, Bots);

    [Fact]
    public void DraftWinsOverAllOtherRules()
    {
        var facts = Facts(Pr() with { IsDraft = true, ChecksState = "FAILURE", Mergeable = "CONFLICTING" });
        Assert.Equal("me", facts.Turn);
        Assert.Equal("finish or close the draft", facts.TurnReason);
    }

    [Theory]
    [InlineData("FAILURE", "MERGEABLE", "fix failing checks")]
    [InlineData("ERROR", "MERGEABLE", "fix failing checks")]
    [InlineData("SUCCESS", "CONFLICTING", "resolve conflicts")]
    [InlineData("FAILURE", "CONFLICTING", "fix failing checks and resolve conflicts")]
    public void FailingChecksOrConflictsWinOverThreads(string checks, string mergeable, string reason)
    {
        var facts = Facts(Pr() with { ChecksState = checks, Mergeable = mergeable,
            Threads = [new("t", null, false, "reviewer", "2026-10-09T12:00:00Z", "ask")] });
        Assert.Equal("me", facts.Turn);
        Assert.Equal(reason, facts.TurnReason);
        Assert.Equal(mergeable == "CONFLICTING", facts.Conflicting);
    }

    [Fact]
    public void OnlyUnresolvedOtherHumanCommentsNeedMe()
    {
        var facts = Facts(Pr() with { Threads = [
            new("1", null, false, "reviewer", "2026-10-09T12:00:00Z", "ask"),
            new("2", null, false, "viewer", "2026-10-09T12:00:00Z", "answered"),
            new("3", null, true, "reviewer", "2026-10-09T12:00:00Z", "resolved"),
            new("4", null, false, "custom-bot", "2026-10-10T12:00:00Z", "bot"),
            new("5", null, false, null, "2026-10-10T12:00:00Z", "unknown")
        ] });
        Assert.Equal("me", facts.Turn);
        Assert.Equal("1 open review comment", facts.TurnReason);
        Assert.Equal(1, facts.IdleDays);
    }

    [Fact]
    public void RequestedChangesNeedMeUntilCommitAfterReview()
    {
        var pr = Pr() with { ReviewDecision = "CHANGES_REQUESTED", Events = [
            Event("review", "reviewer", "2026-10-08T12:00:00Z", "CHANGES_REQUESTED")
        ] };
        Assert.Equal("address requested changes", Facts(pr).TurnReason);
        Assert.Equal("them", Facts(pr with { Events = [..pr.Events, Event("commit", "viewer", "2026-10-09T12:00:00Z")] }).Turn);
    }

    [Fact]
    public void RequestedReviewNeedsMeUnlessIReviewedAfterLatestCommit()
    {
        var pr = Pr() with { IsMine = false, Author = "other", ReviewRequests = [new("viewer", "User")],
            Events = [Event("commit", "other", "2026-10-08T12:00:00Z")] };
        Assert.Equal("your review is requested", Facts(pr).TurnReason);
        var reviewed = pr with { Events = [..pr.Events, Event("review", "VIEWER", "2026-10-09T12:00:00Z", "APPROVED")] };
        Assert.Equal("them", Facts(reviewed).Turn);
        Assert.Equal("me", Facts(reviewed with { Events = [..reviewed.Events, Event("commit", "other", "2026-10-10T11:00:00Z")] }).Turn);
    }

    [Fact]
    public void ApprovedGreenMineIsReadyEvenWhenIdle()
    {
        var facts = Facts(Pr() with { ReviewDecision = "APPROVED", ChecksState = "SUCCESS" });
        Assert.Equal("me", facts.Turn);
        Assert.Equal("ready to merge", facts.TurnReason);
        Assert.Equal("nobody", Facts(Pr() with { ReviewDecision = "APPROVED", ChecksState = null }).Turn);
    }

    [Theory]
    [InlineData("2026-09-19T12:00:00Z", "nobody", 21)]
    [InlineData("2026-09-19T12:00:01Z", "them", 20)]
    public void IdleThresholdUsesHumanActivityNotUpdatedAt(string lastHuman, string turn, int idle)
    {
        var facts = Facts(Pr() with { UpdatedAt = Now.ToString("O"), Events = [
            Event("comment", "reviewer", lastHuman), Event("ci", "github-actions[bot]", Now.ToString("O")),
            Event("comment", "custom-bot", Now.ToString("O"))
        ] });
        Assert.Equal(turn, facts.Turn);
        Assert.Equal(idle, facts.IdleDays);
        Assert.Equal(39, facts.AgeDays);
    }

    [Fact]
    public void FallbackNamesRequestedReviewerAndNotBots()
    {
        var facts = Facts(Pr() with { Events = [Event("comment", "viewer", "2026-10-09T12:00:00Z")],
            ReviewRequests = [new("reviewer", "User"), new("custom-bot", "User")] });
        Assert.Equal("them", facts.Turn);
        Assert.Equal("waiting on reviewer", facts.TurnReason);
    }

    [Theory]
    [InlineData(0, "XS")][InlineData(10, "XS")][InlineData(11, "S")][InlineData(100, "S")]
    [InlineData(101, "M")][InlineData(500, "M")][InlineData(501, "L")][InlineData(1000, "L")][InlineData(1001, "XL")]
    public void SizeBoundariesAreExact(int lines, string size) =>
        Assert.Equal(size, Facts(Pr() with { Additions = lines, Deletions = 0 }).Size);

    [Theory]
    [InlineData("viewer", "Claude-Session: https://example/session", "agent")]
    [InlineData("viewer", "Co-Authored-By: Claude <example@example.com>", "agent")]
    [InlineData("viewer", "Generated with Claude Code", "agent")]
    [InlineData("Copilot", "", "agent")]
    [InlineData("dependabot[bot]", "Claude-Session: fake", "bot")]
    [InlineData("viewer", "", "human")]
    public void OriginRecognizesMarkersAndBotPriority(string author, string body, string expected) =>
        Assert.Equal(expected, Facts(Pr() with { Author = author, Body = body }).Origin);

    [Fact]
    public void CommitTrailerIdentifiesAgent()
    {
        var commit = new PrEvent("2026-10-09T00:00:00Z", "viewer", "commit",
            """{"commit":{"message":"Feature\nClaude-Session: example"}}""");
        Assert.Equal("agent", Facts(Pr() with { Events = [commit] }).Origin);
    }

    [Fact]
    public async Task Recorded49HasFourOpenCommentsAndFactSnapshot()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "github-pr-page.json"));
        var reader = new GitHubReader(new HttpClient(new FixtureHandler(fixture)),
            new GitHubTokens(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["GitHub:Token"] = "fake-test-token" }).Build()),
            NullLogger<GitHubReader>.Instance);
        var page = await reader.SearchAsync("example", "is:pr", null, "viewer", default);
        var facts = page.Prs.Select(Facts).ToArray();
        Assert.Equal("me", facts.Single().Turn);
        Assert.Equal("4 open review comments", facts.Single().TurnReason);
        await Verify(facts).UseFileName("RecordedFacts");
    }

    private sealed class FixtureHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
