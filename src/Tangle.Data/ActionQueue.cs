using System.Text.Json;
using Tangle.Core;

namespace Tangle.Data;

public sealed class ActionQueue(ActionStore store, SyncStore sync, IGitHubActions github, ILiveEvents events)
{
    public async Task<QueuedAction> QueueAsync(string prId, string kind, ActionPayload payload, CancellationToken ct)
    {
        if (kind is not ("comment" or "request_review" or "close" or "merge"))
            throw new ArgumentException("Choose comment, request_review, close or merge.");
        if (kind == "comment" && (string.IsNullOrWhiteSpace(payload.Text) || payload.Text.Length > 10000))
            throw new ArgumentException("Comment text is required, up to 10,000 characters.");
        if (kind == "request_review" && (string.IsNullOrWhiteSpace(payload.Reviewer) ||
            !System.Text.RegularExpressions.Regex.IsMatch(payload.Reviewer, "^[A-Za-z0-9-]{1,39}$")))
            throw new ArgumentException("A GitHub reviewer login is required.");
        var card = (await sync.GetBoardAsync(ct: ct)).FirstOrDefault(c => c.Id == prId)
            ?? throw new ArgumentException("PR must be cached, watched and open.");
        if (kind == "merge")
        {
            var target = await github.InspectAsync(card.RepoId, checked((int)card.Number), ct);
            if (target.State != "OPEN") throw new InvalidOperationException("PR is no longer open.");
            if (!FactsEngine.Same(target.Author, target.Viewer)) throw new InvalidOperationException("Merging another person's PR is not allowed.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(target.HeadSha, "^[a-fA-F0-9]{40}$"))
                throw new InvalidOperationException("GitHub returned an invalid PR head.");
            payload = new(HeadSha: target.HeadSha);
        }
        var action = await store.QueueAsync(prId, kind, JsonSerializer.Serialize(payload), ct);
        events.Publish("action.changed", new { action.Id });
        return action;
    }
}
