using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public static partial class SyncEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,38}$")]
    private static partial Regex OwnerName();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoName();

    public static void MapSyncEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/settings", async (ISyncStore store, CancellationToken ct) => Results.Ok(await store.GetSettingsAsync(ct)));
        api.MapPut("/settings", async (WatchedSettings input, ISyncStore store, EventHub events, CancellationToken ct) =>
        {
            if (input.Owners is null || input.Repos is null ||
                input.BotLogins is null || input.BotLogins.Any(b => string.IsNullOrWhiteSpace(b) || b.Length > 100 || b.Any(char.IsWhiteSpace)) ||
                input.Owners.Any(o => o is null || !OwnerName().IsMatch(o.Id ?? "") || o.Kind is not ("org" or "user")) ||
                input.Repos.Any(r => r is null || !RepoName().IsMatch(r.Id ?? "")) ||
                input.Owners.Select(o => o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Owners.Count ||
                input.Repos.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Repos.Count)
                return Results.BadRequest(new { error = "Supply unique valid owner names (kind org/user) and owner/repo names." });
            if (input.AiBudget is < 0 or > 100)
                return Results.BadRequest(new { error = "AI monthly budget must be between $0 and $100." });
            await store.SaveSettingsAsync(input, ct);
            events.Publish("prs.changed", new { ids = Array.Empty<string>() });
            return Results.Ok(await store.GetSettingsAsync(ct));
        });
        api.MapPost("/sync", async (SyncService sync, ILogger<SyncService> logger, CancellationToken ct) =>
        {
            try { return Results.Ok(await sync.RunAsync(ct)); }
            catch (SyncInProgressException e) { return Results.Conflict(new { error = e.Message }); }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or System.ComponentModel.Win32Exception)
            {
                logger.LogError(e, "GitHub sync failed; completed scopes remain saved");
                return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });
        api.MapGet("/board", async (SyncStore store, AuthoringStore authoring, CancellationToken ct) =>
        {
            var cards = await store.GetBoardAsync(ct: ct);
            var own = await authoring.GetAsync(ct);
            return Results.Ok(new { zones = cards.Select(c => c.RepoId).Distinct(), cards,
                batches = cards.Where(c => c.BatchId is not null).GroupBy(c => c.BatchId)
                    .Select(g => new { id = g.Key, repoId = g.First().RepoId, prIds = g.Select(c => c.Id) }),
                stickies = own.Stickies, notes = own.Notes, strings = await store.GetStringsAsync(ct), positions = own.Positions,
                cutStrings = (await store.GetStringsAsync(ct, includeCut: true)).Where(l => l.Status == "cut"),
                lastSyncAt = await store.GetLastSyncAsync(ct) });
        });
        api.MapGet("/prs/{owner}/{repo}/{number:int}", async (string owner, string repo, int number, SyncStore store, CancellationToken ct) =>
        {
            var context = await store.GetCardContextAsync($"{owner}/{repo}#{number}", ct);
            return context is null ? Results.NotFound(new { error = "PR not found in watched repositories." }) : Results.Ok(context);
        });
        api.MapGet("/events", (HttpContext context, EventHub events) =>
        {
            var value = context.Request.Headers["Last-Event-ID"].ToString();
            if (value.Length > 0 && (!long.TryParse(value, out var parsed) || parsed < 0))
                return Results.BadRequest(new { error = "Last-Event-ID must be a nonnegative integer." });
            context.Response.Headers.CacheControl = "no-cache, no-store";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            return Results.ServerSentEvents(events.Subscribe(value.Length == 0 ? null : long.Parse(value), context.RequestAborted));
        });
        api.MapGet("/ai/status", async (Tangle.Data.Database database, Microsoft.Extensions.Configuration.IConfiguration config, CancellationToken ct) =>
        {
            await using var db = await database.OpenAsync(ct);
            var calls = await Dapper.SqlMapper.QueryAsync(db, new Dapper.CommandDefinition(
                """
                SELECT decider,input_tokens AS inputTokens,output_tokens AS outputTokens,cost,latency_ms AS latencyMs,
                  status,error,created_at AS createdAt,job,targets,outcome,pr_id AS prId,reserved_cost AS reservedCost
                FROM ai_call ORDER BY created_at DESC LIMIT 50
                """, cancellationToken: ct));
            return Results.Ok(new { enabled = config.GetValue<bool>("Ai:Enabled"), calls });
        });
    }
}
