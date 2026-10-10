using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tangle.Data;
using Tangle.Sessions;

namespace Tangle.Web;

public static class SessionEndpoints
{
    public sealed record KeyName(string Name);
    public sealed record Attach(string PrId);
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        api.MapGet("/keys", async (PersonalKeys keys, CancellationToken ct) => Results.Ok(await keys.ListAsync(ct)));
        api.MapPost("/keys", async (KeyName input, PersonalKeys keys, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await keys.CreateAsync(input.Name, ct));
        });
        api.MapDelete("/keys/{id}", async (string id, PersonalKeys keys, CancellationToken ct) =>
        { await keys.RevokeAsync(id, ct); return Results.Ok(new { revoked = true }); });
        api.MapPost("/sessions", async (SessionReport report, SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.ReportAsync(report, ct)));
        api.MapGet("/sessions", async (SessionService sessions, CancellationToken ct) => Results.Ok(await sessions.ListAsync(ct)));
        api.MapPatch("/sessions/{id}", async (string id, Attach input, SessionService sessions, CancellationToken ct) =>
        { await sessions.AttachAsync(Uri.UnescapeDataString(id), input.PrId, ct); return Results.Ok(new { attached = true }); });
    }
}
