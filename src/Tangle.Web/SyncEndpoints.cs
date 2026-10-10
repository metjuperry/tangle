using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
        api.MapPut("/settings", async (WatchedSettings input, ISyncStore store, CancellationToken ct) =>
        {
            if (input.Owners is null || input.Repos is null ||
                input.Owners.Any(o => o is null || !OwnerName().IsMatch(o.Id ?? "") || o.Kind is not ("org" or "user")) ||
                input.Repos.Any(r => r is null || !RepoName().IsMatch(r.Id ?? "")) ||
                input.Owners.Select(o => o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Owners.Count ||
                input.Repos.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Repos.Count)
                return Results.BadRequest(new { error = "Supply unique valid owner names (kind org/user) and owner/repo names." });
            if (input.Repos.Any(r => r.AiAllowed))
                return Results.BadRequest(new { error = "AI is unavailable until M5; all repo switches must remain off." });
            await store.SaveSettingsAsync(input, ct);
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
        api.MapGet("/board", async (SyncStore store, CancellationToken ct) =>
        {
            var cards = await store.GetBoardAsync(ct: ct);
            return Results.Ok(new { zones = cards.Select(c => c.RepoId).Distinct(), cards,
                stickies = Array.Empty<object>(), strings = Array.Empty<object>(), positions = Array.Empty<object>() });
        });
        api.MapGet("/debug/prs", async (SyncStore store, CancellationToken ct) =>
            Results.Ok(await store.GetBoardAsync(includeClosed: true, ct: ct)));
    }
}
