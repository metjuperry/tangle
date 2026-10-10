using Dapper;
using System.Text.Json;
using Tangle.Core;

namespace Tangle.Data;

public sealed partial class SyncStore(Database database, TimeProvider? clock = null) : ISyncStore
{
    public async Task<WatchedSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        await using var db = await database.OpenAsync(ct);
        var owners = await db.QueryAsync<OwnerSetting>(new CommandDefinition(
            "SELECT id AS Id, kind AS Kind, watched AS Watched FROM organization ORDER BY id", cancellationToken: ct));
        var repos = await db.QueryAsync<RepoSetting>(new CommandDefinition(
            "SELECT id AS Id, watched AS Watched, ai_allowed AS AiAllowed FROM repo ORDER BY id", cancellationToken: ct));
        var bots = await db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT value FROM app_setting WHERE key='bot_logins'", cancellationToken: ct));
        return new(owners.ToArray(), repos.ToArray())
        {
            BotLogins = JsonSerializer.Deserialize<string[]>(bots) ?? throw new InvalidOperationException("Bot logins setting is invalid."),
            AiBudget = decimal.Parse(await db.QuerySingleAsync<string>(new CommandDefinition("SELECT value FROM app_setting WHERE key='ai_budget'", cancellationToken: ct)),
                System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public async Task SaveSettingsAsync(WatchedSettings settings, CancellationToken ct = default)
    {
        var previous = await GetSettingsAsync(ct);
        var changed = !previous.Owners.OrderBy(o => o.Id).SequenceEqual(settings.Owners.OrderBy(o => o.Id))
            || !previous.Repos.OrderBy(r => r.Id).SequenceEqual(settings.Repos.OrderBy(r => r.Id));
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        await db.ExecuteAsync(new CommandDefinition("UPDATE organization SET watched=0; UPDATE repo SET watched=0",
            transaction: tx, cancellationToken: ct));
        foreach (var owner in settings.Owners)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO organization(id,kind,watched) VALUES (@Id,@Kind,@Watched) ON CONFLICT(id) DO UPDATE SET kind=excluded.kind,watched=excluded.watched",
                owner, tx, cancellationToken: ct));
        foreach (var repo in settings.Repos)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO repo(id,watched,ai_allowed) VALUES (@Id,@Watched,@AiAllowed) ON CONFLICT(id) DO UPDATE SET watched=excluded.watched,ai_allowed=excluded.ai_allowed",
                repo, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("UPDATE app_setting SET value=@value WHERE key='bot_logins'",
            new { value = JsonSerializer.Serialize(settings.BotLogins) }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("UPDATE app_setting SET value=@value WHERE key='ai_budget'",
            new { value = settings.AiBudget.ToString(System.Globalization.CultureInfo.InvariantCulture) }, tx, cancellationToken: ct));
        if (changed)
            await db.ExecuteAsync(new CommandDefinition("DELETE FROM sync_state", transaction: tx, cancellationToken: ct));
        await ComputeCacheAsync(db, tx, ct);
        tx.Commit();
    }

    public async Task<string?> GetCursorAsync(string scope, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT cursor FROM sync_state WHERE scope=@scope", new { scope }, cancellationToken: ct));
    }

    public async Task<int> SaveScopeAsync(string scope, IReadOnlyList<PullRequest> prs, string cursor, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        var count = 0;
        foreach (var pr in prs)
        {
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO repo(id) VALUES(@RepoId) ON CONFLICT(id) DO NOTHING", pr, tx, cancellationToken: ct));
            var hash = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT content_hash FROM pr WHERE id=@Id", pr, tx, cancellationToken: ct));
            if (hash == pr.ContentHash) continue;
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO pr(id,repo_id,number,title,body,author,is_mine,state,is_draft,head_ref,base_ref,
                  created_at,updated_at,closed_at,additions,deletions,changed_files,mergeable,review_decision,checks_state,content_hash,viewer)
                VALUES(@Id,@RepoId,@Number,@Title,@Body,@Author,@IsMine,@State,@IsDraft,@HeadRef,@BaseRef,
                  @CreatedAt,@UpdatedAt,@ClosedAt,@Additions,@Deletions,@ChangedFiles,@Mergeable,@ReviewDecision,@ChecksState,@ContentHash,@Viewer)
                ON CONFLICT(id) DO UPDATE SET repo_id=excluded.repo_id,number=excluded.number,title=excluded.title,
                  body=excluded.body,author=excluded.author,is_mine=excluded.is_mine,state=excluded.state,
                  is_draft=excluded.is_draft,head_ref=excluded.head_ref,base_ref=excluded.base_ref,
                  created_at=excluded.created_at,updated_at=excluded.updated_at,closed_at=excluded.closed_at,
                  additions=excluded.additions,deletions=excluded.deletions,changed_files=excluded.changed_files,
                  mergeable=excluded.mergeable,review_decision=excluded.review_decision,
                  checks_state=excluded.checks_state,content_hash=excluded.content_hash,viewer=excluded.viewer
                """, pr, tx, cancellationToken: ct));
            await db.ExecuteAsync(new CommandDefinition("""
                DELETE FROM pr_file WHERE pr_id=@Id;
                DELETE FROM pr_event WHERE pr_id=@Id;
                DELETE FROM review_thread WHERE pr_id=@Id;
                DELETE FROM pr_review_request WHERE pr_id=@Id;
                """, pr, tx, cancellationToken: ct));
            foreach (var path in pr.Files)
                await db.ExecuteAsync(new CommandDefinition("INSERT INTO pr_file VALUES(@id,@path)",
                    new { id = pr.Id, path }, tx, cancellationToken: ct));
            foreach (var e in pr.Events)
                await db.ExecuteAsync(new CommandDefinition("INSERT INTO pr_event VALUES(@id,@At,@Actor,@Kind,@Detail)",
                    new { id = pr.Id, e.At, e.Actor, e.Kind, e.Detail }, tx, cancellationToken: ct));
            foreach (var t in pr.Threads)
                await db.ExecuteAsync(new CommandDefinition("INSERT INTO review_thread VALUES(@Id,@pr,@Path,@IsResolved,@LastActor,@LastAt,@Excerpt)",
                    new { t.Id, pr = pr.Id, t.Path, t.IsResolved, t.LastActor, t.LastAt, t.Excerpt }, tx, cancellationToken: ct));
            foreach (var r in pr.ReviewRequests)
                await db.ExecuteAsync(new CommandDefinition("INSERT INTO pr_review_request VALUES(@pr,@Reviewer,@Kind)",
                    new { pr = pr.Id, r.Reviewer, r.Kind }, tx, cancellationToken: ct));
            count++;
        }
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO sync_state(scope,cursor,last_run_at) VALUES(@scope,@cursor,@cursor)
            ON CONFLICT(scope) DO UPDATE SET cursor=excluded.cursor,last_run_at=excluded.last_run_at
            """, new { scope, cursor }, tx, cancellationToken: ct));
        await ComputeCacheAsync(db, tx, ct);
        tx.Commit();
        return count;
    }

    public async Task<IReadOnlyList<BoardCard>> GetBoardAsync(bool includeClosed = false, CancellationToken ct = default)
    {
        await using var db = await database.OpenAsync(ct);
        var rows = await db.QueryAsync<BoardCard>(new CommandDefinition("""
            SELECT p.id AS Id, p.repo_id AS RepoId, p.number AS Number, p.title AS Title, p.author AS Author,
              p.is_mine AS IsMine, p.state AS State, p.is_draft AS IsDraft, p.updated_at AS UpdatedAt,
              p.created_at AS CreatedAt, p.content_hash AS ContentHash, p.batch_id AS BatchId,
              p.head_ref AS HeadRef, p.base_ref AS BaseRef
            FROM pr p JOIN repo r ON r.id=p.repo_id
            WHERE (@includeClosed=1 OR p.state='OPEN') AND r.watched=1
              AND NOT EXISTS (
                SELECT 1 FROM organization o
                WHERE lower(o.id)=lower(substr(p.repo_id,1,instr(p.repo_id,'/')-1)) AND o.watched=0
              )
            ORDER BY p.repo_id,p.number
            """, new { includeClosed }, cancellationToken: ct));
        var facts = (await db.QueryAsync<PrFacts>(new CommandDefinition(FactSelect, cancellationToken: ct)))
            .ToDictionary(f => f.PrId);
        var verdicts = (await db.QueryAsync<StoredVerdict>(new CommandDefinition("""
            SELECT pr_id AS PrId,action AS Action,confidence AS Confidence,priority AS Priority,reason AS Reason,intent AS Intent,created_at AS CreatedAt
            FROM verdict ORDER BY created_at DESC
            """, cancellationToken: ct))).GroupBy(v => v.PrId).ToDictionary(g => g.Key, g => g.First());
        return rows.Select(r => r with { Facts = facts.GetValueOrDefault(r.Id), Verdict = verdicts.GetValueOrDefault(r.Id) }).ToArray();
    }
}

public sealed record BoardCard(string Id, string RepoId, long Number, string Title, string? Author,
    bool IsMine, string State, bool IsDraft, string UpdatedAt, string CreatedAt, string ContentHash)
{
    public BoardCard() : this("", "", 0, "", null, false, "", false, "", "", "") { }
    public PrFacts? Facts { get; init; }
    public string? BatchId { get; init; }
    public string HeadRef { get; init; } = "";
    public string BaseRef { get; init; } = "";
    public StoredVerdict? Verdict { get; init; }
}
