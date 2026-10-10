using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Tangle.Core;

namespace Tangle.GitHub;

public sealed class GitHubActions(HttpClient http, GitHubTokens tokens) : IGitHubActions
{
    public async Task<GitHubActionTarget> InspectAsync(string repo, int number, CancellationToken ct)
    {
        var parts = repo.Split('/');
        var data = await Call(repo, """
            query($owner:String!,$repo:String!,$number:Int!){
              viewer{login} repository(owner:$owner,name:$repo){pullRequest(number:$number){id state headRefOid author{login}}}
            }
            """, new { owner = parts[0], repo = parts[1], number }, ct);
        var pr = data["repository"]?["pullRequest"] ?? throw new InvalidOperationException("GitHub PR not found.");
        return new(pr["id"]!.GetValue<string>(), pr["headRefOid"]!.GetValue<string>(), pr["state"]!.GetValue<string>(),
            pr["author"]?["login"]?.GetValue<string>() ?? "", data["viewer"]!["login"]!.GetValue<string>());
    }
    public async Task<string> ApplyAsync(string repo, GitHubActionTarget target, string kind, ActionPayload payload, CancellationToken ct)
    {
        if (target.State != "OPEN") throw new InvalidOperationException("GitHub PR is no longer open.");
        if (kind is "close" or "merge" && !FactsEngine.Same(target.Author, target.Viewer))
            throw new InvalidOperationException("Closing or merging another person's PR is not allowed; queue a comment instead.");
        if (kind == "merge" && payload.HeadSha != target.HeadSha) throw new InvalidOperationException("PR head changed; queue and confirm the merge again.");
        var query = kind switch
        {
            "comment" => "mutation($id:ID!,$text:String!){addComment(input:{subjectId:$id,body:$text}){commentEdge{node{url}}}}",
            "close" => "mutation($id:ID!){closePullRequest(input:{pullRequestId:$id}){pullRequest{url}}}",
            "merge" => "mutation($id:ID!,$sha:GitObjectID!){mergePullRequest(input:{pullRequestId:$id,expectedHeadOid:$sha,mergeMethod:MERGE}){pullRequest{url}}}",
            "request_review" => "mutation($id:ID!,$reviewer:ID!){requestReviews(input:{pullRequestId:$id,userIds:[$reviewer]}){pullRequest{url}}}",
            _ => throw new ArgumentException("Unsupported GitHub action.")
        };
        if (kind == "request_review")
        {
            var user = await Call(repo, "query($login:String!){user(login:$login){id}}", new { login = payload.Reviewer }, ct);
            var reviewer = user["user"]?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Reviewer not found.");
            return (await Call(repo, query, new { id = target.Id, reviewer }, ct)).ToJsonString();
        }
        return (await Call(repo, query, new { id = target.Id, text = payload.Text, sha = payload.HeadSha }, ct)).ToJsonString();
    }
    private async Task<JsonNode> Call(string repo, string query, object variables, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(repo.Split('/')[0], ct));
        request.Headers.UserAgent.ParseAdd("Tangle/0.1");
        request.Content = JsonContent.Create(new { query, variables });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"GitHub action returned HTTP {(int)response.StatusCode}. No automatic retry.");
        var body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct) ?? throw new InvalidOperationException("Empty GitHub action response.");
        if (body["errors"] is JsonArray errors && errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors.Select(e => e?["message"]?.GetValue<string>())));
        return body["data"] ?? throw new InvalidOperationException("GitHub action response has no data.");
    }
}
