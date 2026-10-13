using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public static class AccountManagement
{
    public sealed record AiInput(string Mode, string? Key, decimal Budget);
    public sealed record DeleteInput(string Login);
    public sealed record UserInput(string Id, bool Disabled, bool CompanyGranted, decimal Budget);
    public sealed record AllowlistInput(string[] Logins, string[] Organizations);
    private static bool Admin(IUserContext user, IConfiguration config) =>
        user.UserId != "local" && user.Login == (config["Auth:AdminLogin"] ?? "metjuperry");
    public static void MapAccountManagement(this WebApplication app)
    {
        var account = app.MapGroup("/api/account").RequireAuthorization();
        account.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
        });
        account.MapGet("/ai", async (IUserContext user, ControlDatabase control, IConfiguration config, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var value = await control.AiAsync(user.UserId, ct);
            return Results.Ok(new { value.Mode, budget = value.MonthlyBudget, hasKey = value.EncryptedKey is not null,
                companyAvailable = value.CompanyGranted && config.GetValue<bool>("Ai:CompanyEnabled"), local = user.UserId == "local" });
        });
        account.MapPut("/ai", async (AiInput input, IUserContext user, ControlDatabase control, IDataProtectionProvider protection,
            IConfiguration config, CancellationToken ct) =>
        {
            if (user.UserId == "local") throw new ArgumentException("Local mode uses existing server AI configuration.");
            if (input.Mode == "company" && !config.GetValue<bool>("Ai:CompanyEnabled")) throw new ArgumentException("Company AI is disabled.");
            if (input.Key is not null && (string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 500 || input.Key.Any(char.IsWhiteSpace)))
                throw new ArgumentException("Supply a valid API key without whitespace.");
            await control.SaveAiAsync(user.UserId, input.Mode, input.Key is null ? null :
                protection.CreateProtector("Tangle.Anthropic.Keys.v1").Protect(input.Key), input.Budget, ct);
            return Results.Ok(new { saved = true });
        });
        account.MapDelete("/", async ([Microsoft.AspNetCore.Mvc.FromBody] DeleteInput input, IUserContext user, ControlDatabase control, UserDatabases databases,
            HttpContext context, UserResources resources, CancellationToken ct) =>
        {
            var identity = await control.AccountAsync(user.UserId, ct);
            if (identity is null || input.Login != identity.Login) throw new ArgumentException("Type your exact login to confirm deletion.");
            await databases.DeleteUserAsync(user.UserId, ct);
            resources.Remove(user.UserId);
            await context.SignOutAsync(GitHubSignIn.Scheme);
            return Results.Ok(new { deleted = true, revokeUrl = "https://github.com/settings/apps/authorizations" });
        });
        account.MapGet("/admin", async (IUserContext user, IConfiguration config, ControlDatabase control, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return
            Admin(user, config) ? Results.Ok(new { users = await control.UsersAsync(ct),
                logins = await control.AllowlistAsync("login", ct), organizations = await control.AllowlistAsync("org", ct),
                companyEnabled = config.GetValue<bool>("Ai:CompanyEnabled") }) : Results.StatusCode(403);
        });
        account.MapPut("/admin/user", async (UserInput input, IUserContext user, IConfiguration config, ControlDatabase control, CancellationToken ct) =>
        {
            if (!Admin(user, config)) return Results.StatusCode(403);
            if (input.Id == user.UserId && input.Disabled) throw new ArgumentException("You cannot disable your own administrator account.");
            if (input.CompanyGranted && !config.GetValue<bool>("Ai:CompanyEnabled")) throw new ArgumentException("Enable company AI only after a payer is approved.");
            await control.GrantCompanyAsync(input.Id, input.CompanyGranted, input.Budget, ct);
            await control.DisableAsync(input.Id, input.Disabled, ct);
            return Results.Ok(new { saved = true });
        });
        account.MapPut("/admin/allowlist", async (AllowlistInput input, IUserContext user, IConfiguration config, ControlDatabase control,
            AuthenticationSettings auth, CancellationToken ct) =>
        {
            if (!Admin(user, config)) return Results.StatusCode(403);
            if (input.Logins is null || input.Organizations is null) throw new ArgumentException("Supply login and organization lists.");
            if (!input.Logins.Contains(user.Login, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Keep the administrator login on the allowlist.");
            await control.SaveAllowlistAsync(input.Logins, input.Organizations, ct);
            auth.AllowedLogins = input.Logins; auth.AllowedOrganizations = input.Organizations;
            return Results.Ok(new { saved = true });
        });
    }
}
