using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Tangle.GitHub;

namespace Tangle.Core.Tests;

public sealed class GitHubReaderTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "github-pr-page.json"));
    private static GitHubReader Reader(HttpMessageHandler handler) => new(new HttpClient(handler),
        new GitHubTokens(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["GitHub:Token"] = "fake-test-token" }).Build()),
        NullLogger<GitHubReader>.Instance);

    [Fact]
    public async Task RecordedResponseMapsIntoStableCache()
    {
        var reader = Reader(new Handler(Fixture));
        var first = await reader.SearchAsync("example", "is:pr", null, "viewer", default);
        var second = await reader.SearchAsync("example", "is:pr", null, "viewer", default);
        var pr = Assert.Single(first.Prs);
        Assert.Equal("example/repo", pr.RepoId);
        Assert.True(pr.IsMine);
        Assert.NotEmpty(pr.Files);
        Assert.NotEmpty(pr.Events);
        Assert.Equal(pr.ContentHash, second.Prs.Single().ContentHash);
        await Verify(new { pr.Id, pr.Title, pr.Author, pr.State, pr.IsDraft, pr.ChangedFiles,
            Files = pr.Files.Count, Events = pr.Events.Count, Threads = pr.Threads.Count });
    }

    [Fact]
    public async Task UnlinkedCommitDisplaysNameWithoutInventingGitHubActor()
    {
        var fixture = JsonNode.Parse(Fixture)!;
        var commit = fixture["data"]!["search"]!["nodes"]![0]!["timelineItems"]!["nodes"]!.AsArray()
            .First(n => n?["__typename"]?.GetValue<string>() == "PullRequestCommit")!["commit"]!;
        commit["author"] = JsonNode.Parse("""{"name":"Example Author","user":null}""");
        var page = await Reader(new Handler(fixture.ToJsonString())).SearchAsync("example", "is:pr", null, "viewer", default);
        var activity = page.Prs.Single().Events.First(e => e.Kind == "commit");
        Assert.Null(activity.Actor);
        Assert.Equal("Example Author (unlinked commit author)", activity.ActorDisplay);
        Assert.Equal("viewer", (activity with { Actor = "viewer" }).ActorDisplay);
    }

    [Fact]
    public async Task GraphQlErrorsNeverBecomeSuccessOrPartialData()
    {
        var reader = Reader(new Handler("""{"errors":[{"message":"Access denied"}],"data":null}"""));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.SearchAsync("example", "is:pr", null, "viewer", default));
        Assert.Contains("Access denied", error.Message);
    }

    [Fact]
    public async Task NestedFileConnectionIsNotSilentlyTruncated()
    {
        var fixture = JsonNode.Parse(Fixture)!;
        fixture["data"]!["search"]!["nodes"]![0]!["files"]!["pageInfo"] =
            JsonNode.Parse("""{"hasNextPage":true,"endCursor":"file-next"}""");
        var continuation = """
            {"data":{"node":{"files":{"nodes":[{"path":"extra/file.cs"}],
              "pageInfo":{"hasNextPage":false,"endCursor":null}}},
              "rateLimit":{"cost":1,"remaining":4900,"resetAt":"2026-10-10T00:00:00Z"}}}
            """;
        var handler = new Handler(fixture.ToJsonString(), continuation);
        var page = await Reader(handler).SearchAsync("example", "is:pr", null, "viewer", default);
        Assert.Contains("extra/file.cs", page.Prs.Single().Files);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task EmptyIncrementalWindowUsesOnlyCheapSearch()
    {
        var response = """
            {"data":{"search":{"issueCount":0,"nodes":[],"pageInfo":{"hasNextPage":false,"endCursor":null}},
            "rateLimit":{"cost":1,"remaining":4900,"resetAt":"2026-10-10T00:00:00Z"}}}
            """;
        var handler = new Handler(response);
        var page = await Reader(handler).SearchAsync("example", "is:pr updated:>=2026-10-09T00:00:00Z", null, "viewer", default);
        Assert.Empty(page.Prs);
        Assert.Equal(1, page.Cost);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("ChangedPrs", handler.LastRequest);
        Assert.DoesNotContain("reviewThreads", handler.LastRequest);
    }
    private sealed class Handler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> queue = new(responses);
        public int Calls { get; private set; }
        public string LastRequest { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            LastRequest = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(queue.Count > 1 ? queue.Dequeue() : queue.Peek(), Encoding.UTF8, "application/json")
            };
        }
    }
}
