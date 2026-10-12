using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapGet("/api/account", async (IUserContext user, ControlDatabase control, AuthenticationSettings auth,
            TimeProvider clock, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var account = await control.AccountAsync(user.UserId, ct);
            if (account is null) return Results.NotFound(new { error = "Account not found." });
            var tokenStatus = account.TokenStatus ?? "none";
            if (account.ExpiresAt is not null && DateTimeOffset.Parse(account.ExpiresAt) <= clock.GetUtcNow()) tokenStatus = "expired";
            return Results.Ok(new { mode = auth.Mode, account.Login, account.Name, account.AvatarUrl, tokenStatus,
                account.ExpiresAt, syncAvailable = auth.Mode == "Local",
                installUrl = "https://github.com/apps/tangle-metjuperry/installations/new" });
        }).RequireAuthorization();
        app.MapGet("/api/account/installations", async (IUserContext user, ControlDatabase control,
            GitHubOAuthClient github, IDataProtectionProvider protection, AuthenticationSettings auth, TimeProvider clock, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (auth.Mode != "GitHub") return Results.Ok(Array.Empty<GitHubInstallation>());
            var account = await control.AccountAsync(user.UserId, ct);
            if (account?.ExpiresAt is null || DateTimeOffset.Parse(account.ExpiresAt) <= clock.GetUtcNow())
                return Results.Json(new { error = "GitHub access expired, sign in again." }, statusCode: 409);
            var encrypted = await control.EncryptedAccessAsync(user.UserId, ct);
            if (encrypted is null) return Results.Json(new { error = "GitHub access expired, sign in again." }, statusCode: 409);
            try
            {
                var token = protection.CreateProtector("Tangle.GitHub.Tokens.v1").Unprotect(encrypted);
                return Results.Ok(await github.InstallationsAsync(token, ct));
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or HttpRequestException
                or InvalidOperationException or System.Text.Json.JsonException)
            { return Results.Json(new { error = "Unable to list GitHub installations. Check app installation, permissions, or sign in again." }, statusCode: 502); }
        }).RequireAuthorization();
    }
}
