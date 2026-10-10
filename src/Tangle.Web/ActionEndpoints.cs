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
        api.MapPost("/actions", async (QueueInput input, ActionStore store, EventHub events, CancellationToken ct) =>
        {
            if (input.Kind is not ("comment" or "request_review" or "close" or "merge") || input.Payload is null)
                throw new ArgumentException("Choose comment, request_review, close or merge.");
            if (input.Kind == "comment" && (string.IsNullOrWhiteSpace(input.Payload.Text) || input.Payload.Text.Length > 10000))
                throw new ArgumentException("Comment text is required, up to 10,000 characters.");
            if (input.Kind == "request_review" && (string.IsNullOrWhiteSpace(input.Payload.Reviewer) || !System.Text.RegularExpressions.Regex.IsMatch(input.Payload.Reviewer, "^[A-Za-z0-9-]{1,39}$")))
                throw new ArgumentException("A GitHub reviewer login is required.");
            if (input.Kind == "merge" && (input.Payload.HeadSha is null || !System.Text.RegularExpressions.Regex.IsMatch(input.Payload.HeadSha, "^[a-fA-F0-9]{40}$")))
                throw new ArgumentException("A 40-character PR head SHA is required for merge.");
            var action = await store.QueueAsync(input.PrId, input.Kind, JsonSerializer.Serialize(input.Payload), ct);
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
