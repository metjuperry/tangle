using Dapper;
using System.Text.Json;
using Tangle.Core;

namespace Tangle.Data;

public sealed class AiStore(Database database, SyncStore sync, AuthoringStore authoring, TimeProvider clock) : IAiStore
{
    public async Task<AiSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var settings = await sync.GetSettingsAsync(ct);
        var cards = (await sync.GetBoardAsync(ct: ct)).Where(c => settings.Repos.Any(r => r.Id == c.RepoId && r.AiAllowed && r.Watched)).ToArray();
        var allowedRefs = cards.Select(c => $"pr:{c.Id}").ToHashSet();
        var own = await authoring.GetAsync(ct);
        var strings = await sync.GetStringsAsync(ct, includeCut: true);
        var contexts = new List<PrContext>();
        await using var db = await database.OpenAsync(ct);
        foreach (var card in cards)
        {
            var context = await sync.GetCardContextAsync(card.Id, ct) ?? throw new InvalidOperationException("Allowed PR disappeared.");
            var paths = await db.QueryAsync<string>(new CommandDefinition("SELECT path FROM pr_file WHERE pr_id=@id ORDER BY path LIMIT 100",
                new { id = card.Id }, cancellationToken: ct));
            var facts = card.Facts ?? throw new InvalidOperationException("Facts missing for AI input.");
            var linked = strings.Where(l => l.Status == "tied" && (l.ARef == $"pr:{card.Id}" || l.BRef == $"pr:{card.Id}"))
                .Select(l => (Link: l, Other: l.ARef == $"pr:{card.Id}" ? l.BRef : l.ARef))
                .Where(l => allowedRefs.Contains(l.Other) || l.Other.StartsWith("sticky:"))
                .Take(20).Select(l => new LinkedContext(l.Other,
                    cards.FirstOrDefault(c => $"pr:{c.Id}" == l.Other)?.Title ??
                    own.Stickies.FirstOrDefault(s => $"sticky:{s.Id}" == l.Other)?.Title ?? "",
                    l.Link.Label is null ? null : Limit(l.Link.Label, 1000))).Where(l => l.Title.Length > 0).ToArray();
            contexts.Add(new($"pr:{card.Id}", card.RepoId, card.Title, Limit(context.Body, 4000), card.Author, card.CreatedAt,
                paths.ToArray(), new(facts.Turn, facts.TurnReason, facts.AgeDays, facts.IdleDays, facts.BehindBy, facts.Conflicting, facts.Size, facts.Origin),
                context.Threads.Take(5).Select(t => Limit(t.Excerpt ?? "", 300)).ToArray(), linked));
        }
        var budget = await db.QuerySingleAsync<string>(new CommandDefinition("SELECT value FROM app_setting WHERE key='ai_budget'", cancellationToken: ct));
        return new(contexts, own.Stickies.Select(s => new AiSticky($"sticky:{s.Id}", s.Title)).ToArray(),
            strings.Select(s => new AiPair(s.ARef, s.BRef)).ToArray(), decimal.Parse(budget, System.Globalization.CultureInfo.InvariantCulture));
    }
    private static string Limit(string text, int length) => text.Length > length ? text[..length] : text;
    public async Task<bool> CachedAsync(string hash, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM ai_cache WHERE input_hash=@hash", new { hash }, cancellationToken: ct)) > 0;
    }
    public async Task<AiReservation?> ReserveAsync(string hash, string decider, decimal maximumCost, CancellationToken ct,
        string? job = null, IReadOnlyList<string>? targets = null)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction(deferred: false);
        var budget = decimal.Parse(await db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT value FROM app_setting WHERE key='ai_budget'", transaction: tx, cancellationToken: ct)), System.Globalization.CultureInfo.InvariantCulture);
        var month = clock.GetUtcNow().ToString("yyyy-MM");
        var used = await db.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(CASE WHEN status='reserved' THEN reserved_cost ELSE cost END),0)
            FROM ai_call WHERE substr(created_at,1,7)=@month
            """, new { month }, tx, cancellationToken: ct));
        if (used + maximumCost > budget) return null;
        var id = Guid.NewGuid().ToString();
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ai_call(id,decider,input_hash,input_tokens,output_tokens,cost,latency_ms,created_at,status,reserved_cost,job,targets)
            VALUES(@id,@decider,@hash,0,0,0,0,@at,'reserved',@maximumCost,@job,@targets)
            """, new { id, decider, hash, at = clock.GetUtcNow().ToString("O"), maximumCost, job,
                targets = targets is null ? null : JsonSerializer.Serialize(targets) }, tx, cancellationToken: ct));
        tx.Commit();
        return new(id, hash);
    }
    public async Task FinishAsync(AiReservation reservation, AiUsage usage, PrContext? pr, PrJudgment? verdict,
        IReadOnlyList<LinkJudgment>? links, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        var at = clock.GetUtcNow().ToString("O");
        var savedVerdict = false;
        var suggested = 0;
        if (pr is not null && verdict is not null && await Allowed(db, tx, pr.Ref, ct))
        {
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO verdict(pr_id,decider,action,confidence,reason,intent,priority,input_hash,created_at)
                VALUES(@id,'haiku-5-5',@action,@Confidence,@Reason,@Intent,@Priority,@hash,@at)
                ON CONFLICT(pr_id,decider,input_hash) DO NOTHING
                """, new { id = pr.Ref[3..], action = verdict.Action.ToString().ToLowerInvariant(), verdict.Confidence, verdict.Reason,
                    verdict.Intent, verdict.Priority, hash = reservation.InputHash, at }, tx, cancellationToken: ct));
            savedVerdict = true;
        }
        foreach (var link in links ?? [])
        {
            if (link.Related <= .75 || !await Allowed(db, tx, link.ARef, ct) || !await Allowed(db, tx, link.BRef, ct)) continue;
            var (a, b) = string.CompareOrdinal(link.ARef, link.BRef) < 0 ? (link.ARef, link.BRef) : (link.BRef, link.ARef);
            suggested += await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO link(id,a_ref,b_ref,source,status,confidence,reason,created_at)
                VALUES(@id,@a,@b,'ai','suggested',@Related,@Reason,@at) ON CONFLICT(a_ref,b_ref) DO NOTHING
                """, new { id = Guid.NewGuid().ToString(), a, b, link.Related, link.Reason, at }, tx, cancellationToken: ct));
        }
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO ai_cache VALUES(@hash,@at) ON CONFLICT(input_hash) DO NOTHING",
            new { hash = reservation.InputHash, at }, tx, cancellationToken: ct));
        await Complete(db, tx, reservation, usage, "completed", null, ct);
        var outcome = verdict is not null
            ? savedVerdict ? $"{verdict.Action.ToString().ToLowerInvariant()}: {verdict.Reason}" : "Verdict not saved: repository permission or PR state changed."
            : $"Assessed {links?.Count ?? 0} candidate pairs; created {suggested} suggested strings.";
        await db.ExecuteAsync(new CommandDefinition("UPDATE ai_call SET pr_id=@prId,outcome=@outcome WHERE id=@id",
            new { prId = pr?.Ref[3..], outcome, id = reservation.Id }, tx, cancellationToken: ct));
        tx.Commit();
    }
    private static async Task<bool> Allowed(Microsoft.Data.Sqlite.SqliteConnection db, Microsoft.Data.Sqlite.SqliteTransaction tx, string reference, CancellationToken ct)
    {
        if (reference.StartsWith("sticky:"))
            return await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM sticky WHERE id=@id AND archived_at IS NULL", new { id = reference[7..] }, tx, cancellationToken: ct)) == 1;
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM pr p JOIN repo r ON p.repo_id=r.id
            WHERE p.id=@id AND p.state='OPEN' AND r.watched=1 AND r.ai_allowed=1
            """, new { id = reference[3..] }, tx, cancellationToken: ct)) == 1;
    }
    public async Task FailAsync(AiReservation reservation, string error, AiUsage? usage, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        if (usage is not null) await Complete(db, tx, reservation, usage, "failed", error, ct);
        else await db.ExecuteAsync(new CommandDefinition("UPDATE ai_call SET error=@error WHERE id=@id",
            new { id = reservation.Id, error }, tx, cancellationToken: ct));
        tx.Commit();
    }
    private static Task Complete(Microsoft.Data.Sqlite.SqliteConnection db, Microsoft.Data.Sqlite.SqliteTransaction tx,
        AiReservation reservation, AiUsage usage, string status, string? error, CancellationToken ct) =>
        db.ExecuteAsync(new CommandDefinition("""
            UPDATE ai_call SET input_tokens=@InputTokens,output_tokens=@OutputTokens,cost=@Cost,latency_ms=@LatencyMs,status=@status,error=@error WHERE id=@id
            """, new { id = reservation.Id, usage.InputTokens, usage.OutputTokens, usage.Cost, usage.LatencyMs, status, error }, tx, cancellationToken: ct));
}
