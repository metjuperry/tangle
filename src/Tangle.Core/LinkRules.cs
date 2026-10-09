using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tangle.Core;

public sealed record RuleString(string Id, string ARef, string BRef, string Rule, string Reason);
public sealed record PrBatch(string Id, string RepoId, string Author, string Pattern, IReadOnlyList<string> PrIds);

public static partial class LinkRules
{
    public static IReadOnlyList<RuleString> Find(IReadOnlyList<PullRequest> prs)
    {
        var open = prs.Where(p => p.State == "OPEN").OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        var ids = open.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var links = new Dictionary<string, RuleString>();
        void Add(PullRequest a, PullRequest b, string rule, string reason)
        {
            if (a.Id == b.Id) return;
            var refs = new[] { $"pr:{a.Id}", $"pr:{b.Id}" }.Order(StringComparer.Ordinal).ToArray();
            var id = StableId("rule", refs[0] + "\n" + refs[1]);
            links.TryAdd(id, new(id, refs[0], refs[1], rule, reason));
        }
        foreach (var pr in open)
        {
            var text = pr.Body + "\n" + string.Join("\n", pr.Events
                .Where(e => e.Kind is "comment" or "review" or "commit").Select(FactsEngine.EventText))
                + "\n" + string.Join("\n", pr.Threads.Select(t => t.Excerpt));
            foreach (Match match in Reference().Matches(text))
            {
                var repo = match.Groups["urlRepo"].Success ? match.Groups["urlRepo"].Value :
                    match.Groups["repo"].Success ? match.Groups["repo"].Value : pr.RepoId;
                var number = match.Groups["urlNumber"].Success ? match.Groups["urlNumber"].Value : match.Groups["number"].Value;
                if (ids.TryGetValue($"{repo}#{number}", out var target))
                    Add(pr, target, "explicit", $"{pr.Id} references {target.Id}");
            }
        }
        for (var i = 0; i < open.Length; i++)
        for (var j = i + 1; j < open.Length; j++)
        {
            var a = open[i]; var b = open[j];
            if (!FactsEngine.Same(a.RepoId, b.RepoId)) continue;
            if (a.BaseRef == b.HeadRef || b.BaseRef == a.HeadRef)
                Add(a, b, "stack", "one PR targets the other's head branch");
            var paths = a.Files.Intersect(b.Files, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (paths.Length > 0) Add(a, b, "file_overlap", $"shared changed files: {string.Join(", ", paths)}");
        }
        return links.Values.OrderBy(l => l.Id, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<PrBatch> Batches(IReadOnlyList<PullRequest> prs)
    {
        var batches = new List<PrBatch>();
        foreach (var group in prs.Where(p => p.State == "OPEN" && !string.IsNullOrEmpty(p.Author))
            .GroupBy(p => (Repo: p.RepoId.ToLowerInvariant(), Author: p.Author!.ToLowerInvariant(), Pattern: TitlePattern(p.Title))))
        {
            if (group.Key.Pattern.Length == 0) continue;
            var ordered = group.OrderBy(p => FactsEngine.At(p.CreatedAt)).ThenBy(p => p.Id, StringComparer.Ordinal).ToArray();
            var start = 0;
            while (start < ordered.Length)
            {
                var end = start;
                while (end < ordered.Length && FactsEngine.At(ordered[end].CreatedAt) - FactsEngine.At(ordered[start].CreatedAt) <= TimeSpan.FromDays(14))
                    end++;
                if (end - start < 5) { start++; continue; }
                var members = ordered[start..end];
                var id = StableId("batch", string.Join("\n", members.Select(p => p.Id).Order(StringComparer.Ordinal)));
                batches.Add(new(id, members[0].RepoId, members[0].Author!, group.Key.Pattern, members.Select(p => p.Id).ToArray()));
                start = end;
            }
        }
        return batches.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
    }

    public static string TitlePattern(string title)
    {
        var scaffold = ScaffoldTitle().Match(title);
        if (scaffold.Success) return $"{scaffold.Groups["prefix"].Value.ToLowerInvariant()}: scaffold applier for <id>";
        return Whitespace().Replace(Identifier().Replace(title.ToLowerInvariant(), "<id>"), " ").Trim();
    }
    private static string StableId(string prefix, string text) =>
        prefix + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [GeneratedRegex(@"https://github\.com/(?<urlRepo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)/pull/(?<urlNumber>\d+)\b|(?<![\w/#])(?:(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)\b")]
    private static partial Regex Reference();
    [GeneratedRegex(@"`[^`]+`|\b[a-z0-9]+(?:[-_/.][a-z0-9]+)+\b|\b\d+(?:\.\d+)*\b")]
    private static partial Regex Identifier();
    [GeneratedRegex(@"^(?<prefix>feat(?:\([^)]*\))?): .+ scaffold applier for \S+$", RegexOptions.IgnoreCase)]
    private static partial Regex ScaffoldTitle();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
