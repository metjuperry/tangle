using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tangle.Core;

namespace Tangle.GitHub;

public sealed class GitHubTokens(IConfiguration configuration)
{
    public async Task<string> GetAsync(string owner, CancellationToken ct)
    {
        var token = configuration[$"GitHub:Tokens:{owner}"] ?? configuration["GitHub:Token"];
        if (!string.IsNullOrWhiteSpace(token)) return token;
        using var process = new Process
        {
            StartInfo = new("gh")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("auth");
        process.StartInfo.ArgumentList.Add("token");
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        _ = await error;
        token = (await output).Trim();
        if (process.ExitCode != 0 || token.Length == 0)
            throw new InvalidOperationException($"No GitHub credential for {owner}. Run gh auth login or set GitHub:Tokens:{owner} in user-secrets.");
        return token;
    }
}

public sealed class GitHubReader(HttpClient http, GitHubTokens tokens, ILogger<GitHubReader> logger) : IGitHubReader
{
    private readonly Dictionary<string, string> viewers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string Query = LoadQuery("Prs");
    private static readonly string ChangedQuery = LoadQuery("ChangedPrs");
    private static string LoadQuery(string name)
    {
        using var reader = new StreamReader(typeof(GitHubReader).Assembly.GetManifestResourceStream($"Tangle.GitHub.queries.{name}.graphql")!);
        return reader.ReadToEnd();
    }

    public async Task<string> GetViewerAsync(string owner, CancellationToken ct)
    {
        if (viewers.TryGetValue(owner, out var viewer)) return viewer;
        var data = await CallAsync(owner, "query { viewer { login } rateLimit { cost remaining resetAt } }", new { }, ct);
        viewer = data["viewer"]?["login"]?.GetValue<string>() ?? throw new InvalidOperationException("GitHub viewer is missing.");
        viewers[owner] = viewer;
        return viewer;
    }

    public async Task<PrPage> SearchAsync(string owner, string query, string? after, string viewer, CancellationToken ct)
    {
        var metrics = new QueryMetrics();
        JsonNode data;
        if (query.Contains(" updated:>=", StringComparison.Ordinal))
        {
            data = await CallAsync(owner, ChangedQuery, new { q = query, after }, ct, metrics);
            if (data["search"]!["issueCount"]!.GetValue<int>() > 1000)
                throw new InvalidOperationException("GitHub search exceeds its 1,000-result limit. Narrow watched scopes before syncing.");
            if (data["search"]!["nodes"]!.AsArray().Count == 0)
            {
                var emptyPage = data["search"]!["pageInfo"]!;
                return new([], emptyPage["hasNextPage"]!.GetValue<bool>(), emptyPage["endCursor"]?.GetValue<string>(),
                    metrics.Cost, data["rateLimit"]!["remaining"]!.GetValue<int>(),
                    data["rateLimit"]!["resetAt"]!.GetValue<string>(), metrics.Calls);
            }
        }
        data = await CallAsync(owner, Query, new { q = query, after }, ct, metrics);
        var search = data["search"] ?? throw new InvalidOperationException("GitHub search is missing.");
        if (search["issueCount"]!.GetValue<int>() > 1000)
            throw new InvalidOperationException("GitHub search exceeds its 1,000-result limit. Narrow watched scopes before syncing.");
        var prs = new List<PullRequest>();
        foreach (var node in search["nodes"]!.AsArray())
        {
            if (node is null || node["number"] is null) continue;
            foreach (var connection in new[] { "files", "reviewRequests", "reviewThreads", "timelineItems" })
                await CompleteConnectionAsync(owner, node, connection, ct, metrics);
            prs.Add(Map(node, viewer));
        }
        var page = search["pageInfo"]!;
        var rate = data["rateLimit"]!;
        return new(prs, page["hasNextPage"]!.GetValue<bool>(), page["endCursor"]?.GetValue<string>(),
            metrics.Cost, metrics.Remaining, rate["resetAt"]!.GetValue<string>(), metrics.Calls);
    }

    private async Task CompleteConnectionAsync(string owner, JsonNode pr, string name, CancellationToken ct, QueryMetrics metrics)
    {
        var backward = name == "timelineItems";
        var more = backward ? "hasPreviousPage" : "hasNextPage";
        var cursorKey = backward ? "startCursor" : "endCursor";
        var connection = pr[name]!;
        var seen = new HashSet<string>();
        while (connection["pageInfo"]![more]!.GetValue<bool>())
        {
            var cursor = connection["pageInfo"]![cursorKey]?.GetValue<string>();
            if (cursor is null || !seen.Add(cursor)) throw new InvalidOperationException($"Missing or repeated {name} cursor.");
            var fields = name switch
            {
                "files" => "nodes { path }",
                "reviewRequests" => "nodes { requestedReviewer { __typename ... on User { login } ... on Team { slug } } }",
                "reviewThreads" => "nodes { id isResolved path comments(last:1) { nodes { author { login } createdAt bodyText } } }",
                _ => """
                    nodes { __typename
                      ... on IssueComment { createdAt author { login } body }
                      ... on PullRequestReview { createdAt submittedAt author { login } body state }
                      ... on ReviewRequestedEvent { createdAt actor { login } requestedReviewer { __typename ... on User { login } ... on Team { slug } } }
                      ... on PullRequestCommit { commit { committedDate message author { user { login } } } }
                    }
                    """
            };
            var args = backward
                ? "last:30,before:$cursor,itemTypes:[ISSUE_COMMENT,PULL_REQUEST_REVIEW,REVIEW_REQUESTED_EVENT,PULL_REQUEST_COMMIT]"
                : "first:100,after:$cursor";
            var query = $$"""
                query More($id:ID!,$cursor:String!) {
                  node(id:$id) { ... on PullRequest {
                    {{name}}({{args}}) { pageInfo { {{more}} {{cursorKey}} } {{fields}} }
                  } }
                  rateLimit { cost remaining resetAt }
                }
                """;
            var data = await CallAsync(owner, query, new { id = pr["id"]!.GetValue<string>(), cursor }, ct, metrics);
            var next = data["node"]?[name] ?? throw new InvalidOperationException($"Missing {name} continuation.");
            foreach (var item in next["nodes"]!.AsArray())
                connection["nodes"]!.AsArray().Add(item?.DeepClone());
            connection["pageInfo"] = next["pageInfo"]!.DeepClone();
        }
    }

    private async Task<JsonNode> CallAsync(string owner, string query, object variables, CancellationToken ct, QueryMetrics? metrics = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(owner, ct));
        request.Headers.UserAgent.ParseAdd("Tangle/0.1");
        request.Content = JsonContent.Create(new { query, variables });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub returned HTTP {(int)response.StatusCode}. Check token access and rate limits.");
        var body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct)
            ?? throw new InvalidOperationException("GitHub returned an empty response.");
        if (body["errors"] is JsonArray errors && errors.Count != 0)
        {
            var messages = string.Join("; ", errors.Select(e => e?["message"]?.GetValue<string>()));
            throw new InvalidOperationException($"GitHub GraphQL failed: {messages}");
        }
        var data = body["data"] ?? throw new InvalidOperationException("GitHub data is missing.");
        var rate = data["rateLimit"]!;
        if (metrics is not null)
        {
            metrics.Calls++;
            metrics.Cost += rate["cost"]!.GetValue<int>();
            metrics.Remaining = rate["remaining"]!.GetValue<int>();
        }
        logger.LogInformation("GitHub query cost {Cost}; remaining {Remaining}; resets {Reset}",
            rate["cost"], rate["remaining"], rate["resetAt"]);
        return data;
    }

    private sealed class QueryMetrics
    {
        public int Calls { get; set; }
        public int Cost { get; set; }
        public int Remaining { get; set; }
    }

    internal static PullRequest Map(JsonNode node, string viewer)
    {
        string Text(string key) => node[key]?.GetValue<string>() ?? throw new InvalidOperationException($"PR {key} is missing.");
        var repo = node["repository"]!["nameWithOwner"]!.GetValue<string>();
        var number = node["number"]!.GetValue<int>();
        var author = node["author"]?["login"]?.GetValue<string>();
        var files = node["files"]!["nodes"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()).Distinct().Order().ToArray();
        var threads = node["reviewThreads"]!["nodes"]!.AsArray().Select(t =>
        {
            var comment = t!["comments"]!["nodes"]!.AsArray().LastOrDefault();
            return new ReviewThread(t["id"]!.GetValue<string>(), t["path"]?.GetValue<string>(), t["isResolved"]!.GetValue<bool>(),
                comment?["author"]?["login"]?.GetValue<string>(), comment?["createdAt"]?.GetValue<string>(), comment?["bodyText"]?.GetValue<string>());
        }).OrderBy(t => t.Id).ToArray();
        var reviewers = node["reviewRequests"]!["nodes"]!.AsArray().Select(r => r?["requestedReviewer"])
            .Where(r => r is not null).Select(r => new ReviewRequest(
                (r!["login"] ?? r["slug"])!.GetValue<string>(), r["__typename"]!.GetValue<string>()))
            .Distinct().OrderBy(r => r.Reviewer).ToArray();
        var events = new List<PrEvent>();
        foreach (var e in node["timelineItems"]!["nodes"]!.AsArray())
        {
            if (e is null) continue;
            var type = e["__typename"]!.GetValue<string>();
            var commit = e["commit"];
            var at = (e["submittedAt"] ?? e["createdAt"] ?? commit?["committedDate"])?.GetValue<string>();
            if (at is null) throw new InvalidOperationException("GitHub event timestamp is missing.");
            var actor = (e["author"]?["login"] ?? e["actor"]?["login"] ?? commit?["author"]?["user"]?["login"])?.GetValue<string>();
            var kind = type switch
            {
                "IssueComment" => "comment", "PullRequestReview" => "review",
                "ReviewRequestedEvent" => "review_requested", "PullRequestCommit" => "commit",
                _ => throw new InvalidOperationException($"Unexpected GitHub event {type}.")
            };
            events.Add(new(at, actor, kind, e.ToJsonString()));
        }
        var lastCommit = node["commits"]!["nodes"]!.AsArray().LastOrDefault()?["commit"];
        var checks = lastCommit?["statusCheckRollup"]?["state"]?.GetValue<string>();
        var pr = new PullRequest($"{repo}#{number}", repo, number, Text("title"), Text("body"), author,
            string.Equals(author, viewer, StringComparison.OrdinalIgnoreCase), Text("state"), node["isDraft"]!.GetValue<bool>(),
            Text("headRefName"), Text("baseRefName"), Text("createdAt"), Text("updatedAt"), node["closedAt"]?.GetValue<string>(),
            node["additions"]!.GetValue<int>(), node["deletions"]!.GetValue<int>(), node["changedFiles"]!.GetValue<int>(),
            Text("mergeable"), node["reviewDecision"]?.GetValue<string>(), checks, files,
            events.OrderBy(e => e.At).ThenBy(e => e.Kind).ThenBy(e => e.Actor).ThenBy(e => e.Detail).ToArray(), threads, reviewers, "", viewer);
        return pr with { ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pr)))) };
    }
}
