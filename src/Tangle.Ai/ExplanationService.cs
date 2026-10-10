using System.Text.Json;
using Tangle.Core;

namespace Tangle.Ai;

public sealed class ExplanationService(IAiStore store, HaikuDecider haiku,
    IExplanationStore persistence, ILiveEvents events) : IExplanationService
{
    public async Task ExplainAsync(string prId, CancellationToken ct)
    {
        var context = (await store.SnapshotAsync(ct)).Prs.FirstOrDefault(p => p.Ref == $"pr:{prId}")
            ?? throw new InvalidOperationException("PR is not currently permitted for AI.");
        var verdict = await persistence.UnexplainedAsync(prId, ct);
        if (verdict is null) return;
        var input = JsonSerializer.Serialize(new { context, verdict.InputHash, verdict.Judgment }, HaikuDecider.Json);
        var hash = AiWorker.Hash("explain", input);
        var work = new AiWork("explain", context.Ref, hash);
        if (await store.LookedAsync(work, [context], ct)) return;
        var reservation = await store.ReserveAsync(hash, haiku.Name, .02m, ct, "explanation", [context.Ref], [work], input)
            ?? throw new InvalidOperationException("Monthly budget cannot cover this explanation.");
        try
        {
            if (!(await store.SnapshotAsync(ct)).Prs.Any(p => p.Ref == context.Ref && p.UpdatedAt == context.UpdatedAt))
            { await store.FailAsync(reservation, "Permission or PR changed before explanation", new(0, 0, 0, 0), ct); return; }
            await ProviderExecution.Gate.WaitAsync(ct);
            try
            {
                var explanation = await haiku.ExplainAsync(context, verdict.Judgment, ct);
                await persistence.SaveAsync(prId, verdict.InputHash, explanation, reservation, haiku.LastUsage!, ct);
            }
            finally { ProviderExecution.Gate.Release(); }
            events.Publish("verdict.ready", new { id = prId });
        }
        catch (Exception e)
        { await store.FailAsync(reservation, e.Message, haiku.LastUsage, CancellationToken.None); throw; }
    }
}
