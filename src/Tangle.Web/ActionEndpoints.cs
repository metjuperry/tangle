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
            catch (GitHubAccessExpiredException e) { return Results.Json(new { error = e.Message }, statusCode: 409); }
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
        api.MapPost("/actions", async (QueueInput input, ActionQueue queue, ILogger<ActionStore> logger, CancellationToken ct) =>
        {
            if (input.Payload is null) throw new ArgumentException("Action payload is required.");
            try { return Results.Ok(await queue.QueueAsync(input.PrId, input.Kind, input.Payload, ct)); }
            catch (HttpRequestException e)
            {
                logger.LogError(e, "Unable to prepare queued action");
                return Results.Json(new { error = "Unable to read the PR version from GitHub. Action was not queued." }, statusCode: 502);
            }
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
        api.MapGet("/actions/policy", async (IConfiguration config, UserApplyPolicy policy, IUserContext user, ControlDatabase control, CancellationToken ct) =>
        {
            var access = user.UserId == "local" || (await control.TokenAsync(user.UserId, ct))?.Status == "active";
            return Results.Ok(new { sandboxRepo = config["Apply:SandboxRepo"], enabled = access && config.GetValue<bool>("Apply:Enabled"),
                repositories = await policy.RepositoriesAsync(ct), perUser = user.UserId != "local" });
        });
        api.MapPut("/actions/policy", async (ApplyRepositories input, UserApplyPolicy policy, CancellationToken ct) =>
        { await policy.SaveAsync(input.Repositories, ct); return Results.Ok(new { saved = true }); });
        api.MapPost("/actions/{id}/apply", async (string id, ConfirmInput input, ActionStore store, IGitHubActions github,
            IConfiguration config, EventHub events, ILogger<ActionStore> logger, UserApplyPolicy policy, IUserContext user,
            UserGitHubTokens tokens, CancellationToken ct) =>
        {
            var action = await store.GetAsync(id, ct) ?? throw new ArgumentException("Action not found.");
            var separator = action.PrId.LastIndexOf('#');
            var repo = action.PrId[..separator]; var number = int.Parse(action.PrId[(separator + 1)..]);
            if (!input.Confirmed) throw new ArgumentException("Explicit confirmation is required.");
            if (!await policy.AllowedAsync(repo, ct))
                return Results.Json(new { error = "Apply is disabled or this repo is not the configured sandbox." }, statusCode: 403);
            if (user.UserId != "local") await tokens.GetAsync(ct);
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
            catch (Exception e)
            {
                logger.LogError(e, "Apply failed or outcome unknown; never retry automatically");
                const string message = "Apply failed or its outcome is unknown. Check GitHub before queuing another attempt.";
                await store.FinishAsync(id, false, message, CancellationToken.None);
                events.Publish("action.changed", new { id });
                return Results.Json(new { error = message }, statusCode: 502);
            }
        });
    }
    public sealed record ApplyRepositories(string[] Repositories);
}
