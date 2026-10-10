using Tangle.Web;
using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Tangle.Web.Tests;

public sealed class EventTests
{
    [Fact]
    public async Task ReconnectReplaysMissedEventsWithIncreasingIds()
    {
        var hub = new EventHub();
        hub.Publish("prs.changed", new { ids = new[] { "one" } });
        hub.Publish("link.changed", new { id = "two" });
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var stream = hub.Subscribe(1, ct.Token).GetAsyncEnumerator(ct.Token);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("link.changed", stream.Current.EventType);
        Assert.Equal("2", stream.Current.EventId);
        hub.Publish("prs.changed", new { ids = new[] { "three" } });
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("3", stream.Current.EventId);
    }

    [Fact]
    public async Task OldOrFutureIdRequiresBoardReload()
    {
        var hub = new EventHub();
        for (var i = 0; i < 260; i++) hub.Publish("prs.changed", new { });
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        foreach (var id in new long[] { 1, 999 })
        {
            await using var stream = hub.Subscribe(id, ct.Token).GetAsyncEnumerator(ct.Token);
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal("board.reload", stream.Current.EventType);
        }
    }

    [Fact]
    public async Task HeartbeatKeepsIdleStreamAlive()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var stream = new EventHub().Subscribe(null, ct.Token, TimeSpan.FromMilliseconds(20)).GetAsyncEnumerator(ct.Token);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("heartbeat", stream.Current.EventType);
        Assert.Null(stream.Current.EventId);
    }

    [Fact]
    public async Task EventEndpointValidatesReconnectAndStreamsReplay()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        client.DefaultRequestHeaders.Add("Last-Event-ID", "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/events")).StatusCode);
        client.DefaultRequestHeaders.Remove("Last-Event-ID");
        client.DefaultRequestHeaders.Add("Last-Event-ID", "0");
        var hub = factory.Services.GetRequiredService<EventHub>();
        hub.Publish("prs.changed", new { ids = new[] { "fixture/repo#1" } });
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, ct.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct.Token));
        var lines = new List<string>();
        while (lines.Count < 5)
        {
            var line = await reader.ReadLineAsync(ct.Token);
            if (string.IsNullOrEmpty(line)) break;
            lines.Add(line);
        }
        Assert.Contains(lines, l => l.Contains("prs.changed"));
        Assert.Contains(lines, l => l.Contains("fixture/repo#1"));
    }
}
