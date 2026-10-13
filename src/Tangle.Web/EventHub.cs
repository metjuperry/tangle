using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Tangle.Core;

namespace Tangle.Web;

public sealed record LiveEvent(long Id, string Type, string Data);

public sealed class EventHub : ILiveEvents
{
    private const int Capacity = 256;
    private readonly object gate = new();
    private readonly Queue<LiveEvent> history = new();
    private readonly HashSet<Channel<LiveEvent>> subscribers = [];
    private long nextId;
    public void Close()
    {
        lock (gate)
        {
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            history.Clear();
        }
    }

    public void Publish(string type, object data)
    {
        lock (gate)
        {
            var item = new LiveEvent(++nextId, type, JsonSerializer.Serialize(data));
            history.Enqueue(item);
            while (history.Count > Capacity) history.Dequeue();
            foreach (var subscriber in subscribers)
                if (!subscriber.Writer.TryWrite(item)) subscriber.Writer.TryComplete();
        }
    }

    public async IAsyncEnumerable<SseItem<string>> Subscribe(long? lastId,
        [EnumeratorCancellation] CancellationToken ct, TimeSpan? heartbeat = null)
    {
        var channel = Channel.CreateBounded<LiveEvent>(Capacity + 1);
        lock (gate)
        {
            if (lastId.HasValue)
            {
                if (lastId > nextId || (history.Count > 0 && lastId < history.Peek().Id - 1))
                    channel.Writer.TryWrite(new(nextId, "board.reload", "{}"));
                else
                    foreach (var item in history.Where(e => e.Id > lastId))
                        channel.Writer.TryWrite(item);
            }
            subscribers.Add(channel);
        }
        try
        {
            var interval = heartbeat ?? TimeSpan.FromSeconds(30);
            var waiting = channel.Reader.WaitToReadAsync(ct).AsTask();
            while (!ct.IsCancellationRequested)
            {
                var tick = Task.Delay(interval, ct);
                if (await Task.WhenAny(waiting, tick) == tick)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return new SseItem<string>("{}", "heartbeat");
                    continue;
                }
                if (!await waiting)
                {
                    // A slow consumer must refetch rather than silently lose changes.
                    yield return new SseItem<string>("{}", "board.reload");
                    yield break;
                }
                while (channel.Reader.TryRead(out var item))
                    yield return new SseItem<string>(item.Data, item.Type) { EventId = item.Id.ToString() };
                waiting = channel.Reader.WaitToReadAsync(ct).AsTask();
            }
        }
        finally
        {
            lock (gate) subscribers.Remove(channel);
            channel.Writer.TryComplete();
        }
    }
}
