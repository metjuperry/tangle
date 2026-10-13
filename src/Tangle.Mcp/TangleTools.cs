using System.ComponentModel;
using System.Text.Json;
using Dapper;
using ModelContextProtocol.Server;
using Tangle.Core;
using Tangle.Data;
using Tangle.Sessions;

namespace Tangle.Mcp;

[McpServerToolType]
public sealed class TangleTools(SyncStore sync, SyncService synchronizer, AuthoringStore authoring,
    ActionQueue actions, Database database, TimeProvider clock, ILiveEvents events, SessionService sessions, IEvaluationReports evaluation,
    IUserContext? user = null, ControlDatabase? control = null)
{
    private async Task RequireClaudeMode(CancellationToken ct)
    {
        if (user is not null && user.UserId != "local" && control is not null &&
            (await control.AiAsync(user.UserId, ct)).Mode != "claude_code")
            throw new InvalidOperationException("Choose My Claude Code in your AI access settings before recording AI proposals.");
    }
    private async Task Fresh(CancellationToken ct)
    {
        var settings = await sync.GetSettingsAsync(ct);
        if (!settings.Owners.Any(o => o.Watched) && !settings.Repos.Any(r => r.Watched)) return;
        var last = await sync.GetLastSyncAsync(ct);
        if (last is null || clock.GetUtcNow() - FactsEngine.At(last) > TimeSpan.FromMinutes(10))
        {
            await synchronizer.RunAsync(ct);
            await sessions.MatchAsync(ct);
        }
    }
    private static string Json(object data) => JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [McpServerTool(Name = "list_prs"), Description("Read watched open PRs and computed facts; refresh stale GitHub cache on demand.")]
    public async Task<string> ListPrs(CancellationToken ct)
    { await Fresh(ct); return Json(await sync.GetBoardAsync(ct: ct)); }

    [McpServerTool(Name = "get_pr_context"), Description("Read a PR's facts, review asks, recent events, notes and sessions before proposing anything.")]
    public async Task<string> GetPrContext(string prId, CancellationToken ct)
    {
        await Fresh(ct);
        var context = await sync.GetCardContextAsync(prId, ct) ?? throw new ArgumentException("PR not found in watched repositories.");
        var own = await authoring.GetAsync(ct);
        return Json(new { context.Card, context.Body, context.Threads, context.Events, context.Strings,
            notes = own.Notes.Where(n => n.TargetRef == $"pr:{prId}"), sessions = context.Sessions });
    }
    [McpServerTool(Name = "get_threads"), Description("Read groups of PRs joined by tied strings or user stickies.")]
    public async Task<string> GetThreads(CancellationToken ct)
    {
        await Fresh(ct);
        var cards = await sync.GetBoardAsync(ct: ct);
        var links = (await sync.GetStringsAsync(ct)).Where(l => l.Status == "tied").ToArray();
        var own = await authoring.GetAsync(ct);
        var refs = cards.Select(p => $"pr:{p.Id}").Concat(own.Stickies.Select(s => $"sticky:{s.Id}")).ToArray();
        var visited = new HashSet<string>();
        var groups = new List<object>();
        foreach (var root in refs)
        {
            if (!visited.Add(root)) continue;
            var group = new HashSet<string> { root };
            var queue = new Queue<string>(); queue.Enqueue(root);
            while (queue.TryDequeue(out var current))
            {
                foreach (var link in links.Where(l => l.ARef == current || l.BRef == current))
                {
                    var other = link.ARef == current ? link.BRef : link.ARef;
                    if (visited.Add(other)) { group.Add(other); queue.Enqueue(other); }
                }
            }
            groups.Add(new { prs = cards.Where(p => group.Contains($"pr:{p.Id}")),
                stickies = own.Stickies.Where(s => group.Contains($"sticky:{s.Id}")), strings = links.Where(l => group.Contains(l.ARef) && group.Contains(l.BRef)) });
        }
        return Json(groups);
    }
    [McpServerTool(Name = "get_eval_report"), Description("Read evaluation metrics and labelled-example coverage. Never triggers provider calls or a replay.")]
    public async Task<string> GetEvalReport(CancellationToken ct)
    {
        return Json(await evaluation.GetAsync(ct));
    }
    [McpServerTool(Name = "add_note"), Description("Save a local note to a pr:, sticky: or link: reference. Never posts to GitHub.")]
    public async Task<string> AddNote(string targetRef, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 10000) throw new ArgumentException("Note text must be 1–10,000 characters.");
        var note = await authoring.NoteAsync(targetRef, text.Trim(), ct);
        events.Publish(targetRef.StartsWith("sticky:") ? "sticky.changed" : "link.changed", new { targetRef });
        return Json(note);
    }
    [McpServerTool(Name = "propose_link"), Description("Propose a dashed string with a reason for human tie/cut. Never overrides tied or cut pairs.")]
    public async Task<string> ProposeLink(string aRef, string bRef, string reason, CancellationToken ct)
    {
        await RequireClaudeMode(ct);
        if (aRef == bRef || aRef.StartsWith("link:") || bRef.StartsWith("link:") ||
            string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new ArgumentException("Two different card refs and a reason are required.");
        await authoring.RequireRefAsync(aRef, ct); await authoring.RequireRefAsync(bRef, ct);
        var visible = (await sync.GetBoardAsync(ct: ct)).Select(p => $"pr:{p.Id}")
            .Concat((await authoring.GetAsync(ct)).Stickies.Select(s => $"sticky:{s.Id}")).ToHashSet();
        if (!visible.Contains(aRef) || !visible.Contains(bRef)) throw new ArgumentException("Both endpoints must be visible on the watched board.");
        if (string.CompareOrdinal(aRef, bRef) > 0) (aRef, bRef) = (bRef, aRef);
        await using var db = await database.OpenAsync(ct);
        var id = Guid.NewGuid().ToString();
        var inserted = await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO link(id,a_ref,b_ref,source,status,reason,created_at)
            VALUES(@id,@aRef,@bRef,'ai','suggested',@reason,@at) ON CONFLICT(a_ref,b_ref) DO NOTHING
            """, new { id, aRef, bRef, reason, at = clock.GetUtcNow().ToString("O") }, cancellationToken: ct));
        if (inserted == 0) throw new InvalidOperationException("This pair already has a string or a saved cut; your decision was preserved.");
        events.Publish("link.changed", new { id }); return Json(new { id, status = "suggested" });
    }
    [McpServerTool(Name = "record_verdict"), Description("Record a proposed verdict with a reason and confidence, not a GitHub action.")]
    public async Task<string> RecordVerdict(string prId, string action, double confidence, int priority, string reason, string intent, CancellationToken ct)
    {
        await RequireClaudeMode(ct);
        if (!Enum.TryParse<NextAction>(action, true, out var parsed) || !Enum.IsDefined(parsed) ||
            !double.IsFinite(confidence) || confidence is < 0 or > 1 || priority is < 1 or > 5 ||
            string.IsNullOrWhiteSpace(reason) || reason.Length > 2000 || string.IsNullOrWhiteSpace(intent) || intent.Length > 4000)
            throw new ArgumentException("Provide a valid action, confidence, priority, reason and intent.");
        await authoring.RequireRefAsync($"pr:{prId}", ct);
        if (!(await sync.GetBoardAsync(ct: ct)).Any(p => p.Id == prId))
            throw new ArgumentException("PR must be watched and open.");
        await using var db = await database.OpenAsync(ct);
        var hash = Guid.NewGuid().ToString();
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO verdict(pr_id,decider,action,confidence,reason,intent,priority,input_hash,created_at)
            VALUES(@prId,'mcp',@action,@confidence,@reason,@intent,@priority,@hash,@at)
            """, new { prId, action = parsed.ToString().ToLowerInvariant(), confidence, reason, intent, priority, hash, at = clock.GetUtcNow().ToString("O") },
            cancellationToken: ct));
        events.Publish("verdict.ready", new { id = prId }); return Json(new { proposed = true });
    }
    [McpServerTool(Name = "queue_action"), Description("Queue a local GitHub action for human review. No Apply tool exists. Merge captures live head using the shared queue service.")]
    public async Task<string> QueueAction(string prId, string kind, string? text, string? reviewer, CancellationToken ct)
    {
        return Json(await actions.QueueAsync(prId, kind, new ActionPayload(text, reviewer), ct));
    }
}
