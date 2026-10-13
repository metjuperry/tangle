using System.Threading.Channels;
using Tangle.Ai;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Host;

public sealed class UserAiQueue(IServiceScopeFactory scopes, ILogger<UserAiQueue> logger) : BackgroundService
{
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>();
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private readonly object gate = new();
    public void Enqueue(string userId)
    {
        lock (gate)
            if (pending.Add(userId)) queue.Writer.TryWrite(userId);
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var id in queue.Reader.ReadAllAsync(ct))
        {
            lock (gate) pending.Remove(id);
            try
            {
                using var scope = scopes.CreateScope();
                using var lease = scope.ServiceProvider.GetRequiredService<UserDatabases>().Lease(id);
                await scope.ServiceProvider.GetRequiredService<UserDatabases>().OpenAsync(id, ct);
                scope.ServiceProvider.GetRequiredService<UserContext>().Set(id, id);
                await scope.ServiceProvider.GetRequiredService<AiWorker>().RunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "AI pass failed for user {UserId}", id); }
        }
    }
}
public sealed class ScopedAiQueue(UserAiQueue queue, IUserContext user, Tangle.Web.UserAiAccess access) : IAiQueue
{
    public void Enqueue() { if (access.Enabled) queue.Enqueue(user.UserId); }
}
