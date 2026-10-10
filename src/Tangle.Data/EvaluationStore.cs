using Dapper;
using Tangle.Core;

namespace Tangle.Data;

public sealed class EvaluationStore(Database database, TimeProvider clock) : IEvaluationStore
{
    public async Task<EvaluationData> ReadAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        var examples = new List<EvaluationExample>();
        var decisions = await db.QueryAsync<DecisionRow>(new CommandDefinition("""
            SELECT id AS Id,pr_id AS PrId,choice AS Choice,created_at AS At FROM decision ORDER BY created_at,id
            """, transaction: tx, cancellationToken: ct));
        var snoozes = 0;
        foreach (var decision in decisions)
        {
            if (decision.Choice == "snooze") { snoozes++; continue; }
            var predictions = await db.QueryAsync<VerdictRow>(new CommandDefinition("""
                SELECT decider AS Decider,action AS Action,confidence AS Confidence,input_json AS InputJson
                FROM verdict WHERE pr_id=@PrId AND created_at<=@At ORDER BY created_at DESC,rowid DESC
                """, decision, tx, cancellationToken: ct));
            var latest = predictions.GroupBy(p => p.Decider).Select(g => g.First()).ToArray();
            if (latest.Length == 0) examples.Add(new(decision.Id, "verdict", decision.PrId, decision.Choice, "unscored", null, null, null, decision.At));
            foreach (var prediction in latest)
                examples.Add(new(decision.Id, "verdict", decision.PrId, decision.Choice, prediction.Decider,
                    prediction.Action, prediction.Confidence, prediction.InputJson, decision.At));
        }
        var links = await db.QueryAsync<LinkRow>(new CommandDefinition("""
            SELECT id AS Id,a_ref AS ARef,b_ref AS BRef,status AS Status,decided_at AS At FROM link
            WHERE status IN ('tied','cut') AND decided_at IS NOT NULL
              AND EXISTS(SELECT 1 FROM pair_prediction p WHERE p.a_ref=link.a_ref AND p.b_ref=link.b_ref)
            ORDER BY decided_at,id
            """, transaction: tx, cancellationToken: ct));
        foreach (var link in links)
        {
            var predictions = await db.QueryAsync<PairRow>(new CommandDefinition("""
                SELECT decider AS Decider,related AS Related,input_json AS InputJson
                FROM pair_prediction WHERE a_ref=@ARef AND b_ref=@BRef AND created_at<=@At
                  AND (input_json IS NOT NULL OR id LIKE 'legacy:%') ORDER BY created_at DESC,rowid DESC
                """, link, tx, cancellationToken: ct));
            foreach (var prediction in predictions.GroupBy(p => p.Decider).Select(g => g.First()))
                examples.Add(new(link.Id, "pair", $"{link.ARef}\n{link.BRef}", link.Status == "tied" ? "related" : "unrelated",
                    prediction.Decider, prediction.Related > .75 ? "related" : "unrelated", prediction.Related,
                    prediction.InputJson, link.At));
        }
        var replays = await db.QueryAsync<EvaluationPrediction>(new CommandDefinition("""
            SELECT example_id AS ExampleId,decider AS Decider,predicted AS Predicted,probability AS Probability,
              cost AS Cost,latency_ms AS LatencyMs FROM evaluation_result
            """, transaction: tx, cancellationToken: ct));
        var calls = await db.QueryAsync<AiUsageRecord>(new CommandDefinition("""
            SELECT decider AS Decider,cost AS Cost,latency_ms AS LatencyMs FROM ai_call WHERE status='completed'
            """, transaction: tx, cancellationToken: ct));
        tx.Commit();
        return new(examples, replays.ToArray(), calls.ToArray(), snoozes);
    }
    public async Task<bool> MayReplayAsync(EvaluationExample example, CancellationToken ct)
    {
        if (example.InputJson is null) return false;
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var refs = example.Kind == "verdict"
            ? PrRefs(System.Text.Json.JsonSerializer.Deserialize<PrContext>(example.InputJson, options)!)
            : PairRefs(System.Text.Json.JsonSerializer.Deserialize<PairContext>(example.InputJson, options)!);
        await using var db = await database.OpenAsync(ct);
        foreach (var reference in refs.Where(r => r.StartsWith("pr:")))
            if (await db.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM pr p JOIN repo r ON r.id=p.repo_id WHERE p.id=@id AND r.ai_allowed=1 AND r.watched=1
                  AND NOT EXISTS(SELECT 1 FROM organization o WHERE lower(o.id)=lower(substr(r.id,1,instr(r.id,'/')-1)) AND o.watched=0)
                """, new { id = reference[3..] }, cancellationToken: ct)) != 1) return false;
        return true;
    }
    private static string[] PrRefs(PrContext pr) => new[] { pr.Ref }.Concat(pr.Links.Select(l => l.Ref)).ToArray();
    private static string[] PairRefs(PairContext pair) => new[] { pair.ARef, pair.BRef }
        .Concat(new[] { pair.A }.Concat(pair.B is null ? [] : new[] { pair.B }).SelectMany(p => p.Links.Select(l => l.Ref))).ToArray();
    public async Task SaveReplayAsync(EvaluationExample example, string decider, string predicted, double probability,
        AiReservation reservation, AiUsage usage, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        var at = clock.GetUtcNow().ToString("O");
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO evaluation_result VALUES(@Id,@decider,@predicted,@probability,@Cost,@LatencyMs,@at)
            ON CONFLICT(example_id,decider) DO UPDATE SET predicted=excluded.predicted,probability=excluded.probability,
              cost=excluded.cost,latency_ms=excluded.latency_ms,created_at=excluded.created_at
            """, new { example.Id, decider, predicted, probability, usage.Cost, usage.LatencyMs, at }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE ai_call SET status='completed',cost=@Cost,input_tokens=@InputTokens,output_tokens=@OutputTokens,
              latency_ms=@LatencyMs,outcome=@outcome WHERE id=@id
            """, new { id = reservation.Id, usage.Cost, usage.InputTokens, usage.OutputTokens, usage.LatencyMs,
                outcome = $"Evaluation: {predicted}; expected {example.Expected}" }, tx, cancellationToken: ct));
        tx.Commit();
    }
    private sealed record DecisionRow(string Id, string PrId, string Choice, string At);
    private sealed record VerdictRow(string Decider, string Action, double Confidence, string? InputJson);
    private sealed record LinkRow(string Id, string ARef, string BRef, string Status, string At);
    private sealed record PairRow(string Decider, double Related, string? InputJson);
}
