using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Tangle.Core;
using Tangle.Data;

namespace Tangle.Web;

public sealed class UserGitHubTokens(IUserContext user, ControlDatabase control, GitHubOAuthClient github,
    IDataProtectionProvider protection, UserResources resources, TimeProvider clock, ILogger<UserGitHubTokens> logger)
{
    private string? usedAccess;
    private IDataProtector Protector => protection.CreateProtector("Tangle.GitHub.Tokens.v1");
    public async Task<string> GetAsync(CancellationToken ct)
    {
        var gate = resources.Tokens(user.UserId);
        await gate.WaitAsync(ct);
        try
        {
            var stored = await control.TokenAsync(user.UserId, ct);
            if (stored is null || stored.Status != "active") throw new GitHubAccessExpiredException();
            var originalAccess = stored.AccessToken;
            try
            {
                if (!DateTimeOffset.TryParse(stored.ExpiresAt, out var expiry))
                    throw new InvalidOperationException("Invalid stored token expiry.");
                if (expiry <= clock.GetUtcNow().AddMinutes(5))
                {
                    if (string.IsNullOrWhiteSpace(stored.RefreshToken)) throw new GitHubAccessExpiredException();
                    var refreshed = await github.RefreshAsync(Protector.Unprotect(stored.RefreshToken), ct);
                    var access = Protector.Protect(refreshed.AccessToken);
                    if (!await control.RefreshTokenAsync(user.UserId, stored.AccessToken, access,
                        Protector.Protect(refreshed.RefreshToken), refreshed.ExpiresAt, ct))
                    {
                        // A concurrent sign-in may have replaced credentials while the refresh was in flight.
                        stored = await control.TokenAsync(user.UserId, ct);
                        if (stored is null || stored.Status != "active") throw new GitHubAccessExpiredException();
                    }
                    else stored = new(access, null, refreshed.ExpiresAt.ToString("O"), "active");
                }
                usedAccess = stored.AccessToken;
                return Protector.Unprotect(stored.AccessToken);
            }
            catch (Exception error) when (error is CryptographicException or InvalidOperationException or HttpRequestException or JsonException or OperationCanceledException)
            {
                await control.InvalidateTokenAsync(user.UserId, originalAccess, CancellationToken.None);
                resources.Events(user.UserId).Publish("github.access", new { expired = true });
                logger.LogWarning("GitHub token refresh failed for user {UserId} ({FailureType}); no credentials logged",
                    user.UserId, error.GetType().Name);
                throw new GitHubAccessExpiredException();
            }
        }
        finally { gate.Release(); }
    }
    public async Task InvalidateAsync(CancellationToken ct)
    {
        if (usedAccess is not null) await control.InvalidateTokenAsync(user.UserId, usedAccess, ct);
        resources.Events(user.UserId).Publish("github.access", new { expired = true });
    }
}
