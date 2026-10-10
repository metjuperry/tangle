using Tangle.Core;
using System.Text.Json;

namespace Tangle.Core.Tests;

public sealed class LinkRulesTests
{
    [Theory]
    [InlineData("See #2")]
    [InlineData("Depends on example/repo#2")]
    [InlineData("https://github.com/example/repo/pull/2")]
    public void ExplicitReferencesLinkExistingOpenPrs(string body)
    {
        var a = SyncTests.Pr() with { Body = body, Files = [] };
        var b = SyncTests.Pr(2) with { Files = [], HeadRef = "another" };
        var link = Assert.Single(LinkRules.Find([a, b]));
        Assert.Equal("explicit", link.Rule);
        Assert.Equal("pr:example/repo#1", link.ARef);
        Assert.Equal("pr:example/repo#2", link.BRef);
        Assert.Empty(LinkRules.Find([a, b with { State = "CLOSED" }]));
    }

    [Fact]
    public void CrossRepoReferencesAndCommitCommentsAreRead()
    {
        var a = SyncTests.Pr() with { Files = [], Body = "", Events = [
            new("2026-10-09T00:00:00Z", "viewer", "commit", """{"commit":{"message":"other/repo#2"}}""")
        ] };
        var b = SyncTests.Pr(2) with { Id = "other/repo#2", RepoId = "other/repo", Files = [] };
        Assert.Equal("explicit", Assert.Single(LinkRules.Find([a, b])).Rule);
        var comment = a with { Events = [new("2026-10-09T00:00:00Z", "viewer", "comment", """{"body":"other/repo#2"}""")] };
        Assert.Single(LinkRules.Find([comment, b]));
        Assert.Empty(LinkRules.Find([a with { Body = "#999 #1", Events = [] }, b]));
    }

    [Fact]
    public void StacksAreSameRepoAndWinOverOverlap()
    {
        var a = SyncTests.Pr();
        var b = SyncTests.Pr(2) with { BaseRef = a.HeadRef, HeadRef = "other" };
        Assert.Equal("stack", Assert.Single(LinkRules.Find([a, b])).Rule);
        Assert.Empty(LinkRules.Find([a, b with { RepoId = "different/repo", Id = "different/repo#2" }]));
    }

    [Fact]
    public void FileOverlapIsExactAndDeterministic()
    {
        var a = SyncTests.Pr() with { Files = ["src/file.cs", "same.cs"] };
        var b = SyncTests.Pr(2) with { HeadRef = "other", Files = ["same.cs"] };
        var link = Assert.Single(LinkRules.Find([a, b]));
        Assert.Equal("file_overlap", link.Rule);
        Assert.Equal("shared changed files: same.cs", link.Reason);
        Assert.Equal(link, Assert.Single(LinkRules.Find([b, a])));
        Assert.Empty(LinkRules.Find([a, b with { Files = ["SAME.cs"] }]));
    }

    [Fact]
    public void BatchNeedsFiveMatchingTitlesOneAuthorRepoAndInclusiveFourteenDays()
    {
        var prs = Enumerable.Range(1, 5).Select(n => SyncTests.Pr(n) with
        {
            Title = $"feat: scaffold applier for pp-{n}", CreatedAt = $"2026-09-{n + 1:00}T00:00:00Z"
        }).ToArray();
        Assert.Single(LinkRules.Batches(prs));
        Assert.Empty(LinkRules.Batches(prs[..4]));
        Assert.Empty(LinkRules.Batches(prs.Select((p, i) => i == 4 ? p with { Author = "other" } : p).ToArray()));
        Assert.Empty(LinkRules.Batches(prs.Select((p, i) => i == 4 ? p with { RepoId = "other/repo" } : p).ToArray()));
        Assert.Empty(LinkRules.Batches(prs.Select((p, i) => i == 4 ? p with { Title = "different work" } : p).ToArray()));
        var edge = prs.Select((p, i) => i == 4 ? p with { CreatedAt = "2026-09-16T00:00:00Z" } : p).ToArray();
        Assert.Single(LinkRules.Batches(edge));
        Assert.Empty(LinkRules.Batches(edge.Select((p, i) => i == 4 ? p with { CreatedAt = "2026-09-16T00:00:01Z" } : p).ToArray()));
        Assert.Equal(LinkRules.Batches(prs).Single().Id, LinkRules.Batches(prs.Reverse().ToArray()).Single().Id);
    }

    [Fact]
    public void RecordedScaffoldAppliersCollapseIntoOneBatch()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "scaffold-batches.json")));
        var prs = fixture.RootElement.EnumerateArray().Select(p => SyncTests.Pr(p.GetProperty("number").GetInt32()) with
        {
            Title = p.GetProperty("title").GetString()!,
            CreatedAt = p.GetProperty("createdAt").GetString()!,
            Author = p.GetProperty("author").GetString()!
        }).ToArray();
        var batch = Assert.Single(LinkRules.Batches(prs));
        Assert.Equal(33, batch.PrIds.Count);
        Assert.DoesNotContain("example/repo#94", batch.PrIds);
        Assert.DoesNotContain("example/repo#102", batch.PrIds);
        Assert.True(prs.Where(p => batch.PrIds.Contains(p.Id)).Max(p => FactsEngine.At(p.CreatedAt)) -
            prs.Where(p => batch.PrIds.Contains(p.Id)).Min(p => FactsEngine.At(p.CreatedAt)) <= TimeSpan.FromDays(14));
    }
}
