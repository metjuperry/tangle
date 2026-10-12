using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Tangle.Core;
using Tangle.Data;
using Tangle.Mcp;
using Tangle.Web;

namespace Tangle.Web.Tests;

public sealed class UserIsolationTests
{
    [Fact]
    public async Task DatabasesKeysEventsMcpAndSessionsArePrivateToTheirUser()
    {
        using var factory = new TangleFactory();
        using var browser = factory.LocalClient();
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        await using (var db = await control.OpenAsync())
            await db.ExecuteAsync("""
                INSERT INTO user(id,github_id,login,created_at,last_seen_at) VALUES
                  ('101',101,'alice','now','now'),('202',202,'bob','now','now');
                """);
        var databases = factory.Services.GetRequiredService<UserDatabases>();
        var resources = factory.Services.GetRequiredService<UserResources>();
        CreatedKey? aliceKey = null;
        foreach (var (id, login) in new[] { ("101", "alice"), ("202", "bob") })
        {
            using var scope = factory.Services.CreateScope();
            await databases.OpenAsync(id);
            scope.ServiceProvider.GetRequiredService<UserContext>().Set(id, login);
            var sync = scope.ServiceProvider.GetRequiredService<SyncStore>();
            await sync.SaveScopeAsync("involves:viewer repo:example/repo", [new PullRequest
            {
                Id = "example/repo#1", RepoId = "example/repo", Number = 1, State = "OPEN",
                Title = login + " private PR", Viewer = "viewer", HeadRef = "feature", BaseRef = "main",
                CreatedAt = "2026-10-01T00:00:00Z", UpdatedAt = "2026-10-09T00:00:00Z", ContentHash = login
            }], DateTimeOffset.UtcNow.ToString("O"), default);
            var keys = scope.ServiceProvider.GetRequiredService<PersonalKeys>();
            var key = await keys.CreateAsync(login, default);
            if (id == "101") aliceKey = key;
            Assert.Single(await keys.ListAsync(default));
            var authoring = scope.ServiceProvider.GetRequiredService<AuthoringStore>();
            await authoring.NoteAsync("pr:example/repo#1", login + " private note", default);
            var tools = ActivatorUtilities.CreateInstance<TangleTools>(scope.ServiceProvider);
            var result = await tools.GetPrContext("example/repo#1", default);
            Assert.Contains(login + " private", result);
            Assert.DoesNotContain((id == "101" ? "bob" : "alice") + " private", result);
            Assert.Contains(login + " private PR", await tools.ListPrs(default));
            var actions = scope.ServiceProvider.GetRequiredService<ActionStore>();
            await actions.QueueAsync("example/repo#1", "comment", "{\"text\":\"" + login + "\"}", default);
            Assert.Single(await actions.ListAsync(default));
            await Assert.ThrowsAsync<GitHubAccessExpiredException>(() =>
                scope.ServiceProvider.GetRequiredService<Tangle.GitHub.GitHubTokens>().GetAsync("example", default));
        }
        Assert.NotSame(databases.GetOpened("101"), databases.GetOpened("202"));
        Assert.NotSame(resources.Events("101"), resources.Events("202"));
        Assert.NotSame(resources.Sync("101"), resources.Sync("202"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        resources.Events("101").Publish("alice-only", new { });
        resources.Events("202").Publish("bob-only", new { });
        await using var events = resources.Events("202").Subscribe(0, timeout.Token).GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("bob-only", events.Current.EventType);
        using var agent = factory.LocalClient();
        agent.DefaultRequestHeaders.Authorization = new("Bearer", aliceKey!.Key);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("/api/board")).StatusCode);
        agent.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        agent.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        agent.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        var response = await agent.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "get_pr_context", arguments = new { prId = "example/repo#1" } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("alice private", json);
        Assert.DoesNotContain("bob private", json);
        var session = await agent.PostAsJsonAsync("/api/sessions", new Tangle.Sessions.SessionReport(
            "alice-session", "/local/alice", "/local/repo", "https://github.com/example/repo.git", "feature"));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            await databases.OpenAsync("202");
            scope.ServiceProvider.GetRequiredService<UserContext>().Set("202", "bob");
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<Tangle.Sessions.SessionService>().ListAsync(default));
            Assert.Single(await scope.ServiceProvider.GetRequiredService<ActionStore>().ListAsync(default));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                scope.ServiceProvider.GetRequiredService<PersonalKeys>().RevokeAsync(aliceKey.Id, default));
        }
        await using (var db = await control.OpenAsync())
            await db.ExecuteAsync("UPDATE user SET disabled=1 WHERE id='101'");
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/mcp", new { })).StatusCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => databases.OpenAsync("101"));
    }

    [Fact]
    public async Task LegacyKeyHashesAreCopiedIdempotentlyWithoutDeletingOriginals()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<Database>();
        await using (var db = await source.OpenAsync())
            await db.ExecuteAsync("INSERT INTO personal_key VALUES('legacy','Old agent','existing-hash','now',NULL)");
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        await control.ImportKeysAsync(source, "local");
        await control.ImportKeysAsync(source, "local");
        await using var old = await source.OpenAsync();
        Assert.Equal(1, await old.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM personal_key"));
        await using var destination = await control.OpenAsync();
        Assert.Equal("existing-hash", await destination.ExecuteScalarAsync<string>("SELECT key_hash FROM personal_key WHERE id='legacy' AND user_id='local'"));
    }
}
