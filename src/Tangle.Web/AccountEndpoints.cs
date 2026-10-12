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
            if (tokenStatus == "active" && account.ExpiresAt is not null && DateTimeOffset.Parse(account.ExpiresAt) <= clock.GetUtcNow()) tokenStatus = "refresh-needed";
            return Results.Ok(new { mode = auth.Mode, account.Login, account.Name, account.AvatarUrl, tokenStatus,
                account.ExpiresAt, syncAvailable = user.UserId == "local" || tokenStatus is "active" or "refresh-needed",
                installUrl = "https://github.com/apps/tangle-metjuperry/installations/new" });
        }).RequireAuthorization();
        app.MapGet("/api/account/installations", async (IUserContext user, ControlDatabase control,
            GitHubOAuthClient github, UserGitHubTokens tokens, AuthenticationSettings auth, TimeProvider clock, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (auth.Mode != "GitHub") return Results.Ok(Array.Empty<GitHubInstallation>());
            try
            {
                var token = await tokens.GetAsync(ct);
                return Results.Ok(await github.InstallationsAsync(token, ct));
            }
            catch (GitHubAccessExpiredException)
            { await tokens.InvalidateAsync(CancellationToken.None); return Results.Json(new { error = "GitHub access expired, sign in again." }, statusCode: 409); }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or HttpRequestException
                or InvalidOperationException or System.Text.Json.JsonException)
            { return Results.Json(new { error = "Unable to list GitHub installations. Check app installation, permissions, or sign in again." }, statusCode: 502); }
        }).RequireAuthorization();
    }
}
