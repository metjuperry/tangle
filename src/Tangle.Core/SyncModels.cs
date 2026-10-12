namespace Tangle.Core;

public sealed record OwnerSetting(string Id, string Kind = "org", bool Watched = true)
{
    public OwnerSetting() : this("") { }
}
public sealed record RepoSetting(string Id, bool Watched = true, bool AiAllowed = false)
{
    public RepoSetting() : this("") { }
}
public sealed record WatchedSettings(IReadOnlyList<OwnerSetting> Owners, IReadOnlyList<RepoSetting> Repos)
{
    public IReadOnlyList<string> BotLogins { get; init; } = ["dependabot", "dependabot[bot]", "github-actions", "github-actions[bot]"];
    public decimal AiBudget { get; init; } = 1m;
    public string AiVerdicts { get; init; } = "haiku-5-5";
    public string AiPairs { get; init; } = "haiku-5-5";
    public string AiExplain { get; init; } = "haiku-5-5";
}
public sealed record PrEvent(string At, string? Actor, string Kind, string Detail)
{
    public PrEvent() : this("", null, "", "") { }
    public string? ActorDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Actor)) return Actor;
            if (Kind != "commit") return null;
            using var json = System.Text.Json.JsonDocument.Parse(Detail);
            if (json.RootElement.TryGetProperty("commit", out var commit) &&
                commit.TryGetProperty("author", out var author) && author.ValueKind == System.Text.Json.JsonValueKind.Object &&
                author.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var value = name.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return $"{value} (unlinked commit author)";
            }
            return null;
        }
    }
}
public sealed record ReviewThread(string Id, string? Path, bool IsResolved, string? LastActor, string? LastAt, string? Excerpt)
{
    public ReviewThread() : this("", null, false, null, null, null) { }
}
public sealed record ReviewRequest(string Reviewer, string Kind)
{
    public ReviewRequest() : this("", "") { }
}

public sealed record PullRequest(
    string Id, string RepoId, int Number, string Title, string Body, string? Author, bool IsMine,
    string State, bool IsDraft, string HeadRef, string BaseRef, string CreatedAt, string UpdatedAt,
    string? ClosedAt, int Additions, int Deletions, int ChangedFiles, string Mergeable,
    string? ReviewDecision, string? ChecksState, IReadOnlyList<string> Files,
    IReadOnlyList<PrEvent> Events, IReadOnlyList<ReviewThread> Threads, IReadOnlyList<ReviewRequest> ReviewRequests,
    string ContentHash, string Viewer = "")
{
    public PullRequest() : this("", "", 0, "", "", null, false, "", false, "", "", "", "",
        null, 0, 0, 0, "", null, null, [], [], [], [], "") { }
}

public sealed record PrPage(IReadOnlyList<PullRequest> Prs, bool HasNextPage, string? EndCursor,
    int Cost, int Remaining, string ResetAt, int CallCount = 1);
public interface IGitHubReader
{
    Task<string> GetViewerAsync(string owner, CancellationToken ct);
    Task<PrPage> SearchAsync(string owner, string query, string? after, string viewer, CancellationToken ct);
}

public interface ISyncStore
{
    Task<WatchedSettings> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(WatchedSettings settings, CancellationToken ct = default);
    Task<string?> GetCursorAsync(string scope, CancellationToken ct);
    Task<string?> GetOldestOpenUpdateAsync(string owner, string? repo, CancellationToken ct);
    Task<int> SaveScopeAsync(string scope, IReadOnlyList<PullRequest> prs, string cursor, CancellationToken ct);
}
public sealed record SyncResult(int Fetched, int Changed, int Calls, int Cost);

public sealed class SyncService(IGitHubReader github, ISyncStore store, TimeProvider clock, ILiveEvents? events = null, IAiQueue? ai = null,
    SemaphoreSlim? syncGate = null)
{
    private readonly SemaphoreSlim gate = syncGate ?? new(1, 1);
    public async Task<SyncResult> RunAsync(CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) throw new SyncInProgressException();
        try
        {
            events?.Publish("sync.started", new { });
            var settings = await store.GetSettingsAsync(ct);
            var scopes = settings.Owners.Where(o => o.Watched)
                .Select(o => (Owner: o.Id, Filter: $"{(o.Kind == "user" ? "user" : "org")}:{o.Id}"))
                .Concat(settings.Repos.Where(r => r.Watched && !settings.Owners.Any(o =>
                        string.Equals(o.Id, r.Id.Split('/')[0], StringComparison.OrdinalIgnoreCase)))
                    .Select(r => (Owner: r.Id.Split('/')[0], Filter: $"repo:{r.Id}")))
                .Distinct().ToArray();
            if (scopes.Length == 0)
                throw new InvalidOperationException("No watched scopes. Add an owner or repository in Settings before syncing.");
            var fetched = 0; var changed = 0; var calls = 0; var cost = 0;
            foreach (var scope in scopes)
            {
                var started = clock.GetUtcNow();
                var viewer = await github.GetViewerAsync(scope.Owner, ct);
                var scopeKey = $"involves:{viewer} {scope.Filter}";
                var cursor = await store.GetCursorAsync(scopeKey, ct);
                var oldest = cursor is null ? await store.GetOldestOpenUpdateAsync(scope.Owner,
                    scope.Filter.StartsWith("repo:", StringComparison.Ordinal) ? scope.Filter[5..] : null, ct) : null;
                var window = cursor is null ? "" : $" updated:>={DateTimeOffset.Parse(cursor).AddSeconds(-1):yyyy-MM-ddTHH:mm:ssZ}";
                var excluded = string.Join("", settings.Repos.Where(r => !r.Watched &&
                    string.Equals(r.Id.Split('/')[0], scope.Owner, StringComparison.OrdinalIgnoreCase))
                    .Select(r => $" -repo:{r.Id}"));
                var prs = new Dictionary<string, PullRequest>(StringComparer.Ordinal);
                foreach (var state in cursor is null && oldest is null ? new[] { "open" } : new[] { "open", "closed" })
                {
                    string? after = null;
                    var cursors = new HashSet<string>();
                    do
                    {
                        var stateWindow = cursor is null && state == "closed"
                            ? $" updated:>={DateTimeOffset.Parse(oldest!).AddSeconds(-1):yyyy-MM-ddTHH:mm:ssZ}" : window;
                        var page = await github.SearchAsync(scope.Owner,
                            $"is:pr is:{state} involves:{viewer} archived:false {scope.Filter}{excluded}{stateWindow}", after, viewer, ct);
                        fetched += page.Prs.Count; calls += page.CallCount; cost += page.Cost;
                        events?.Publish("sync.progress", new { scope = scope.Filter, fetched, calls, cost });
                        foreach (var pr in page.Prs) prs[pr.Id] = pr;
                        if (!page.HasNextPage) break;
                        if (string.IsNullOrEmpty(page.EndCursor) || !cursors.Add(page.EndCursor))
                            throw new InvalidOperationException("GitHub returned a missing or repeated pagination cursor.");
                        after = page.EndCursor;
                    } while (true);
                }
                changed += await store.SaveScopeAsync(scopeKey, prs.Values.ToArray(), started.ToString("O"), ct);
                events?.Publish("prs.changed", new { ids = prs.Keys.ToArray() });
            }
            events?.Publish("sync.finished", new { fetched, changed, calls, cost, success = true });
            ai?.Enqueue();
            return new(fetched, changed, calls, cost);
        }
        catch (Exception)
        {
            events?.Publish("sync.finished", new { success = false });
            throw;
        }
        finally { gate.Release(); }
    }
}

public sealed class SyncInProgressException() : Exception("A sync is already running.");
