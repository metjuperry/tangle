using Dapper;
using Tangle.Core;

namespace Tangle.Data;

public sealed class ExplanationStore(Database database) : IExplanationStore
{
    public async Task<UnexplainedVerdict?> UnexplainedAsync(string prId, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT input_hash AS InputHash,action AS Action,confidence AS Confidence,priority AS Priority,intent AS Intent
            FROM verdict WHERE pr_id=@prId AND decider='jev' ORDER BY created_at DESC,rowid DESC LIMIT 1
            """, new { prId }, cancellationToken: ct));
        return row is null || row.Intent is not null ? null :
            new(row.InputHash, new(Enum.Parse<NextAction>(row.Action, true), row.Confidence, row.Priority, null, null));
    }
    public async Task SaveAsync(string prId, string inputHash, PrJudgment judgment, AiReservation reservation, AiUsage usage, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE verdict SET reason=@Reason,intent=@Intent WHERE pr_id=@prId AND input_hash=@inputHash AND decider='jev'
              AND EXISTS(SELECT 1 FROM pr p JOIN repo r ON r.id=p.repo_id WHERE p.id=@prId AND r.ai_allowed=1 AND r.watched=1);
            UPDATE ai_call SET status='completed',cost=@Cost,input_tokens=@InputTokens,output_tokens=@OutputTokens,
              latency_ms=@LatencyMs,outcome='Written explanation prepared' WHERE id=@id
            """, new { prId, inputHash, judgment.Reason, judgment.Intent, usage.Cost, usage.InputTokens,
                usage.OutputTokens, usage.LatencyMs, id = reservation.Id }, tx, cancellationToken: ct));
        tx.Commit();
    }
    private sealed record Row(string InputHash, string Action, double Confidence, int Priority, string? Intent)
    {
        public Row() : this("", "", 0, 0, null) { }
    }
}
