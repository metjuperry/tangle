using Dapper;

namespace Tangle.Data;

public sealed partial class ControlDatabase
{
    public async Task<AiAccount> AiAsync(string id, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);
        return await db.QuerySingleAsync<AiAccount>(new CommandDefinition("""
            SELECT mode AS Mode,anthropic_key AS EncryptedKey,monthly_budget AS Budget,company_granted AS CompanyGranted
            FROM user_ai WHERE user_id=@id
            """, new { id }, cancellationToken: ct));
    }
    public async Task SaveAiAsync(string id, string mode, string? encryptedKey, decimal budget, CancellationToken ct)
    {
        if (mode is not ("off" or "claude_code" or "own_key" or "company") || budget is < 0 or > 100)
            throw new ArgumentException("Choose a valid AI mode and a monthly budget between $0 and $100.");
        await using var db = await OpenAsync(ct);
        using var tx = db.BeginTransaction(deferred: false);
        var existing = await db.QuerySingleAsync<AiAccount>(new CommandDefinition("""
            SELECT mode AS Mode,anthropic_key AS EncryptedKey,monthly_budget AS Budget,company_granted AS CompanyGranted
            FROM user_ai WHERE user_id=@id
            """, new { id }, tx, cancellationToken: ct));
        if (mode == "company" && !existing.CompanyGranted) throw new ArgumentException("Company AI has not been granted to this account.");
        if (mode == "company" && budget > existing.MonthlyBudget) throw new ArgumentException("Company budget cannot exceed the administrator's grant.");
        if (mode == "own_key" && string.IsNullOrWhiteSpace(encryptedKey ?? existing.EncryptedKey))
            throw new ArgumentException("Supply your Anthropic API key before selecting this mode.");
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE user_ai SET mode=@mode,anthropic_key=CASE WHEN @mode='own_key' THEN COALESCE(@encryptedKey,anthropic_key)
                ELSE NULL END,monthly_budget=@budget WHERE user_id=@id
            """, new { id, mode, encryptedKey, budget = budget.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            tx, cancellationToken: ct));
        tx.Commit();
    }
    public async Task DisableAsync(string id, bool disabled, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        if (await db.ExecuteAsync(new CommandDefinition("UPDATE user SET disabled=@disabled WHERE id=@id AND id<>'local'",
            new { id, disabled }, cancellationToken: ct)) != 1) throw new ArgumentException("GitHub account not found.");
    }
    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (id == "local") throw new ArgumentException("The local working database cannot be deleted.");
        await using var db = await OpenAsync(ct);
        using var tx = db.BeginTransaction(deferred: false);
        await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM personal_key WHERE user_id=@id;
            DELETE FROM user_token WHERE user_id=@id;
            DELETE FROM user_ai WHERE user_id=@id;
            DELETE FROM user WHERE id=@id;
            """, new { id }, tx, cancellationToken: ct));
        tx.Commit();
    }
    public async Task<IReadOnlyList<AdminUser>> UsersAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return (await db.QueryAsync<AdminUser>(new CommandDefinition("""
            SELECT u.id AS Id,u.login AS Login,u.last_seen_at AS LastSeenAt,u.disabled AS Disabled,
              COALESCE(a.company_granted,0) AS CompanyGranted FROM user u LEFT JOIN user_ai a ON a.user_id=u.id
            WHERE u.id<>'local' ORDER BY u.login
            """, cancellationToken: ct))).ToArray();
    }
    public async Task GrantCompanyAsync(string id, bool grant, decimal budget, CancellationToken ct)
    {
        if (budget is < 0 or > 100) throw new ArgumentException("Company budget must be between $0 and $100.");
        await using var db = await OpenAsync(ct);
        if (await db.ExecuteAsync(new CommandDefinition("""
            UPDATE user_ai SET company_granted=@grant,monthly_budget=@budget,
              mode=CASE WHEN @grant=0 AND mode='company' THEN 'off' ELSE mode END WHERE user_id=@id
            """, new { id, grant, budget = budget.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken: ct)) != 1)
            throw new ArgumentException("Account not found.");
    }
    public async Task<string[]> AllowlistAsync(string kind, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return (await db.QueryAsync<string>(new CommandDefinition("SELECT value FROM allowlist WHERE kind=@kind ORDER BY value",
            new { kind }, cancellationToken: ct))).ToArray();
    }
    public async Task SaveAllowlistAsync(string[] logins, string[] organizations, CancellationToken ct)
    {
        if (logins.Length + organizations.Length == 0 || logins.Concat(organizations).Any(value =>
            string.IsNullOrWhiteSpace(value) || value.Length > 39 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("Supply an explicit allowlist of valid GitHub logins/organization names.");
        await using var db = await OpenAsync(ct);
        using var tx = db.BeginTransaction(deferred: false);
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM allowlist", transaction: tx, cancellationToken: ct));
        foreach (var (kind, values) in new[] { ("login", logins), ("org", organizations) })
            foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
                await db.ExecuteAsync(new CommandDefinition("INSERT INTO allowlist VALUES(@kind,@value)",
                    new { kind, value = value.ToLowerInvariant() }, tx, cancellationToken: ct));
        tx.Commit();
    }
}
public sealed record AiAccount(string Mode, string? EncryptedKey, string Budget, bool CompanyGranted)
{
    public AiAccount() : this("off", null, "1", false) { }
    public decimal MonthlyBudget => decimal.Parse(Budget, System.Globalization.CultureInfo.InvariantCulture);
}
public sealed record AdminUser(string Id, string Login, string LastSeenAt, bool Disabled, bool CompanyGranted)
{
    public AdminUser() : this("", "", "", false, false) { }
}
