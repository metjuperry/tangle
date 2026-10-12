using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Tangle.Data;

namespace Tangle.Web;

public sealed class UserApplyPolicy(Database database, IUserContext user, IConfiguration config)
{
    public async Task<string[]> RepositoriesAsync(CancellationToken ct)
    {
        var sandbox = config["Apply:SandboxRepo"];
        if (user.UserId == "local") return string.IsNullOrWhiteSpace(sandbox) ? [] : [sandbox];
        await using var db = await database.OpenAsync(ct);
        var json = await db.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT value FROM app_setting WHERE key='apply_repositories'", cancellationToken: ct));
        return JsonSerializer.Deserialize<string[]>(json) ?? throw new InvalidOperationException("Invalid Apply repository settings.");
    }
    public async Task<bool> AllowedAsync(string repo, CancellationToken ct) =>
        config.GetValue<bool>("Apply:Enabled") && string.Equals(repo, config["Apply:SandboxRepo"], StringComparison.OrdinalIgnoreCase)
            && (await RepositoriesAsync(ct)).Contains(repo, StringComparer.OrdinalIgnoreCase);
    public async Task SaveAsync(string[] repos, CancellationToken ct)
    {
        if (user.UserId == "local") throw new ArgumentException("Local mode uses the server sandbox policy.");
        if (repos is null || repos.Any(repo => string.IsNullOrWhiteSpace(repo) ||
            !string.Equals(repo, config["Apply:SandboxRepo"], StringComparison.OrdinalIgnoreCase))
            || repos.Distinct(StringComparer.OrdinalIgnoreCase).Count() != repos.Length)
            throw new ArgumentException("Only the server-authorized sandbox may be opted into. Real-repository Apply is not enabled.");
        await using var db = await database.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("UPDATE app_setting SET value=@json WHERE key='apply_repositories'",
            new { json = JsonSerializer.Serialize(repos) }, cancellationToken: ct));
    }
}
