using Dapper;
using Tangle.Core;

namespace Tangle.Data;

public sealed class AuthoringStore(Database database, TimeProvider clock)
{
    private string Now => clock.GetUtcNow().ToString("O");
    public async Task RequireRefAsync(string reference, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var (table, id) = reference switch
        {
            _ when reference.StartsWith("pr:", StringComparison.Ordinal) => ("pr", reference[3..]),
            _ when reference.StartsWith("sticky:", StringComparison.Ordinal) => ("sticky", reference[7..]),
            _ when reference.StartsWith("link:", StringComparison.Ordinal) => ("link", reference[5..]),
            _ => throw new ArgumentException("Use a pr:, sticky: or link: reference.")
        };
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM {table} WHERE id=@id",
            new { id }, cancellationToken: ct)) == 0) throw new KeyNotFoundException("Referenced item does not exist.");
    }
    public async Task<BoardString> TieAsync(string a, string b, string? label, CancellationToken ct)
    {
        if (a == b || a.StartsWith("link:") || b.StartsWith("link:")) throw new ArgumentException("Tie two different cards or stickies.");
        await RequireRefAsync(a, ct); await RequireRefAsync(b, ct);
        if (string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO link(id,a_ref,b_ref,source,status,label,created_at,decided_at)
            VALUES(@id,@a,@b,'me','tied',@label,@now,@now)
            ON CONFLICT(a_ref,b_ref) DO UPDATE SET source='me',status='tied',label=COALESCE(excluded.label,link.label),decided_at=excluded.decided_at
            """, new { id = Guid.NewGuid().ToString(), a, b, label, now = Now }, cancellationToken: ct));
        return await db.QuerySingleAsync<BoardString>(new CommandDefinition(LinkSelect + " WHERE a_ref=@a AND b_ref=@b",
            new { a, b }, cancellationToken: ct));
    }
    private const string LinkSelect = "SELECT id AS Id,a_ref AS ARef,b_ref AS BRef,source AS Source,rule AS Rule,status AS Status,reason AS Reason,label AS Label FROM link";
    public async Task<BoardString> ChangeLinkAsync(string id, string? status, string? label, CancellationToken ct)
    {
        await RequireRefAsync($"link:{id}", ct);
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE link SET status=COALESCE(@status,status),label=COALESCE(@label,label),decided_at=@now WHERE id=@id
            """, new { id, status, label, now = Now }, cancellationToken: ct));
        return await db.QuerySingleAsync<BoardString>(new CommandDefinition(LinkSelect + " WHERE id=@id", new { id }, cancellationToken: ct));
    }
    public async Task<Sticky> SaveStickyAsync(string? id, string title, string color, bool archived, CancellationToken ct)
    {
        if (id is not null) await RequireRefAsync($"sticky:{id}", ct);
        id ??= Guid.NewGuid().ToString();
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO sticky(id,title,color,archived_at) VALUES(@id,@title,@color,@at)
            ON CONFLICT(id) DO UPDATE SET title=excluded.title,color=excluded.color,archived_at=excluded.archived_at
            """, new { id, title, color, at = archived ? Now : null }, cancellationToken: ct));
        return new(id, title, color, archived ? Now : null);
    }
    public async Task<BoardNote> NoteAsync(string targetRef, string text, CancellationToken ct)
    {
        await RequireRefAsync(targetRef, ct);
        var note = new BoardNote(Guid.NewGuid().ToString(), targetRef, text, Now);
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO note VALUES(@Id,@TargetRef,@Text,@CreatedAt)", note, cancellationToken: ct));
        return note;
    }
    public async Task PositionAsync(string nodeRef, double x, double y, CancellationToken ct)
    {
        await RequireRefAsync(nodeRef, ct);
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO position VALUES(@nodeRef,@x,@y,1)
            ON CONFLICT(node_ref) DO UPDATE SET x=excluded.x,y=excluded.y,moved_by_me=1
            """, new { nodeRef, x, y }, cancellationToken: ct));
    }
    public async Task<AuthoringData> GetAsync(CancellationToken ct = default)
    {
        await using var db = await database.OpenAsync(ct);
        var stickies = (await db.QueryAsync<Sticky>(new CommandDefinition(
            "SELECT id AS Id,title AS Title,color AS Color,archived_at AS ArchivedAt FROM sticky WHERE archived_at IS NULL ORDER BY id",
            cancellationToken: ct))).ToArray();
        var notes = await db.QueryAsync<BoardNote>(new CommandDefinition(
            "SELECT id AS Id,target_ref AS TargetRef,text AS Text,created_at AS CreatedAt FROM note ORDER BY created_at,id", cancellationToken: ct));
        var positions = await db.QueryAsync<BoardPosition>(new CommandDefinition(
            "SELECT node_ref AS NodeRef,x AS X,y AS Y FROM position", cancellationToken: ct));
        foreach (var sticky in stickies)
        {
            var states = (await db.QueryAsync<string>(new CommandDefinition("""
                SELECT p.state FROM link l JOIN pr p ON 'pr:'||p.id=CASE WHEN l.a_ref=@ref THEN l.b_ref ELSE l.a_ref END
                WHERE (l.a_ref=@ref OR l.b_ref=@ref) AND l.status='tied'
                """, new { @ref = $"sticky:{sticky.Id}" }, cancellationToken: ct))).ToArray();
            sticky.Total = states.Length; sticky.Merged = states.Count(s => s == "MERGED");
        }
        return new(stickies, notes.ToArray(), positions.ToArray());
    }
}
public sealed record Sticky(string Id, string Title, string Color, string? ArchivedAt)
{
    public Sticky() : this("", "", "", null) { }
    public int Total { get; set; }
    public int Merged { get; set; }
}
public sealed record BoardNote(string Id, string TargetRef, string Text, string CreatedAt);
public sealed record BoardPosition(string NodeRef, double X, double Y)
{
    public BoardPosition() : this("", 0, 0) { }
}
public sealed record AuthoringData(IReadOnlyList<Sticky> Stickies, IReadOnlyList<BoardNote> Notes, IReadOnlyList<BoardPosition> Positions);
