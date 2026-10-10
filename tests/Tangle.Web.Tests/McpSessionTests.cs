using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Tangle.Core;
using Tangle.Data;
using Tangle.Mcp;
using Tangle.Sessions;

namespace Tangle.Web.Tests;

public sealed class McpSessionTests
{
    private static async Task Seed(TangleFactory factory)
    {
        var store = factory.Services.GetRequiredService<SyncStore>();
        var prs = Enumerable.Range(1, 2).Select(n => new PullRequest($"example/repo#{n}", "example/repo", n, "Fixture", "",
            "viewer", true, "OPEN", false, $"feature-{n}", "main", "2026-10-01T00:00:00Z",
            "2026-10-10T00:00:00Z", null, 1, 0, 0, "MERGEABLE", null, null, [], [], [], [], $"hash-{n}", "viewer")).ToArray();
        await store.SaveScopeAsync("involves:viewer org:example", prs, DateTimeOffset.UtcNow.ToString("O"), default);
    }
    [Fact]
    public async Task PersonalKeysAreScopedHashedAndRevocableAndSessionMatches()
    {
        using var factory = new TangleFactory();
        using var browser = factory.LocalClient();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        await Seed(factory);
        var report = new SessionReport("session-one", "/local/transcript", "/local/repo", "git@github.com:example/repo.git", "feature-1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/sessions", report)).StatusCode);
        var created = await (await browser.PostAsJsonAsync("/api/keys", new { name = "Claude" })).Content.ReadFromJsonAsync<CreatedKey>();
        Assert.NotNull(created);
        using var agent = factory.LocalClient();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", created.Key);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("/api/actions/missing/apply", new { confirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("/api/sessions", report)).StatusCode);
        var info = (await factory.Services.GetRequiredService<SessionService>().ListAsync(default)).Single();
        Assert.Equal("example/repo#1", info.PrId);
        Assert.Equal("branch", info.MatchedBy);
        await factory.Services.GetRequiredService<SessionService>().AttachAsync(info.Id, "example/repo#2", default);
        await agent.PostAsJsonAsync("/api/sessions", report);
        Assert.Equal("example/repo#2", (await factory.Services.GetRequiredService<SessionService>().ListAsync(default)).Single().PrId);
        await using var db = await factory.Services.GetRequiredService<Database>().OpenAsync();
        Assert.NotEqual(created.Key, await db.ExecuteScalarAsync<string>("SELECT key_hash FROM personal_key"));
        await browser.DeleteAsync($"/api/keys/{created.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/sessions", report)).StatusCode);
    }
    [Fact]
    public async Task McpHttpListsOnlyEightProposalSafeToolsAndCallsReadAndWrite()
    {
        using var factory = new TangleFactory();
        using var browser = factory.LocalClient();
        await Seed(factory);
        var created = await factory.Services.GetRequiredService<PersonalKeys>().CreateAsync("MCP", default);
        browser.DefaultRequestHeaders.Authorization = new("Bearer", created.Key);
        browser.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        browser.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        browser.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        var initialize = await browser.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "test", version = "1" } }
        });
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        var response = await browser.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("list_prs", text);
        Assert.Contains("queue_action", text);
        Assert.DoesNotContain("\"name\":\"apply", text);
        foreach (var tool in new[] { "list_prs", "get_threads", "get_eval_report" })
        {
            response = await browser.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = tool, arguments = new { } } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("\"isError\":true", await response.Content.ReadAsStringAsync());
        }
        response = await browser.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "add_note", arguments = new { targetRef = "pr:example/repo#1", text = "Remember context" } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Remember context", await response.Content.ReadAsStringAsync());
        await browser.DeleteAsync("/mcp"); // No GitHub writes; transport cleanup only.
    }
    [Fact]
    public async Task TrailerMatchingAndAmbiguousBranchesPreserveManualChoice()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        await Seed(factory);
        await using var db = await factory.Services.GetRequiredService<Database>().OpenAsync();
        await db.ExecuteAsync("UPDATE pr SET body='Claude-Session: cloud-fixture' WHERE id='example/repo#1'");
        var sessions = factory.Services.GetRequiredService<SessionService>();
        await sessions.MatchAsync(default);
        Assert.Equal("trailer", (await sessions.ListAsync(default)).Single().MatchedBy);
        await sessions.AttachAsync("cloud-fixture", "example/repo#2", default);
        await sessions.MatchAsync(default);
        Assert.Equal("example/repo#2", (await sessions.ListAsync(default)).Single().PrId);
        await db.ExecuteAsync("UPDATE pr SET head_ref='feature-1' WHERE id='example/repo#2'");
        var report = await sessions.ReportAsync(new("ambiguous", "/transcript", "/repo", "https://github.com/example/repo.git", "feature-1"), default);
        Assert.Null(report.PrId);
    }
    [Fact]
    public async Task EachToolUsesSharedFixtureStoreAndPreservesCuts()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        await Seed(factory);
        var tools = ActivatorUtilities.CreateInstance<TangleTools>(factory.Services);
        Assert.Contains("example/repo#1", await tools.ListPrs(default));
        Assert.Contains("Fixture", await tools.GetPrContext("example/repo#1", default));
        Assert.Contains("prs", await tools.GetThreads(default));
        Assert.Contains("labelledExamples", await tools.GetEvalReport(default));
        var note = await tools.AddNote("pr:example/repo#1", "note", default);
        Assert.Contains("note", note);
        await tools.ProposeLink("pr:example/repo#1", "pr:example/repo#2", "Shared work", default);
        var sync = factory.Services.GetRequiredService<SyncStore>();
        var link = Assert.Single(await sync.GetStringsAsync());
        Assert.Equal("suggested", link.Status);
        await factory.Services.GetRequiredService<AuthoringStore>().ChangeLinkAsync(link.Id, "cut", null, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.ProposeLink(link.ARef, link.BRef, "Again", default));
        await tools.RecordVerdict("example/repo#1", "fix", .9, 4, "Review asks", "Intent", default);
        Assert.Equal("fix", (await sync.GetBoardAsync()).First().Verdict!.Action);
        await tools.QueueAction("example/repo#1", "comment", "Draft only", null, default);
        Assert.Equal("queued", (await factory.Services.GetRequiredService<ActionStore>().ListAsync(default)).Single().Status);
    }
}
