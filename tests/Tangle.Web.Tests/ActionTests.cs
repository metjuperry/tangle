using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tangle.Core;
using Tangle.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Tangle.Web.Tests;

public sealed class ActionTests
{
    private static async Task Seed(ActionStore actions, SyncStore sync)
    {
        var pr = new PullRequest("fixture/sandbox#1", "fixture/sandbox", 1, "Fixture", "", "viewer", true, "OPEN", false,
            "feature", "main", "2026-10-01T00:00:00Z", "2026-10-10T00:00:00Z", null, 1, 0, 0, "MERGEABLE", null, null,
            [], [], [], [], "hash", "viewer");
        await sync.SaveScopeAsync("scope", [pr], "now", default);
    }
    [Fact]
    public async Task QueueDecisionUndoAndSandboxApplyAreExplicitAndExactlyOnce()
    {
        var github = new FakeActions();
        using var baseFactory = new TangleFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Apply:Enabled"] = "true", ["Apply:SandboxRepo"] = "fixture/sandbox" }));
            builder.ConfigureServices(services => { services.RemoveAll<IGitHubActions>(); services.AddSingleton<IGitHubActions>(github); });
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1:5202") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        var store = factory.Services.GetRequiredService<ActionStore>();
        await Seed(store, factory.Services.GetRequiredService<SyncStore>());
        var action = await store.QueueAsync("fixture/sandbox#1", "comment", System.Text.Json.JsonSerializer.Serialize(new ActionPayload("Exact text")), default);
        await store.DecideAsync(action.PrId, "fix", action.Id, default);
        await store.UndoAsync(default);
        Assert.Equal("dismissed", (await store.GetAsync(action.Id, default))!.Status);
        action = await store.QueueAsync("fixture/sandbox#1", "comment", System.Text.Json.JsonSerializer.Serialize(new ActionPayload("Exact text")), default);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/actions/{action.Id}/apply", new { confirmed = false })).StatusCode);
        Assert.Equal(0, github.Applied);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/actions/{action.Id}/apply", new { confirmed = true })).StatusCode);
        Assert.Equal("Exact text", github.Text);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/actions/{action.Id}/apply", new { confirmed = true })).StatusCode);
        Assert.Equal(1, github.Applied);
        var mergeResponse = await client.PostAsJsonAsync("/api/actions", new
        {
            prId = "fixture/sandbox#1", kind = "merge", payload = new { headSha = new string('b', 40) }
        });
        Assert.Equal(HttpStatusCode.OK, mergeResponse.StatusCode);
        var merge = await mergeResponse.Content.ReadFromJsonAsync<QueuedAction>();
        var payload = System.Text.Json.JsonSerializer.Deserialize<ActionPayload>(merge!.Payload);
        Assert.Equal(new string('a', 40), payload!.HeadSha);
        Assert.Equal(1, github.Applied); // Queueing inspects but never mutates.
    }
    [Fact]
    public async Task DisabledApplyNeverCallsGitHub()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        var store = factory.Services.GetRequiredService<ActionStore>();
        await Seed(store, factory.Services.GetRequiredService<SyncStore>());
        var action = await store.QueueAsync("fixture/sandbox#1", "close", "{}", default);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/actions/{action.Id}/apply", new { confirmed = true })).StatusCode);
        Assert.Equal("queued", (await store.GetAsync(action.Id, default))!.Status);
    }
    private sealed class FakeActions : IGitHubActions
    {
        public int Applied { get; private set; }
        public string? Text { get; private set; }
        public Task<GitHubActionTarget> InspectAsync(string repo, int number, CancellationToken ct) => Task.FromResult(new GitHubActionTarget("id", new string('a', 40), "OPEN", "viewer", "viewer"));
        public Task<string> ApplyAsync(string repo, GitHubActionTarget target, string kind, ActionPayload payload, CancellationToken ct)
        { Applied++; Text = payload.Text; return Task.FromResult("fixture result"); }
    }
}
