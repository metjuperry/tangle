using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tangle.Core;

namespace Tangle.Web;

public static class EvaluationEndpoints
{
    public sealed record ReplayInput(string Decider, bool Confirmed);
    public static void MapEvaluationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/evaluation").RequireAuthorization();
        api.MapGet("/", async (IEvaluationReports reports, CancellationToken ct) => Results.Ok(await reports.GetAsync(ct)));
        api.MapGet("/report", async (IEvaluationReports reports, CancellationToken ct) =>
            Results.Text(await reports.MarkdownAsync(ct), "text/markdown"));
        app.MapPost("/api/prs/{owner}/{repo}/{number:int}/explain", async (string owner, string repo, int number,
            IExplanationService service, IConfiguration config, CancellationToken ct) =>
        {
            if (!config.GetValue<bool>("Ai:Enabled")) return Results.Json(new { error = "Provider calls disabled." }, statusCode: 403);
            try { await service.ExplainAsync($"{owner}/{repo}#{number}", ct); return Results.Ok(new { explained = true }); }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
            { return Results.Json(new { error = e.Message }, statusCode: 502); }
        }).RequireAuthorization();
        api.MapPost("/replay", async (ReplayInput input, IEvaluationReports reports, IConfiguration config, ILiveEvents events,
            ILogger<IEvaluationReports> logger, CancellationToken ct) =>
        {
            if (!input.Confirmed) return Results.BadRequest(new { error = "Confirm paid evaluation before replay." });
            if (!config.GetValue<bool>("Ai:Enabled")) return Results.Json(new { error = "Provider calls are disabled." }, statusCode: 403);
            try
            {
                var calls = await reports.ReplayAsync(input.Decider, ct);
                events.Publish("evaluation.ready", new { });
                return Results.Ok(new { calls });
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or HttpRequestException)
            {
                logger.LogError(e, "Evaluation replay failed; saved predictions and authored data are unchanged");
                events.Publish("evaluation.ready", new { });
                return Results.Json(new { error = e.Message }, statusCode: 502);
            }
        });
    }
}
