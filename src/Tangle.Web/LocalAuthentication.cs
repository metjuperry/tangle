using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public static class LocalAuthentication
{
    public static void AddLocalAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Tangle";
        }).AddPolicyScheme("Tangle", "Tangle", options =>
        {
            options.ForwardDefaultSelector = context => context.RequestServices.GetRequiredService<AuthenticationSettings>().Mode == "GitHub"
                ? GitHubSignIn.Scheme : CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddCookie(options =>
        {
            options.Cookie.Name = "tangle.local";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        }).AddCookie(GitHubSignIn.Scheme, options =>
        {
            options.Cookie.Name = "tangle.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnSigningIn = context =>
            {
                context.CookieOptions.Secure = context.Request.IsHttps ||
                    !(Uri.TryCreate("http://" + context.Request.Host.Host, UriKind.Absolute, out var uri) && uri.IsLoopback);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                var auth = context.HttpContext.RequestServices.GetRequiredService<AuthenticationSettings>();
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var expiry = context.Principal?.FindFirstValue(GitHubSignIn.AdmissionExpiryClaim);
                if (id is null || !long.TryParse(expiry, out var admissionExpiry)
                    || admissionExpiry <= context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds()
                    || !GitHubSignIn.Admitted(context.Principal!, auth)
                    || !await context.HttpContext.RequestServices.GetRequiredService<ControlDatabase>().ActiveAsync(id, context.HttpContext.RequestAborted))
                    context.RejectPrincipal();
            };
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Auth:RequestsPerMinute", 30),
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        services.AddAuthorization();
    }

    public static void UseLocalAuthentication(this WebApplication app, RuntimeSettings settings)
    {
        var address = new Uri(settings.Address);
        var auth = app.Services.GetService<AuthenticationSettings>() ?? new();
        var hosts = settings.AllowedHosts.Length == 0 ? [address.Authority] : settings.AllowedHosts;
        var origins = settings.AllowedOrigins.Length == 0 ? [address.GetLeftPart(UriPartial.Authority)] : settings.AllowedOrigins;
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin;
            if (!hosts.Contains(context.Request.Host.Value, StringComparer.OrdinalIgnoreCase)
                || (origin.Count > 0 && (origin.Count != 1 || !origins.Contains(origin[0], StringComparer.Ordinal))))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Foreign Host or Origin is not allowed." });
                return;
            }
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await next(context);
        });
        app.UseAuthentication();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            var bearer = context.Request.Headers.Authorization.ToString();
            var personalRoute = context.Request.Path.StartsWithSegments("/mcp") ||
                context.Request.Path == "/api/sessions" && HttpMethods.IsPost(context.Request.Method);
            if (personalRoute)
            {
                var userId = bearer.StartsWith("Bearer ", StringComparison.Ordinal)
                    ? await PersonalKeys.ResolveAsync(context.RequestServices.GetRequiredService<ControlDatabase>(), bearer[7..], context.RequestAborted) : null;
                if (userId is null)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "An active personal key is required." });
                    return;
                }
                context.User = Principal(userId);
            }
            else if (bearer.StartsWith("Bearer tangle_", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "Personal keys may access only MCP and session reporting." });
                return;
            }
            if (auth.Mode == "ContainerApps" && !personalRoute)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
                var remote = context.Connection.RemoteIpAddress?.ToString();
                var login = context.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"];
                if (remote is not null && auth.TrustedProxyAddresses.Contains(remote, StringComparer.Ordinal)
                    && login.Count == 1 && auth.AllowedLogins.Contains(login[0], StringComparer.OrdinalIgnoreCase))
                    context.User = Principal(login[0]!);
            }
            if (auth.Mode == "Local" && bearer.StartsWith("Bearer ", StringComparison.Ordinal) && Matches(bearer[7..], settings.DevelopmentKey))
                context.User = Principal();
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var control = context.RequestServices.GetRequiredService<ControlDatabase>();
                var id = personalRoute ? context.User.Identity.Name! : auth.Mode == "Local" ? "local" :
                    auth.Mode == "GitHub" ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) :
                    await control.FindLoginAsync(context.User.Identity.Name!, context.RequestAborted);
                if (id is null || !await control.ActiveAsync(id, context.RequestAborted))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "User is disabled or missing." });
                    return;
                }
                var databases = context.RequestServices.GetRequiredService<UserDatabases>();
                using var lease = context.Request.Path == "/api/events" ? null : databases.Lease(id);
                await databases.OpenAsync(id, context.RequestAborted);
                context.RequestServices.GetRequiredService<UserContext>().Set(id, context.User.Identity.Name!);
                await next(context);
                return;
            }
            await next(context);
        });
        app.UseAuthorization();
        app.MapPost("/api/auth/login", async (Login input, HttpContext context) =>
        {
            if (auth.Mode != "Local") return Results.Json(new { error = "Local key sign-in is disabled." }, statusCode: 403);
            if (!Matches(input.Key, settings.DevelopmentKey))
                return Results.Json(new { error = "Invalid development key." }, statusCode: 401);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal());
            return Results.NoContent();
        }).RequireRateLimiting("auth");
        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(auth.Mode == "GitHub" ? GitHubSignIn.Scheme : CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/api/auth", () => Results.Ok(new { authenticated = true })).RequireAuthorization();
    }

    private static bool Matches(string? provided, string expected) =>
        provided is not null && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static ClaimsPrincipal Principal(string login = "local") => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, login)], CookieAuthenticationDefaults.AuthenticationScheme));

    public sealed record Login(string Key);
}
