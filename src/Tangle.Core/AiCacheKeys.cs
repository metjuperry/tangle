using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tangle.Core;

public static class AiCacheKeys
{
    public static AiWork Verdict(PrContext pr) => new("verdict", pr.Ref, Hash(State(pr)));
    public static AiWork Pair(PairContext pair) => new("pairs",
        string.Join("\n", new[] { pair.ARef, pair.BRef }.Order(StringComparer.Ordinal)),
        Hash(new { a = State(pair.A), b = pair.B is null ? null : State(pair.B), pair.StickyTitle }));

    private static object State(PrContext pr) => new
    {
        pr.Ref, pr.RepoId, pr.Title, pr.Body, pr.Author, pr.CreatedAt, pr.UpdatedAt,
        paths = pr.Paths.Order(StringComparer.Ordinal),
        facts = new
        {
            // Clock-only aging must not turn an unchanged PR into a paid rerun.
            turn = pr.Facts.Turn == "nobody" ? "them" : pr.Facts.Turn,
            reason = pr.Facts.Turn is "them" or "nobody" ? null : pr.Facts.TurnReason,
            pr.Facts.BehindBy, pr.Facts.Conflicting, pr.Facts.Size, pr.Facts.Origin
        },
        comments = pr.OpenComments,
        links = pr.Links.OrderBy(l => l.Ref, StringComparer.Ordinal).ThenBy(l => l.Label, StringComparer.Ordinal)
    };
    private static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
