using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public sealed class GitHubAppSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackUrl { get; set; } = "";
    public string Callback(RuntimeSettings runtime) => string.IsNullOrWhiteSpace(CallbackUrl)
        ? new Uri(new Uri(runtime.Address), "/auth/github/callback").AbsoluteUri : CallbackUrl;
    public void Validate(RuntimeSettings runtime)
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("GitHub mode requires GitHub:App:ClientId and GitHub:App:ClientSecret in Host user-secrets or environment.");
        if (!Uri.TryCreate(Callback(runtime), UriKind.Absolute, out var uri) || uri.AbsolutePath != "/auth/github/callback"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidOperationException("GitHub:App:CallbackUrl must be an exact HTTPS callback (HTTP allowed only on localhost).");
        var origins = runtime.AllowedOrigins.Length == 0 ? [new Uri(runtime.Address).GetLeftPart(UriPartial.Authority)] : runtime.AllowedOrigins;
        if (!origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal))
            throw new InvalidOperationException("GitHub callback origin must match a configured Tangle allowed origin. Register this exact callback on the GitHub App.");
        var hosts = runtime.AllowedHosts.Length == 0 ? [new Uri(runtime.Address).Authority] : runtime.AllowedHosts;
        if (!hosts.Contains(uri.Authority, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("GitHub callback authority must match a configured Tangle allowed host.");
    }
}

public sealed record GitHubIdentity(long Id, string Login, string? Name, string? AvatarUrl);
public sealed record GitHubAuthorization(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
public sealed record GitHubInstallation(string Account, string Url);
public sealed class GitHubOAuthClient(HttpClient http, GitHubAppSettings settings, RuntimeSettings runtime, TimeProvider clock)
{
    public async Task<GitHubAuthorization> ExchangeAsync(string code, string verifier, CancellationToken ct)
    {
        return await TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId, ["client_secret"] = settings.ClientSecret,
            ["code"] = code, ["redirect_uri"] = settings.Callback(runtime), ["code_verifier"] = verifier
        }, ct);
    }
    public Task<GitHubAuthorization> RefreshAsync(string refresh, CancellationToken ct) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId, ["client_secret"] = settings.ClientSecret,
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh
        }, ct);
    private async Task<GitHubAuthorization> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(form);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("GitHub token exchange failed. Try signing in again.");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = json.RootElement;
        if (root.TryGetProperty("error", out _) || !root.TryGetProperty("access_token", out var token)
            || !root.TryGetProperty("refresh_token", out var refresh) || !root.TryGetProperty("expires_in", out var expiry)
            || !expiry.TryGetInt32(out var seconds) || seconds <= 0
            || string.IsNullOrWhiteSpace(token.GetString()) || string.IsNullOrWhiteSpace(refresh.GetString()))
            throw new InvalidOperationException("GitHub did not issue expiring user tokens. Confirm the app's token-expiry setting, then sign in again.");
        return new(token.GetString()!, refresh.GetString()!, clock.GetUtcNow().AddSeconds(seconds));
    }
    private async Task<JsonDocument> GetAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("Tangle/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new GitHubAccessExpiredException();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound
                ? "GitHub access is missing. Check the app installation and required permissions; organization admission requires Members read."
                : "Unable to verify your GitHub identity. Sign in again.");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }
    public async Task<GitHubIdentity> IdentityAsync(string token, CancellationToken ct)
    {
        using var json = await GetAsync("user", token, ct);
        var root = json.RootElement;
        var id = root.GetProperty("id").GetInt64();
        var login = root.GetProperty("login").GetString();
        if (id <= 0 || string.IsNullOrWhiteSpace(login)) throw new InvalidOperationException("GitHub returned an invalid identity.");
        return new(id, login, root.GetProperty("name").GetString(), root.GetProperty("avatar_url").GetString());
    }
    public async Task<string[]> MembershipsAsync(string token, IEnumerable<string> organizations, CancellationToken ct)
    {
        var active = new List<string>();
        foreach (var org in organizations)
        {
            using var json = await GetAsync("user/memberships/orgs/" + Uri.EscapeDataString(org), token, ct);
            if (json.RootElement.GetProperty("state").GetString() == "active") active.Add(org);
        }
        return active.ToArray();
    }
    public async Task<IReadOnlyList<GitHubInstallation>> InstallationsAsync(string token, CancellationToken ct)
    {
        var result = new List<GitHubInstallation>();
        for (var page = 1; ; page++)
        {
            using var json = await GetAsync($"user/installations?per_page=100&page={page}", token, ct);
            var items = json.RootElement.GetProperty("installations").EnumerateArray().ToArray();
            foreach (var item in items)
                result.Add(new(item.GetProperty("account").GetProperty("login").GetString()!,
                    "https://github.com/apps/tangle-metjuperry/installations/new"));
            if (items.Length < 100) return result;
            if (page >= 100) throw new InvalidOperationException("Too many GitHub installations to list completely.");
        }
    }
}

public sealed class OAuthAttempts(TimeProvider clock)
{
    private sealed record Attempt(string Verifier, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    public (string State, string Verifier) Create()
    {
        foreach (var old in attempts.Where(pair => pair.Value.Expires <= clock.GetUtcNow())) attempts.TryRemove(old.Key, out _);
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        attempts[state] = new(verifier, clock.GetUtcNow().AddMinutes(10));
        return (state, verifier);
    }
    public string? Consume(string state) => attempts.TryRemove(state, out var attempt) && attempt.Expires > clock.GetUtcNow()
        ? attempt.Verifier : null;
}

public static class GitHubSignIn
{
    public const string Scheme = "GitHub";
    public const string MembershipClaim = "tangle.organization";
    public const string AdmissionExpiryClaim = "tangle.admission-expires";
    private const string FlowCookie = "tangle.oauth";
    public static bool Admitted(ClaimsPrincipal principal, AuthenticationSettings auth) =>
        auth.AllowedLogins.Contains(principal.Identity?.Name, StringComparer.OrdinalIgnoreCase)
        || principal.FindAll(MembershipClaim).Any(claim => auth.AllowedOrganizations.Contains(claim.Value, StringComparer.OrdinalIgnoreCase));
    private static CookieOptions FlowOptions(HttpRequest request) => new()
    {
        HttpOnly = true, Secure = request.IsHttps || !request.Host.Host.Equals("127.0.0.1", StringComparison.Ordinal)
            && !request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
        SameSite = SameSiteMode.Lax, Path = "/auth/github", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true
    };
    public static void MapGitHubSignIn(this WebApplication app)
    {
        app.MapGet("/api/auth/config", (AuthenticationSettings auth) => Results.Ok(new { mode = auth.Mode }));
        app.MapGet("/auth/github/login", (HttpContext context, AuthenticationSettings auth, GitHubAppSettings settings,
            RuntimeSettings runtime, OAuthAttempts attempts, IDataProtectionProvider protection) =>
        {
            if (auth.Mode != "GitHub") return Results.NotFound();
            var attempt = attempts.Create();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Cookies.Append(FlowCookie, protection.CreateProtector("Tangle.OAuth.Flow.v1").Protect(attempt.State), FlowOptions(context.Request));
            var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier)));
            return Results.Redirect(QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = settings.ClientId, ["redirect_uri"] = settings.Callback(runtime), ["state"] = attempt.State,
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
            }));
        }).RequireRateLimiting("auth");
        app.MapGet("/auth/github/callback", async (HttpContext context, AuthenticationSettings auth, OAuthAttempts attempts,
            IDataProtectionProvider protection, GitHubOAuthClient github, ControlDatabase control, UserDatabases databases, TimeProvider clock,
            ILogger<GitHubOAuthClient> logger, CancellationToken ct) =>
        {
            if (auth.Mode != "GitHub") return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            var cookie = context.Request.Cookies[FlowCookie];
            context.Response.Cookies.Delete(FlowCookie, FlowOptions(context.Request));
            var state = context.Request.Query["state"];
            var code = context.Request.Query["code"];
            string? expected = null;
            try { if (cookie is not null) expected = protection.CreateProtector("Tangle.OAuth.Flow.v1").Unprotect(cookie); }
            catch (CryptographicException) { }
            if (expected is null || state.Count != 1 || state[0] != expected)
                return Results.Redirect("/?signInError=state");
            var verifier = attempts.Consume(expected);
            if (verifier is null || code.Count != 1 || string.IsNullOrWhiteSpace(code[0]) || code[0]!.Length > 2048
                || context.Request.Query.ContainsKey("error"))
                return Results.Redirect("/?signInError=state");
            try
            {
                var token = await github.ExchangeAsync(code[0]!, verifier, ct);
                var identity = await github.IdentityAsync(token.AccessToken, ct);
                var organizations = auth.AllowedLogins.Contains(identity.Login, StringComparer.OrdinalIgnoreCase) ? [] :
                    await github.MembershipsAsync(token.AccessToken, auth.AllowedOrganizations, ct);
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, identity.Id.ToString(CultureInfo.InvariantCulture)),
                    new(ClaimTypes.Name, identity.Login),
                    new(AdmissionExpiryClaim, clock.GetUtcNow().AddHours(8).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
                };
                claims.AddRange(organizations.Select(org => new Claim(MembershipClaim, org)));
                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
                if (!Admitted(principal, auth)) return Results.Redirect("/?signInError=denied");
                var first = await control.SaveGitHubUserAsync(identity.Id, identity.Login, identity.Name, identity.AvatarUrl,
                    protection.CreateProtector("Tangle.GitHub.Tokens.v1").Protect(token.AccessToken),
                    protection.CreateProtector("Tangle.GitHub.Tokens.v1").Protect(token.RefreshToken), token.ExpiresAt, ct);
                await databases.OpenAsync(identity.Id.ToString(CultureInfo.InvariantCulture), ct);
                await context.SignInAsync(Scheme, principal);
                return Results.Redirect(first ? "/settings#account" : "/");
            }
            catch (Exception error) when (error is HttpRequestException or InvalidOperationException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning("GitHub sign-in failed ({FailureType}); no credentials are logged", error.GetType().Name);
                return Results.Redirect("/?signInError=github");
            }
        }).RequireRateLimiting("auth");
    }
}
