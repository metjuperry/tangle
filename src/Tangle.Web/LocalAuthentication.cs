using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tangle.Core;

namespace Tangle.Web;

public static class LocalAuthentication
{
    public static void AddLocalAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "tangle.local";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });
        services.AddAuthorization();
    }

    public static void UseLocalAuthentication(this WebApplication app, RuntimeSettings settings)
    {
        var address = new Uri(settings.Address);
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin;
            if (!string.Equals(context.Request.Host.Value, address.Authority, StringComparison.OrdinalIgnoreCase)
                || (origin.Count > 0 && (origin.Count != 1 || origin[0] != address.GetLeftPart(UriPartial.Authority))))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Foreign Host or Origin is not allowed." });
                return;
            }
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await next(context);
        });
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            var bearer = context.Request.Headers.Authorization.ToString();
            if (bearer.StartsWith("Bearer ", StringComparison.Ordinal) && Matches(bearer[7..], settings.DevelopmentKey))
                context.User = Principal();
            await next(context);
        });
        app.UseAuthorization();
        app.MapPost("/api/auth/login", async (Login input, HttpContext context) =>
        {
            if (!Matches(input.Key, settings.DevelopmentKey))
                return Results.Json(new { error = "Invalid development key." }, statusCode: 401);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal());
            return Results.NoContent();
        });
        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/api/auth", () => Results.Ok(new { authenticated = true })).RequireAuthorization();
    }

    private static bool Matches(string? provided, string expected) =>
        provided is not null && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "local")], CookieAuthenticationDefaults.AuthenticationScheme));

    public sealed record Login(string Key);
}
