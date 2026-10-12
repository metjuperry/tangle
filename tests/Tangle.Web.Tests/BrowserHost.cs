using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;
using Tangle.Core;
using Tangle.Data;
using Tangle.GitHub;
using Microsoft.Data.Sqlite;

namespace Tangle.Web.Tests;

// Test-only host: fixture SQLite and a fake GitHub reader; never enabled in the production host.
public static class BrowserHost
{
    public static async Task Main()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Tangle.Host")));
        using var factory = new BrowserFactory();
        factory.UseKestrel(5203);
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<SyncStore>();
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "github-pr-page.json"));
        var github = new GitHubReader(new HttpClient(new FixtureHandler(fixture)),
            new GitHubTokens(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["GitHub:Token"] = "fixture-not-a-secret" }).Build()),
            NullLogger<GitHubReader>.Instance);
        var recorded = (await github.SearchAsync("example", "is:pr", null, "viewer", default)).Prs.Single();
        var first = recorded with { Body = "A recorded review fixture. Related to example/tools#195.\nClaude-Session: fixture",
            ContentHash = "fixture-original" };
        var prs = new List<PullRequest> { first };
        using var batchFixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "scaffold-batches.json")));
        foreach (var p in batchFixture.RootElement.EnumerateArray().Where(p => p.GetProperty("title").GetString()!.Contains("scaffold applier")))
        {
            var number = p.GetProperty("number").GetInt32();
            prs.Add(first with { Id = $"example/metadata#{number}", RepoId = "example/metadata", Number = number,
                Title = p.GetProperty("title").GetString()!, CreatedAt = p.GetProperty("createdAt").GetString()!,
                Author = "person-1", IsMine = false, Body = "", Threads = [], Events = [], Files = [],
                HeadRef = $"scaffold-{number}", ContentHash = $"scaffold-{number}" });
        }
        for (var i = 0; i < 11; i++)
        {
            var repo = i < 4 ? "example/tools" : i < 8 ? "example/templates" : "example/repo";
            var number = 195 + i;
            prs.Add(first with { Id = $"{repo}#{number}", RepoId = repo, Number = number,
                Title = new[] { "Validate connector metadata", "Repair a cloud flow template", "Normalize exported solution files" }[i % 3],
                Body = "", Threads = [], Files = [], Events = [], HeadRef = $"feature-{number}",
                CreatedAt = i % 2 == 0 ? "2026-08-01T12:00:00Z" : "2026-10-01T12:00:00Z",
                Author = i % 3 == 0 ? "viewer" : "person-1", IsMine = i % 3 == 0,
                ContentHash = $"fixture-{number}" });
        }
        await store.SaveSettingsAsync(new([new("example")], []));
        await store.SaveScopeAsync("involves:viewer org:example", prs, DateTimeOffset.UtcNow.ToString("O"), default);
        factory.Services.GetRequiredService<FixtureReader>().Pr = first;
        Console.WriteLine("Fixture board ready: 45 PRs; no GitHub or AI network calls.");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private sealed class BrowserFactory : WebApplicationFactory<Program>
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"tangle-browser-{Guid.NewGuid()}");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Tangle.Host")));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tangle:DevelopmentKey"] = "tangle-browser-test-key-not-a-secret",
                ["Ai:Enabled"] = "false",
                ["Ai:JevEnabled"] = "false",
                ["Tangle:Address"] = "http://127.0.0.1:5203",
                ["Tangle:DatabasePath"] = Path.Combine(directory, "fixture.db")
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitHubReader>();
                services.AddSingleton<FixtureReader>();
                services.AddSingleton<IGitHubReader>(s => s.GetRequiredService<FixtureReader>());
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory))
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(directory, recursive: true);
            }
        }
    }
    private sealed class FixtureReader : IGitHubReader
    {
        public PullRequest? Pr { get; set; }
        public Task<string> GetViewerAsync(string owner, CancellationToken ct) => Task.FromResult("viewer");
        public Task<PrPage> SearchAsync(string owner, string query, string? after, string viewer, CancellationToken ct) =>
            Task.FromResult(new PrPage(query.Contains("is:closed") || Pr is null ? [] :
                [Pr with { Title = "Updated from fixture sync", ContentHash = "fixture-updated" }], false, null, 1, 4999, DateTimeOffset.UtcNow.ToString("O")));
    }
    private sealed class FixtureHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
