using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tangle.Data;

namespace Tangle.Web;

public static class AuthoringEndpoints
{
    public sealed record Tie(string ARef, string BRef, string? Label);
    public sealed record ChangeLink(string? Status, string? Label);
    public sealed record EditSticky(string Title, string Color, bool Archived = false);
    public sealed record AddNote(string TargetRef, string Text);
    public sealed record Move(double X, double Y);
    public static void MapAuthoringEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (KeyNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
        });
        api.MapPost("/links", async (Tie input, AuthoringStore store, EventHub events, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.ARef) || string.IsNullOrWhiteSpace(input.BRef) || input.Label?.Length > 2000)
                throw new ArgumentException("Provide two valid references and a label of at most 2,000 characters.");
            var link = await store.TieAsync(input.ARef, input.BRef, input.Label, ct);
            events.Publish("link.changed", new { link.Id }); return Results.Ok(link);
        });
        api.MapPatch("/links/{id}", async (string id, ChangeLink input, AuthoringStore store, EventHub events, CancellationToken ct) =>
        {
            if (input.Status is not (null or "tied" or "cut") || input.Label?.Length > 2000 || (input.Status is null && input.Label is null))
                throw new ArgumentException("Set status tied/cut or a label of at most 2,000 characters.");
            var link = await store.ChangeLinkAsync(id, input.Status, input.Label, ct);
            events.Publish("link.changed", new { id }); return Results.Ok(link);
        });
        async Task<IResult> SaveSticky(string? id, EditSticky input, AuthoringStore store, EventHub events, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 300 || input.Color is not ("yellow" or "blue" or "pink" or "green"))
                throw new ArgumentException("A sticky needs a title of up to 300 characters and color yellow, blue, pink or green.");
            var sticky = await store.SaveStickyAsync(id, input.Title.Trim(), input.Color, input.Archived, ct);
            events.Publish("sticky.changed", new { sticky.Id }); return Results.Ok(sticky);
        }
        api.MapPost("/stickies", (EditSticky input, AuthoringStore store, EventHub events, CancellationToken ct) => SaveSticky(null, input, store, events, ct));
        api.MapPatch("/stickies/{id}", (string id, EditSticky input, AuthoringStore store, EventHub events, CancellationToken ct) => SaveSticky(id, input, store, events, ct));
        api.MapPost("/notes", async (AddNote input, AuthoringStore store, EventHub events, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.TargetRef) || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 10000)
                throw new ArgumentException("Provide a target and a note of up to 10,000 characters.");
            var note = await store.NoteAsync(input.TargetRef, input.Text.Trim(), ct);
            events.Publish(input.TargetRef.StartsWith("sticky:") ? "sticky.changed" : "link.changed", new { targetRef = input.TargetRef });
            return Results.Ok(note);
        });
        api.MapPut("/positions/{nodeRef}", async (string nodeRef, Move input, AuthoringStore store, EventHub events, CancellationToken ct) =>
        {
            nodeRef = Uri.UnescapeDataString(nodeRef);
            if (!double.IsFinite(input.X) || !double.IsFinite(input.Y) || Math.Abs(input.X) > 100000 || Math.Abs(input.Y) > 100000 || nodeRef.StartsWith("link:"))
                throw new ArgumentException("Provide finite card/sticky coordinates within ±100,000.");
            await store.PositionAsync(nodeRef, input.X, input.Y, ct);
            events.Publish("prs.changed", new { ids = Array.Empty<string>() }); return Results.Ok(new { saved = true });
        });
    }
}
