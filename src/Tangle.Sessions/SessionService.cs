using System.Text.RegularExpressions;
using Dapper;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Sessions;

public sealed record SessionReport(string SessionId, string TranscriptPath, string Cwd, string GitRemote,
    string GitBranch, string? StartedAt = null, string? Excerpt = null);
public sealed record SessionInfo(string Id, string? Path, string? Cwd, string? GitBranch,
    string? StartedAt, string? PrId, string? MatchedBy, string? RepoId, string? Excerpt);

public sealed partial class SessionService(Database database, TimeProvider clock, ILiveEvents events)
{
    public static string Repo(string remote)
    {
        var match = Remote().Match(remote);
        if (!match.Success) throw new ArgumentException("A GitHub HTTPS or SSH remote is required.");
        return $"{match.Groups[1].Value}/{match.Groups[2].Value}";
    }
    public async Task<SessionInfo> ReportAsync(SessionReport report, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(report.SessionId) || report.SessionId.Length > 200 ||
            string.IsNullOrWhiteSpace(report.GitRemote) || report.GitRemote.Length > 2000 ||
            string.IsNullOrWhiteSpace(report.TranscriptPath) || string.IsNullOrWhiteSpace(report.Cwd) ||
            string.IsNullOrWhiteSpace(report.GitBranch) || report.GitBranch.Length > 300 ||
            report.TranscriptPath?.Length > 2000 || report.Cwd?.Length > 2000 || report.Excerpt?.Length > 300)
            throw new ArgumentException("Invalid session metadata. Optional excerpt must be at most 300 characters.");
        var repo = Repo(report.GitRemote);
        await using var db = await database.OpenAsync(ct);
        var matches = (await db.QueryAsync<string>(new CommandDefinition("""
            SELECT id FROM pr WHERE lower(repo_id)=lower(@repo) AND head_ref=@branch AND state='OPEN'
            """, new { repo, branch = report.GitBranch }, cancellationToken: ct))).ToArray();
        var prId = matches.Length == 1 ? matches[0] : null;
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO session(id,path,cwd,git_branch,started_at,pr_id,matched_by,repo_id,excerpt)
            VALUES(@id,@path,@cwd,@branch,@at,@prId,@matched,@repo,@excerpt)
            ON CONFLICT(id) DO UPDATE SET path=excluded.path,cwd=excluded.cwd,git_branch=excluded.git_branch,
              repo_id=excluded.repo_id,excerpt=excluded.excerpt,
              pr_id=CASE WHEN session.matched_by='manual' THEN session.pr_id ELSE excluded.pr_id END,
              matched_by=CASE WHEN session.matched_by='manual' THEN session.matched_by ELSE excluded.matched_by END
            """, new { id = report.SessionId, path = report.TranscriptPath, cwd = report.Cwd, branch = report.GitBranch,
                at = report.StartedAt ?? clock.GetUtcNow().ToString("O"), prId, matched = prId is null ? null : "branch", repo, excerpt = report.Excerpt },
            cancellationToken: ct));
        await MatchAsync(ct);
        events.Publish("prs.changed", new { ids = prId is null ? Array.Empty<string>() : new[] { prId } });
        return await db.QuerySingleAsync<SessionInfo>(new CommandDefinition(Select + " WHERE id=@id", new { id = report.SessionId }, cancellationToken: ct));
    }
    private const string Select = """
        SELECT id AS Id,path AS Path,cwd AS Cwd,git_branch AS GitBranch,started_at AS StartedAt,pr_id AS PrId,
          matched_by AS MatchedBy,repo_id AS RepoId,excerpt AS Excerpt FROM session
        """;
    public async Task MatchAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        using var tx = db.BeginTransaction();
        var sessions = await db.QueryAsync<SessionInfo>(new CommandDefinition(Select + " WHERE matched_by IS NULL OR matched_by='branch'",
            transaction: tx, cancellationToken: ct));
        foreach (var session in sessions)
        {
            var matches = (await db.QueryAsync<string>(new CommandDefinition(
                "SELECT id FROM pr WHERE lower(repo_id)=lower(@repo) AND head_ref=@branch AND state='OPEN'",
                new { repo = session.RepoId, branch = session.GitBranch }, tx, cancellationToken: ct))).ToArray();
            if (matches.Length == 1) await db.ExecuteAsync(new CommandDefinition(
                "UPDATE session SET pr_id=@prId,matched_by='branch' WHERE id=@id",
                new { prId = matches[0], id = session.Id }, tx, cancellationToken: ct));
        }
        var texts = await db.QueryAsync<PrText>(new CommandDefinition("""
            SELECT id AS Id,body AS Text FROM pr
            UNION ALL SELECT pr_id AS Id,detail AS Text FROM pr_event WHERE kind='commit'
            """, transaction: tx, cancellationToken: ct));
        var trailers = new Dictionary<string, HashSet<string>>();
        foreach (var text in texts)
        {
            var value = text.Text;
            if (value.StartsWith('{'))
            {
                using var json = System.Text.Json.JsonDocument.Parse(value);
                if (json.RootElement.TryGetProperty("commit", out var commit) && commit.TryGetProperty("message", out var message))
                    value = message.GetString() ?? "";
            }
            foreach (Match match in Trailer().Matches(value))
            {
                var id = match.Groups[1].Value.TrimEnd('.', ',', ';');
                if (!trailers.TryGetValue(id, out var targets)) trailers[id] = targets = [];
                targets.Add(text.Id);
            }
        }
        foreach (var (id, targets) in trailers)
        {
            if (targets.Count == 1)
            {
                var prId = targets.Single();
                await db.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO session(id,pr_id,matched_by) VALUES(@id,@prId,'trailer')
                    ON CONFLICT(id) DO UPDATE SET pr_id=excluded.pr_id,matched_by='trailer'
                    WHERE session.matched_by IS NULL OR session.matched_by='branch'
                    """, new { id, prId }, tx, cancellationToken: ct));
            }
        }
        tx.Commit();
    }
    public async Task AttachAsync(string sessionId, string prId, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM pr WHERE id=@prId", new { prId }, cancellationToken: ct)) != 1)
            throw new ArgumentException("PR not found.");
        if (await db.ExecuteAsync(new CommandDefinition("UPDATE session SET pr_id=@prId,matched_by='manual' WHERE id=@sessionId",
            new { sessionId, prId }, cancellationToken: ct)) != 1) throw new ArgumentException("Session not found.");
        events.Publish("prs.changed", new { ids = new[] { prId } });
    }
    public async Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return (await db.QueryAsync<SessionInfo>(new CommandDefinition(Select + " ORDER BY started_at DESC,id", cancellationToken: ct))).ToArray();
    }
    private sealed record PrText(string Id, string Text);
    [GeneratedRegex(@"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([^/]+)/([^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex Remote();
    [GeneratedRegex(@"Claude-Session:\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex Trailer();
}
