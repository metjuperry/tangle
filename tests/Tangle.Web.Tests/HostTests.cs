using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tangle.Core;
using Tangle.Data;
using System.Text.Json.Nodes;

namespace Tangle.Web.Tests;

public sealed class TangleFactory : WebApplicationFactory<Program>
{
    public const string Key = "tangle-api-test-key-not-a-secret-123";
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"tangle-api-{Guid.NewGuid()}");
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Tangle:DevelopmentKey"] = Key,
                ["Tangle:Address"] = "http://127.0.0.1:5202",
                ["Tangle:DatabasePath"] = Path.Combine(directory, "test.db")
            }));
    public HttpClient LocalClient() => CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1:5202") });
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

public sealed class HostTests(TangleFactory factory) : IClassFixture<TangleFactory>
{
    [Fact]
    public async Task ProbeAndEmbeddedShellAreAvailable()
    {
        using var client = factory.LocalClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Contains("<title>Tangle</title>", await client.GetStringAsync("/"));
    }
    [Fact]
    public async Task KeyIsRequiredAndLoginUsesStrictHttpOnlyCookie()
    {
        using var client = factory.LocalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { key = "wrong" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { key = TangleFactory.Key });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth")).StatusCode);
    }
    [Theory]
    [InlineData("evil.example:5202", null)]
    [InlineData("127.0.0.1:5202", "https://evil.example")]
    [InlineData("localhost:5202", null)]
    public async Task ForeignHostAndOriginAreRejected(string host, string? origin)
    {
        using var client = factory.LocalClient();
        client.DefaultRequestHeaders.Host = host;
        if (origin is not null) client.DefaultRequestHeaders.Add("Origin", origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public async Task SettingsBoardAndSyncRequireAuthentication()
    {
        using var client = factory.LocalClient();
        foreach (var path in new[] { "/api/settings", "/api/board", "/api/events", "/api/prs/example/repo/1" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/sync", null)).StatusCode);
    }

    [Fact]
    public async Task SettingsValidateScopeAndKeepAiDisabled()
    {
        using var client = factory.LocalClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        var good = new { owners = new[] { new { id = "example", kind = "org", watched = true } },
            repos = new[] { new { id = "example/repo", watched = true, aiAllowed = false } } };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", good)).StatusCode);
        Assert.Contains("example/repo", await client.GetStringAsync("/api/settings"));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", new
        {
            owners = Array.Empty<object>(), repos = new[] { new { id = "example/repo", watched = true, aiAllowed = true } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/settings", new
        {
            owners = new[] { new { id = "example is:closed", kind = "org", watched = true } }, repos = Array.Empty<object>()
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/board")).StatusCode);
    }

    [Fact]
    public async Task BoardAndCardContextExposeFactsBatchesAndStrings()
    {
        using var isolated = new TangleFactory();
        using var client = isolated.LocalClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        var store = isolated.Services.GetRequiredService<SyncStore>();
        var prs = Enumerable.Range(1, 5).Select(n => new PullRequest(
            $"fixture/repo#{n}", "fixture/repo", n, $"feat: scaffold applier for pp-{n}", n == 1 ? "#2" : "",
            "viewer", true, "OPEN", false, $"feature-{n}", "main", "2026-10-01T00:00:00Z",
            "2026-10-09T00:00:00Z", null, 10, 0, 1, "MERGEABLE", null, null, [], [], [
                new($"thread-{n}", null, false, "reviewer", "2026-10-09T00:00:00Z", "fix")
            ], [], $"hash-{n}", "viewer")).ToArray();
        await store.SaveScopeAsync("involves:viewer org:fixture", prs, "2026-10-10T00:00:00Z", default);
        var board = JsonNode.Parse(await client.GetStringAsync("/api/board"))!;
        Assert.Equal(5, board["cards"]!.AsArray().Count);
        Assert.Equal("me", board["cards"]![0]!["facts"]!["turn"]!.GetValue<string>());
        Assert.Single(board["batches"]!.AsArray());
        Assert.Single(board["strings"]!.AsArray());
        Assert.Contains("1 open review comment", await client.GetStringAsync("/api/prs/fixture/repo/1"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/prs/fixture/repo/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/debug/prs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/debug/prs")).StatusCode);
    }
}
