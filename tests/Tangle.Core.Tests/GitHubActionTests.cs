using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tangle.Core;
using Tangle.GitHub;

namespace Tangle.Core.Tests;

public sealed class GitHubActionTests
{
    [Theory]
    [InlineData("close")][InlineData("merge")]
    public async Task CannotCloseOrMergeSomeoneElsesPr(string kind)
    {
        var handler = new Handler();
        var client = Client(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyAsync("example/repo",
            new("id", new string('a', 40), "OPEN", "other", "viewer"), kind, new(HeadSha: new string('a', 40)), default));
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task MergePinsExpectedHeadAndRejectsChangedHeadBeforeMutation()
    {
        var handler = new Handler(); var client = Client(handler);
        var target = new GitHubActionTarget("id", new string('a', 40), "OPEN", "viewer", "viewer");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ApplyAsync("example/repo", target, "merge", new(HeadSha: new string('b', 40)), default));
        Assert.Equal(0, handler.Calls);
        await client.ApplyAsync("example/repo", target, "merge", new(HeadSha: target.HeadSha), default);
        Assert.Contains("expectedHeadOid", handler.Query);
        Assert.Contains(target.HeadSha, handler.Query);
    }
    private static GitHubActions Client(Handler handler) => new(new HttpClient(handler),
        new GitHubTokens(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GitHub:Token"] = "fake" }).Build()));
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Query { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Query = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"mergePullRequest":{"pullRequest":{"url":"https://example.test"}}}}""") };
        }
    }
}
