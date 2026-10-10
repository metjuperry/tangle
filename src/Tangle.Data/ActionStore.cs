using Dapper;
using Tangle.Core;

namespace Tangle.Data;

public sealed class ActionStore(Database database, TimeProvider clock)
{
    private const string Select = """
        SELECT id AS Id,pr_id AS PrId,kind AS Kind,payload AS Payload,status AS Status,created_at AS CreatedAt,
          applied_at AS AppliedAt,result AS Result FROM action
        """;
    public async Task<IReadOnlyList<QueuedAction>> ListAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return (await db.QueryAsync<QueuedAction>(new CommandDefinition(Select + " ORDER BY created_at,id", cancellationToken: ct))).ToArray();
    }
    public async Task<QueuedAction?> GetAsync(string id, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<QueuedAction>(new CommandDefinition(Select + " WHERE id=@id", new { id }, cancellationToken: ct));
    }
    public async Task<QueuedAction> QueueAsync(string prId, string kind, string payload, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM pr WHERE id=@prId AND state='OPEN'",
            new { prId }, cancellationToken: ct)) != 1) throw new ArgumentException("PR must be cached and open.");
        var action = new QueuedAction(Guid.NewGuid().ToString(), prId, kind, payload, "queued", clock.GetUtcNow().ToString("O"), null, null);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO action VALUES(@Id,@PrId,@Kind,@Payload,@Status,@CreatedAt,@AppliedAt,@Result)
            """, action, cancellationToken: ct));
        return action;
    }
    public async Task DismissAsync(string id, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        if (await db.ExecuteAsync(new CommandDefinition("UPDATE action SET status='dismissed' WHERE id=@id AND status='queued'",
            new { id }, cancellationToken: ct)) != 1) throw new InvalidOperationException("Only queued actions can be removed.");
    }
    public async Task<bool> ClaimAsync(string id, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("UPDATE action SET status='applying' WHERE id=@id AND status='queued'",
            new { id }, cancellationToken: ct)) == 1;
    }
    public async Task FinishAsync(string id, bool success, string result, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("UPDATE action SET status=@status,result=@result,applied_at=@at WHERE id=@id",
            new { id, status = success ? "applied" : "failed", result, at = clock.GetUtcNow().ToString("O") }, cancellationToken: ct));
    }
    public async Task<IReadOnlyList<DeckDecision>> DecisionsAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return (await db.QueryAsync<DeckDecision>(new CommandDefinition("""
            SELECT id AS Id,pr_id AS PrId,choice AS Choice,action_id AS ActionId,created_at AS CreatedAt
            FROM decision ORDER BY created_at,id
            """, cancellationToken: ct))).ToArray();
    }
    public async Task<DeckDecision> DecideAsync(string prId, string choice, string? actionId, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM pr WHERE id=@prId AND is_mine=1 AND state='OPEN'",
            new { prId }, tx, cancellationToken: ct)) != 1) throw new ArgumentException("Untangle contains only your open PRs.");
        if (actionId is not null && await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM action WHERE id=@actionId AND pr_id=@prId AND status='queued'", new { actionId, prId }, tx, cancellationToken: ct)) != 1)
            throw new ArgumentException("Queued action does not match this PR.");
        var decision = new DeckDecision(Guid.NewGuid().ToString(), prId, choice, actionId, clock.GetUtcNow().ToString("O"));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO decision(id,pr_id,choice,action_id,created_at)
            VALUES(@Id,@PrId,@Choice,@ActionId,@CreatedAt)
            """, decision, tx, cancellationToken: ct));
        tx.Commit(); return decision;
    }
    public async Task UndoAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction(deferred: false);
        var latest = await db.QuerySingleOrDefaultAsync<DeckDecision>(new CommandDefinition("""
            SELECT id AS Id,pr_id AS PrId,choice AS Choice,action_id AS ActionId,created_at AS CreatedAt
            FROM decision ORDER BY created_at DESC,rowid DESC LIMIT 1
            """, transaction: tx, cancellationToken: ct));
        if (latest is null) throw new InvalidOperationException("No decision to undo.");
        if (latest.ActionId is not null)
        {
            var status = await db.QuerySingleAsync<string>(new CommandDefinition("SELECT status FROM action WHERE id=@id", new { id = latest.ActionId }, tx, cancellationToken: ct));
            if (status is not ("queued" or "dismissed")) throw new InvalidOperationException("An applied or attempted action cannot be undone locally.");
            await db.ExecuteAsync(new CommandDefinition("UPDATE action SET status='dismissed' WHERE id=@id", new { id = latest.ActionId }, tx, cancellationToken: ct));
        }
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM decision WHERE id=@Id", latest, tx, cancellationToken: ct));
        tx.Commit();
    }
}
