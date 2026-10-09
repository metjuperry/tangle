using Dapper;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Tangle.Core;

namespace Tangle.Data;

public sealed partial class SyncStore
{
    private const string FactSelect = """
        SELECT pr_id AS PrId, turn AS Turn, turn_reason AS TurnReason, age_days AS AgeDays,
          idle_days AS IdleDays, behind_by AS BehindBy, conflicting AS Conflicting,
          size AS Size, origin AS Origin, computed_at AS ComputedAt FROM fact
        """;

    public async Task RecomputeAsync(CancellationToken ct = default)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        await ComputeCacheAsync(db, tx, ct);
        tx.Commit();
    }

    private async Task ComputeCacheAsync(SqliteConnection db, SqliteTransaction tx, CancellationToken ct)
    {
        var rows = await db.QueryAsync<PullRequest>(new CommandDefinition("""
            SELECT id AS Id, repo_id AS RepoId, number AS Number, title AS Title, body AS Body,
              author AS Author, is_mine AS IsMine, state AS State, is_draft AS IsDraft, head_ref AS HeadRef,
              base_ref AS BaseRef, created_at AS CreatedAt, updated_at AS UpdatedAt, closed_at AS ClosedAt,
              additions AS Additions, deletions AS Deletions, changed_files AS ChangedFiles,
              mergeable AS Mergeable, review_decision AS ReviewDecision, checks_state AS ChecksState,
              content_hash AS ContentHash, viewer AS Viewer FROM pr ORDER BY id
            """, transaction: tx, cancellationToken: ct));
        var botsJson = await db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT value FROM app_setting WHERE key='bot_logins'", transaction: tx, cancellationToken: ct));
        var bots = JsonSerializer.Deserialize<string[]>(botsJson) ?? throw new InvalidOperationException("Bot logins setting is invalid.");
        var scopes = (await db.QueryAsync<string>(new CommandDefinition(
            "SELECT scope FROM sync_state", transaction: tx, cancellationToken: ct))).ToArray();
        var prs = new List<PullRequest>();
        foreach (var row in rows)
        {
            var viewer = row.Viewer;
            if (viewer.Length == 0)
            {
                if (row.IsMine && row.Author is not null) viewer = row.Author;
                else
                {
                    var scope = scopes.FirstOrDefault(s => s.EndsWith($"org:{row.RepoId.Split('/')[0]}", StringComparison.OrdinalIgnoreCase)
                        || s.EndsWith($"user:{row.RepoId.Split('/')[0]}", StringComparison.OrdinalIgnoreCase)
                        || s.EndsWith($"repo:{row.RepoId}", StringComparison.OrdinalIgnoreCase));
                    if (scope is null)
                        throw new InvalidOperationException($"Viewer missing for cached PR {row.Id}. Run a full sync to populate its identity.");
                    viewer = scope.Split(' ')[0]["involves:".Length..];
                }
            }
            var files = await db.QueryAsync<string>(new CommandDefinition(
                "SELECT path FROM pr_file WHERE pr_id=@Id ORDER BY path", row, tx, cancellationToken: ct));
            var events = await db.QueryAsync<PrEvent>(new CommandDefinition(
                "SELECT at AS At, actor AS Actor, kind AS Kind, detail AS Detail FROM pr_event WHERE pr_id=@Id ORDER BY at",
                row, tx, cancellationToken: ct));
            var threads = await db.QueryAsync<ReviewThread>(new CommandDefinition("""
                SELECT id AS Id, path AS Path, is_resolved AS IsResolved, last_actor AS LastActor,
                  last_at AS LastAt, excerpt AS Excerpt FROM review_thread WHERE pr_id=@Id ORDER BY id
                """, row, tx, cancellationToken: ct));
            var requests = await db.QueryAsync<ReviewRequest>(new CommandDefinition(
                "SELECT reviewer AS Reviewer, kind AS Kind FROM pr_review_request WHERE pr_id=@Id ORDER BY reviewer",
                row, tx, cancellationToken: ct));
            prs.Add(row with { Viewer = viewer, Files = files.ToArray(), Events = events.ToArray(),
                Threads = threads.ToArray(), ReviewRequests = requests.ToArray() });
        }
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        foreach (var pr in prs)
        {
            var facts = FactsEngine.Compute(pr, now, bots);
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fact(pr_id,turn,turn_reason,age_days,idle_days,behind_by,conflicting,size,origin,computed_at)
                  VALUES(@PrId,@Turn,@TurnReason,@AgeDays,@IdleDays,@BehindBy,@Conflicting,@Size,@Origin,@ComputedAt)
                ON CONFLICT(pr_id) DO UPDATE SET turn=excluded.turn,turn_reason=excluded.turn_reason,
                  age_days=excluded.age_days,idle_days=excluded.idle_days,behind_by=excluded.behind_by,
                  conflicting=excluded.conflicting,size=excluded.size,origin=excluded.origin,computed_at=excluded.computed_at
                """, facts, tx, cancellationToken: ct));
        }
        await db.ExecuteAsync(new CommandDefinition("UPDATE pr SET batch_id=NULL", transaction: tx, cancellationToken: ct));
        foreach (var batch in LinkRules.Batches(prs))
        foreach (var id in batch.PrIds)
            await db.ExecuteAsync(new CommandDefinition("UPDATE pr SET batch_id=@batch WHERE id=@id",
                new { batch = batch.Id, id }, tx, cancellationToken: ct));
        var rules = LinkRules.Find(prs);
        // Only unedited rule rows are disposable. A user's decisions always win.
        await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM link WHERE source='rule' AND status='tied' AND decided_at IS NULL AND label IS NULL
              AND id NOT IN @ids
            """, new { ids = rules.Select(l => l.Id).ToArray() }, tx, cancellationToken: ct));
        foreach (var link in rules)
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO link(id,a_ref,b_ref,source,rule,status,reason,created_at)
                  VALUES(@Id,@ARef,@BRef,'rule',@Rule,'tied',@Reason,@at)
                ON CONFLICT(a_ref,b_ref) DO UPDATE SET rule=excluded.rule,reason=excluded.reason
                  WHERE link.source='rule' AND link.status='tied' AND link.decided_at IS NULL AND link.label IS NULL
                """, new { link.Id, link.ARef, link.BRef, link.Rule, link.Reason, at = now.ToString("O") },
                tx, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<BoardString>> GetStringsAsync(CancellationToken ct = default)
    {
        var cards = await GetBoardAsync(ct: ct);
        var visible = cards.Select(c => $"pr:{c.Id}").ToHashSet(StringComparer.Ordinal);
        await using var db = await database.OpenAsync(ct);
        var links = await db.QueryAsync<BoardString>(new CommandDefinition("""
            SELECT id AS Id,a_ref AS ARef,b_ref AS BRef,source AS Source,rule AS Rule,status AS Status,
              reason AS Reason,label AS Label FROM link WHERE status<>'cut' ORDER BY id
            """, cancellationToken: ct));
        return links.Where(l => visible.Contains(l.ARef) && visible.Contains(l.BRef)).ToArray();
    }
}

public sealed record BoardString(string Id, string ARef, string BRef, string Source, string? Rule,
    string Status, string? Reason, string? Label)
{
    public BoardString() : this("", "", "", "", null, "", null, null) { }
}
