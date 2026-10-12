using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Tangle.Data;
using Tangle.Core;
using System.Text.Json.Nodes;

namespace Tangle.Web.Tests;

public sealed class EvaluationApiTests
{
    [Fact]
    public async Task ReportUsesHistoricPreDecisionJudgmentAndNeverCallsProvider()
    {
        using var factory = new TangleFactory();
        using var scope = factory.Services.CreateScope();
        using var client = factory.LocalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/evaluation")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", TangleFactory.Key);
        await using var db = await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();
        await db.ExecuteAsync("""
            INSERT INTO repo(id) VALUES('example/repo');
            INSERT INTO pr(id,repo_id,number) VALUES('example/repo#1','example/repo',1);
            INSERT INTO verdict(pr_id,decider,action,confidence,reason,intent,priority,input_hash,created_at)
              VALUES('example/repo#1','haiku-5-5','fix',0.8,'reason','intent',4,'before','2026-10-01T00:00:00Z'),
                    ('example/repo#1','haiku-5-5','merge',0.9,'later','intent',4,'after','2026-10-03T00:00:00Z');
            INSERT INTO decision(id,pr_id,choice,created_at) VALUES('decision','example/repo#1','fix','2026-10-02T00:00:00Z');
            """);
        var report = JsonNode.Parse(await client.GetStringAsync("/api/evaluation"))!;
        Assert.Equal(1, report["labelledExamples"]!.GetValue<int>());
        Assert.Equal(1, report["stats"]![0]!["agreement"]!.GetValue<double>());
        Assert.Equal(29, report["needed"]!.GetValue<int>());
        Assert.Contains("# Tangle evaluation", await client.GetStringAsync("/api/evaluation/report"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/evaluation/replay", new { decider = "haiku-5-5", confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/evaluation/replay", new { decider = "haiku-5-5", confirmed = true })).StatusCode);
    }
}
