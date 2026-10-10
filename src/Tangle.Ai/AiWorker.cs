using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tangle.Core;

namespace Tangle.Ai;

public sealed class AiWorker(IAiStore store, HaikuDecider decider, ILiveEvents events, ILogger<AiWorker> logger,
    bool enabled, DeciderRegistry? registry = null) : BackgroundService, IAiQueue
{
    private readonly Channel<bool> queue = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public void Enqueue()
    {
        if (enabled) queue.Writer.TryWrite(true);
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var _ in queue.Reader.ReadAllAsync(ct))
        {
            try { await RunAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogError(e, "AI proposal pass failed; facts and authored data remain intact");
                events.Publish("ai.error", new { error = e.Message });
            }
        }
    }
    public async Task RunAsync(CancellationToken ct)
    {
        if (!enabled) return;
        var snapshot = await store.SnapshotAsync(ct);
        var verdictDecider = registry?.Get(snapshot.VerdictDecider) ?? decider;
        var pairDecider = registry?.Get(snapshot.PairDecider) ?? decider;
        foreach (var pr in snapshot.Prs)
        {
            var hash = Hash("verdict", pr);
            var work = WorkFor(AiCacheKeys.Verdict(pr), verdictDecider.Name);
            if (await store.LookedAsync(work, snapshot.Prs, ct) || await store.CachedAsync(hash, ct)) continue;
            HaikuDecider.SerializeInput(pr);
            // Reserve a conservative bound before any provider call; unresolved calls retain the reservation.
            hash = verdictDecider.Name + ":" + hash;
            var reservation = await store.ReserveAsync(hash, verdictDecider.Name, .02m, ct, "verdict", [pr.Ref], [work],
                JsonSerializer.Serialize(pr, HaikuDecider.Json));
            if (reservation is null) { events.Publish("ai.budget", new { reached = true }); return; }
            try
            {
                var fresh = (await store.SnapshotAsync(ct)).Prs.FirstOrDefault(p => p.Ref == pr.Ref);
                if (fresh is null || verdictDecider.Name + ":" + Hash("verdict", fresh) != hash)
                {
                    await store.FailAsync(reservation, "AI context or repo opt-in changed before call.", new(0, 0, 0, 0), ct);
                    continue;
                }
                await ProviderExecution.Gate.WaitAsync(ct);
                try
                {
                    var result = await verdictDecider.JudgePrAsync(pr, ct);
                    await store.FinishAsync(reservation, verdictDecider.LastUsage!, pr, result, null, ct);
                }
                finally { ProviderExecution.Gate.Release(); }
                events.Publish("verdict.ready", new { id = pr.Ref[3..] });
                events.Publish("evaluation.ready", new { });
            }
            catch (Exception e)
            {
                await store.FailAsync(reservation, e.Message, verdictDecider.LastUsage, CancellationToken.None);
                throw;
            }
        }
        var pairs = new List<PairContext>();
        foreach (var pair in Candidates(snapshot))
            if (!await store.LookedAsync(WorkFor(AiCacheKeys.Pair(pair), pairDecider.Name), snapshot.Prs, ct)) pairs.Add(pair);
        foreach (var batch in Batches(pairs))
        {
            var hash = pairDecider.Name + ":" + Hash("pairs", HaikuDecider.PairInput(batch));
            if (await store.CachedAsync(hash, ct)) continue;
            var current = await store.SnapshotAsync(ct);
            var currentRefs = current.Prs.Select(p => p.Ref).Concat(current.Stickies.Select(s => s.Ref)).ToHashSet();
            var currentPairs = Candidates(current);
            if (batch.Any(p => !currentRefs.Contains(p.ARef) || !currentRefs.Contains(p.BRef) ||
                current.ExistingPairs.Any(e => e.ARef == p.ARef && e.BRef == p.BRef) ||
                !currentPairs.Any(fresh => fresh.ARef == p.ARef && fresh.BRef == p.BRef &&
                    AiCacheKeys.Pair(fresh).StateHash == AiCacheKeys.Pair(p).StateHash))) continue;
            var reservation = await store.ReserveAsync(hash, pairDecider.Name, .20m, ct, "pairs",
                batch.Select(p => $"{p.ARef} ↔ {p.BRef}").ToArray(),
                batch.Select(p => WorkFor(AiCacheKeys.Pair(p), pairDecider.Name)).ToArray(), JsonSerializer.Serialize(batch, HaikuDecider.Json));
            if (reservation is null) { events.Publish("ai.budget", new { reached = true }); return; }
            try
            {
                await ProviderExecution.Gate.WaitAsync(ct);
                try
                {
                    var result = await pairDecider.JudgePairsAsync(batch, ct);
                    await store.FinishAsync(reservation, pairDecider.LastUsage!, null, null, result, ct);
                }
                finally { ProviderExecution.Gate.Release(); }
                events.Publish("link.changed", new { });
                events.Publish("evaluation.ready", new { });
            }
            catch (Exception e)
            {
                await store.FailAsync(reservation, e.Message, pairDecider.LastUsage, CancellationToken.None);
                throw;
            }
        }
    }
    private static AiWork WorkFor(AiWork work, string name) => name == "haiku-5-5" ? work : work with { Job = $"{work.Job}:{name}" };
    public static IReadOnlyList<PairContext[]> Batches(IReadOnlyList<PairContext> pairs)
    {
        var result = new List<PairContext[]>();
        var current = new List<PairContext>();
        foreach (var pair in pairs)
        {
            HaikuDecider.SerializeInput(HaikuDecider.PairInput(new[] { pair }));
            var candidate = current.Append(pair).ToArray();
            var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(HaikuDecider.PairInput(candidate), HaikuDecider.Json));
            if (current.Count > 0 && (candidate.Length > 20 || bytes > HaikuDecider.MaximumInputBytes))
            {
                result.Add(current.ToArray());
                current.Clear();
            }
            current.Add(pair);
        }
        if (current.Count > 0) result.Add(current.ToArray());
        return result;
    }
    public static string Hash(string job, object input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HaikuDecider.ModelId + "\n" +
            HaikuDecider.Prompt(job) + "\n" + JsonSerializer.Serialize(input, HaikuDecider.Json))));
    public static IReadOnlyList<PairContext> Candidates(AiSnapshot snapshot)
    {
        var pairs = new List<PairContext>();
        bool Existing(string a, string b) => snapshot.ExistingPairs.Any(p => p.ARef == a && p.BRef == b || p.ARef == b && p.BRef == a);
        var prs = snapshot.Prs.OrderBy(p => p.Ref, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < prs.Length; i++)
        for (var j = i + 1; j < prs.Length; j++)
        {
            var a = prs[i]; var b = prs[j];
            if (Existing(a.Ref, b.Ref)) continue;
            var words = Words(a.Title).Intersect(Words(b.Title)).Any();
            var folders = a.Paths.Select(Folder).Where(p => p.Length > 0).Intersect(b.Paths.Select(Folder)).Any();
            var author = a.Author is not null && a.Author == b.Author &&
                Math.Abs((FactsEngine.At(a.CreatedAt) - FactsEngine.At(b.CreatedAt)).TotalDays) <= 30;
            if (words || folders || author) pairs.Add(new(a.Ref, b.Ref, a, b, null));
        }
        foreach (var pr in prs)
        foreach (var sticky in snapshot.Stickies.OrderBy(s => s.Ref))
        {
            if (Existing(pr.Ref, sticky.Ref)) continue;
            var (a, b) = string.CompareOrdinal(pr.Ref, sticky.Ref) < 0 ? (pr.Ref, sticky.Ref) : (sticky.Ref, pr.Ref);
            pairs.Add(new(a, b, pr, null, sticky.Title));
        }
        return pairs;
    }
    private static IEnumerable<string> Words(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"[a-z][a-z0-9-]{3,}")
            .Select(m => m.Value).Where(w => w is not ("feat" or "fixes" or "with" or "from" or "this" or "that"));
    private static string Folder(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
}
