using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public static class ActionEndpoints
{
    public sealed record QueueInput(string PrId, string Kind, ActionPayload Payload);
    public sealed record DecisionInput(string PrId, string Choice, string? ActionId);
    public sealed record ConfirmInput(bool Confirmed);
    public static void MapActionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
        });
        api.MapGet("/actions", async (ActionStore store, CancellationToken ct) => Results.Ok(await store.ListAsync(ct)));
        api.MapGet("/receipts/history", async (ActionStore store, CancellationToken ct) => Results.Ok(await store.HistoryAsync(ct)));
        api.MapPost("/receipts/tear", async (ConfirmInput input, ActionStore store, EventHub events, CancellationToken ct) =>
        {
            if (!input.Confirmed) throw new ArgumentException("Confirm saving this round and resetting its decisions.");
            var roundId = await store.TearAsync(ct);
            events.Publish("receipt.saved", new { roundId });
            return Results.Ok(new { roundId });
        });
        api.MapPost("/actions", async (QueueInput input, ActionStore store, SyncStore sync, IGitHubActions github,
            EventHub events, ILogger<ActionStore> logger, CancellationToken ct) =>
        {
            if (input.Kind is not ("comment" or "request_review" or "close" or "merge") || input.Payload is null)
                throw new ArgumentException("Choose comment, request_review, close or merge.");
            if (input.Kind == "comment" && (string.IsNullOrWhiteSpace(input.Payload.Text) || input.Payload.Text.Length > 10000))
                throw new ArgumentException("Comment text is required, up to 10,000 characters.");
            if (input.Kind == "request_review" && (string.IsNullOrWhiteSpace(input.Payload.Reviewer) || !System.Text.RegularExpressions.Regex.IsMatch(input.Payload.Reviewer, "^[A-Za-z0-9-]{1,39}$")))
                throw new ArgumentException("A GitHub reviewer login is required.");
            var payload = input.Payload;
            if (input.Kind == "merge")
            {
                var card = (await sync.GetBoardAsync(ct: ct)).FirstOrDefault(c => c.Id == input.PrId);
                if (card is null) throw new ArgumentException("PR must be cached, watched and open.");
                try
                {
                    var target = await github.InspectAsync(card.RepoId, checked((int)card.Number), ct);
                    if (target.State != "OPEN") throw new InvalidOperationException("PR is no longer open.");
                    if (!FactsEngine.Same(target.Author, target.Viewer))
                        throw new InvalidOperationException("Merging another person's PR is not allowed.");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(target.HeadSha, "^[a-fA-F0-9]{40}$"))
                        throw new InvalidOperationException("GitHub returned an invalid PR head.");
                    payload = new ActionPayload(HeadSha: target.HeadSha);
                }
                catch (HttpRequestException e)
                {
                    logger.LogError(e, "Unable to capture PR head for queued merge");
                    return Results.Json(new { error = "Unable to read the current PR version from GitHub. Merge was not queued." }, statusCode: 502);
                }
            }
            var action = await store.QueueAsync(input.PrId, input.Kind, JsonSerializer.Serialize(payload), ct);
            events.Publish("action.changed", new { action.Id }); return Results.Ok(action);
        });
        api.MapDelete("/actions/{id}", async (string id, ActionStore store, EventHub events, CancellationToken ct) =>
        { await store.DismissAsync(id, ct); events.Publish("action.changed", new { id }); return Results.Ok(new { dismissed = true }); });
        api.MapGet("/decisions", async (ActionStore store, CancellationToken ct) => Results.Ok(await store.DecisionsAsync(ct)));
        api.MapPost("/decisions", async (DecisionInput input, ActionStore store, EventHub events, CancellationToken ct) =>
        {
            if (input.Choice is not ("merge" or "fix" or "ping" or "wait" or "close" or "snooze")) throw new ArgumentException("Invalid Untangle choice.");
            var decision = await store.DecideAsync(input.PrId, input.Choice, input.ActionId, ct);
            events.Publish("action.changed", new { decision.Id }); return Results.Ok(decision);
        });
        api.MapPost("/decisions/undo", async (ActionStore store, EventHub events, CancellationToken ct) =>
        { await store.UndoAsync(ct); events.Publish("action.changed", new { }); return Results.Ok(new { undone = true }); });
        api.MapGet("/actions/policy", (IConfiguration config) => Results.Ok(new
        { sandboxRepo = config["Apply:SandboxRepo"], enabled = config.GetValue<bool>("Apply:Enabled") }));
        api.MapPost("/actions/{id}/apply", async (string id, ConfirmInput input, ActionStore store, IGitHubActions github,
            IConfiguration config, EventHub events, ILogger<ActionStore> logger, CancellationToken ct) =>
        {
            var action = await store.GetAsync(id, ct) ?? throw new ArgumentException("Action not found.");
            var separator = action.PrId.LastIndexOf('#');
            var repo = action.PrId[..separator]; var number = int.Parse(action.PrId[(separator + 1)..]);
            if (!input.Confirmed) throw new ArgumentException("Explicit confirmation is required.");
            if (!config.GetValue<bool>("Apply:Enabled") || !string.Equals(repo, config["Apply:SandboxRepo"], StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Apply is disabled or this repo is not the configured sandbox." }, statusCode: 403);
            if (!await store.ClaimAsync(id, ct)) throw new InvalidOperationException("Only queued actions can be applied once.");
            events.Publish("action.changed", new { id });
            try
            {
                var target = await github.InspectAsync(repo, number, ct);
                var payload = JsonSerializer.Deserialize<ActionPayload>(action.Payload) ?? throw new InvalidOperationException("Invalid stored payload.");
                var result = await github.ApplyAsync(repo, target, action.Kind, payload, ct);
                await store.FinishAsync(id, true, result, CancellationToken.None);
                events.Publish("action.changed", new { id }); return Results.Ok(new { applied = true, result });
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                logger.LogError(e, "Apply failed or outcome unknown; never retry automatically");
                await store.FinishAsync(id, false, e.Message + " Check GitHub before queuing another attempt.", CancellationToken.None);
                events.Publish("action.changed", new { id });
                return Results.Json(new { error = e.Message + " Check GitHub before retrying." }, statusCode: 502);
            }
        });
    }
}
