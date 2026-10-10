using Dapper;
using Tangle.Core;

namespace Tangle.Data;

public sealed partial class SyncStore
{
    public async Task<string?> GetLastSyncAsync(CancellationToken ct = default)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT MIN(last_run_at) FROM sync_state", cancellationToken: ct));
    }

    public async Task<CardContext?> GetCardContextAsync(string id, CancellationToken ct = default)
    {
        var card = (await GetBoardAsync(includeClosed: true, ct: ct)).FirstOrDefault(c => c.Id == id);
        if (card is null) return null;
        await using var db = await database.OpenAsync(ct);
        var body = await db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT body FROM pr WHERE id=@id", new { id }, cancellationToken: ct));
        var threads = await db.QueryAsync<ReviewThread>(new CommandDefinition("""
            SELECT id AS Id,path AS Path,is_resolved AS IsResolved,last_actor AS LastActor,last_at AS LastAt,excerpt AS Excerpt
              FROM review_thread WHERE pr_id=@id AND is_resolved=0 ORDER BY last_at
            """, new { id }, cancellationToken: ct));
        var events = await db.QueryAsync<PrEvent>(new CommandDefinition("""
            SELECT at AS At,actor AS Actor,kind AS Kind,detail AS Detail FROM pr_event WHERE pr_id=@id ORDER BY at DESC LIMIT 30
            """, new { id }, cancellationToken: ct));
        var strings = (await GetStringsAsync(ct)).Where(l => l.ARef == $"pr:{id}" || l.BRef == $"pr:{id}").ToArray();
        return new(card, body, threads.ToArray(), events.ToArray(), strings);
    }
}

public sealed record CardContext(BoardCard Card, string Body, IReadOnlyList<ReviewThread> Threads,
    IReadOnlyList<PrEvent> Events, IReadOnlyList<BoardString> Strings);
