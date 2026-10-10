using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tangle.Data;
using Tangle.Core;

namespace Tangle.Web.Tests;

public sealed class AuthoringTests
{
    [Fact]
    public async Task AuthoringRoundTripSurvivesSyncAndReportsStickyMergedProgress()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        var store = factory.Services.GetRequiredService<SyncStore>();
        PullRequest Pr(int n, string state = "OPEN") => new($"example/repo#{n}", "example/repo", n,
            "Fixture", "", "viewer", true, state, false, $"feature-{n}", "main", "2026-10-01T00:00:00Z",
            "2026-10-09T00:00:00Z", null, 1, 0, 0, "MERGEABLE", null, null, [], [], [], [], state, "viewer");
        await store.SaveScopeAsync("involves:viewer org:example", [Pr(1), Pr(2)], "now", default);
        var stickyResponse = await client.PostAsJsonAsync("/api/stickies", new { title = "Release", color = "blue" });
        Assert.Equal(HttpStatusCode.OK, stickyResponse.StatusCode);
        var stickyId = JsonNode.Parse(await stickyResponse.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
        var tied = await client.PostAsJsonAsync("/api/links", new { aRef = "pr:example/repo#1", bRef = $"sticky:{stickyId}", label = "business goal" });
        Assert.Equal(HttpStatusCode.OK, tied.StatusCode);
        var linkId = JsonNode.Parse(await tied.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/notes", new { targetRef = $"link:{linkId}", text = "Remember why" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/notes", new { targetRef = $"sticky:{stickyId}", text = "Sticky context" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/notes", new { targetRef = "pr:example/repo#1", text = "PR context" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/positions/pr:example%2Frepo%231", new { x = 40, y = 90 })).StatusCode);
        await store.SaveScopeAsync("scope", [Pr(1, "MERGED"), Pr(2)], "later", default);
        var board = JsonNode.Parse(await client.GetStringAsync("/api/board"))!;
        Assert.Equal(1, board["stickies"]![0]!["merged"]!.GetValue<int>());
        Assert.Contains("Remember why", board["notes"]!.ToJsonString());
        Assert.Equal(3, board["notes"]!.AsArray().Count);
        Assert.Single(board["positions"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/links/{linkId}", new { status = "cut" })).StatusCode);
        await store.SaveScopeAsync("scope", [Pr(1), Pr(2)], "again", default);
        board = JsonNode.Parse(await client.GetStringAsync("/api/board"))!;
        Assert.Empty(board["strings"]!.AsArray());
        Assert.Single(board["cutStrings"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/links/{linkId}", new { status = "tied" })).StatusCode);
        Assert.Single((JsonNode.Parse(await client.GetStringAsync("/api/board"))!)["strings"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/stickies/{stickyId}", new { title = "Renamed release", color = "green" })).StatusCode);
        board = JsonNode.Parse(await client.GetStringAsync("/api/board"))!;
        Assert.Equal("Renamed release", board["stickies"]![0]!["title"]!.GetValue<string>());
        Assert.Equal("green", board["stickies"]![0]!["color"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/stickies/{stickyId}", new { title = "Renamed release", color = "green", archived = true })).StatusCode);
        Assert.Empty((JsonNode.Parse(await client.GetStringAsync("/api/board"))!)["stickies"]!.AsArray());
    }
    [Fact]
    public async Task WritesRejectMissingAuthInvalidReferencesAndInvalidColors()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/notes", new { targetRef = "pr:missing", text = "note" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/notes", new { targetRef = "pr:missing", text = "note" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/stickies", new { title = "title", color = "invalid" })).StatusCode);
    }
}
