using System.Net;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tangle.Core;
using Tangle.Data;
using Tangle.GitHub;
using Tangle.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Tangle.Web.Tests;

public sealed class UserTokenTests
{
    [Fact]
    public async Task UserApplyAllowlistIsIndependentAndCannotAuthorizeRealRepos()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tangle.GitHub.Tokens.v1");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Apply:Enabled"] = "true", ["Apply:SandboxRepo"] = "fixture/sandbox" }).Build();
        foreach (var id in new[] { 101L, 202L })
            await control.SaveGitHubUserAsync(id, id.ToString(), null, null, protector.Protect("access"),
                protector.Protect("refresh"), DateTimeOffset.UtcNow.AddHours(8), default);
        var databases = factory.Services.GetRequiredService<UserDatabases>();
        foreach (var id in new[] { "101", "202" })
        {
            await databases.OpenAsync(id);
            var user = new UserContext(databases);
            user.Set(id, id);
            var policy = new UserApplyPolicy(user.Database, user, config);
            Assert.False(await policy.AllowedAsync("fixture/sandbox", default));
            if (id == "101")
            {
                await policy.SaveAsync(["fixture/sandbox"], default);
                Assert.True(await policy.AllowedAsync("fixture/sandbox", default));
                await Assert.ThrowsAsync<ArgumentException>(() => policy.SaveAsync(["example/real"], default));
            }
        }
    }
    [Fact]
    public async Task RefreshIsOncePerUserAndNeverUsesAnotherUsersToken()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tangle.GitHub.Tokens.v1");
        foreach (var (id, login) in new[] { (101L, "alice"), (202L, "bob") })
            await control.SaveGitHubUserAsync(id, login, null, null, protector.Protect("access-" + login),
                protector.Protect("refresh-" + login), DateTimeOffset.UtcNow.AddMinutes(2), default);
        using var fake = new FakeOAuthHandler();
        var github = new GitHubOAuthClient(new HttpClient(fake), new GitHubAppSettings(), new RuntimeSettings(), TimeProvider.System);
        async Task<string> Read(string id)
        {
            using var scope = factory.Services.CreateScope();
            await factory.Services.GetRequiredService<UserDatabases>().OpenAsync(id);
            var user = scope.ServiceProvider.GetRequiredService<UserContext>();
            user.Set(id, id);
            return await new UserGitHubTokens(user, control, github, factory.Services.GetRequiredService<IDataProtectionProvider>(),
                factory.Services.GetRequiredService<UserResources>(), TimeProvider.System, NullLogger<UserGitHubTokens>.Instance).GetAsync(default);
        }
        var alice = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Read("101")));
        Assert.All(alice, token => Assert.Equal("access-alice", token));
        Assert.Equal(1, fake.Refreshes);
        Assert.Equal("access-bob", await Read("202"));
        Assert.Equal(2, fake.Refreshes);
    }
    [Fact]
    public async Task ReaderAndActionInspectionSendTheResolvedUsersCredential()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        var protection = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = protection.CreateProtector("Tangle.GitHub.Tokens.v1");
        using var handler = new RecordingGitHub();
        foreach (var (id, login) in new[] { (101L, "alice"), (202L, "bob") })
        {
            await control.SaveGitHubUserAsync(id, login, null, null, protector.Protect("access-" + login),
                protector.Protect("refresh-" + login), DateTimeOffset.UtcNow.AddHours(8), default);
            using var scope = factory.Services.CreateScope();
            await factory.Services.GetRequiredService<UserDatabases>().OpenAsync(id.ToString());
            var user = scope.ServiceProvider.GetRequiredService<UserContext>();
            user.Set(id.ToString(), login);
            var tokens = scope.ServiceProvider.GetRequiredService<GitHubTokens>();
            var reader = new GitHubReader(new HttpClient(handler, false), tokens, NullLogger<GitHubReader>.Instance);
            Assert.Equal(login, await reader.GetViewerAsync("example", default));
            var actions = new GitHubActions(new HttpClient(handler, false), tokens);
            Assert.Equal(login, (await actions.InspectAsync("example/repo", 1, default)).Viewer);
        }
        Assert.Equal(new[] { "access-alice", "access-alice", "access-bob", "access-bob" }, handler.Tokens);
    }
    private sealed class RecordingGitHub : HttpMessageHandler
    {
        public List<string> Tokens { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var token = request.Headers.Authorization!.Parameter!;
            Tokens.Add(token);
            var login = token.Replace("access-", "");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data = new
                {
                    viewer = new { login }, rateLimit = new { cost = 1, remaining = 4999, resetAt = "2026-10-13T00:00:00Z" },
                    repository = new { pullRequest = new { id = "fixture", state = "OPEN", headRefOid = new string('a', 40), author = new { login } } }
                } }))
            });
        }
    }
    [Fact]
    public async Task FailedRefreshAndUnauthorizedResponseInvalidateWithoutRetry()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        var protection = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = protection.CreateProtector("Tangle.GitHub.Tokens.v1");
        await control.SaveGitHubUserAsync(101, "alice", null, null, protector.Protect("access-alice"),
            protector.Protect("refresh-alice"), DateTimeOffset.UtcNow.AddHours(-1), default);
        using var scope = factory.Services.CreateScope();
        await factory.Services.GetRequiredService<UserDatabases>().OpenAsync("101");
        var user = scope.ServiceProvider.GetRequiredService<UserContext>();
        user.Set("101", "alice");
        using var fake = new FakeOAuthHandler { FailToken = true };
        var github = new GitHubOAuthClient(new HttpClient(fake), new GitHubAppSettings(), new RuntimeSettings(), TimeProvider.System);
        var service = new UserGitHubTokens(user, control, github, protection, factory.Services.GetRequiredService<UserResources>(),
            TimeProvider.System, NullLogger<UserGitHubTokens>.Instance);
        await Assert.ThrowsAsync<GitHubAccessExpiredException>(() => service.GetAsync(default));
        Assert.Equal("invalid", (await control.TokenAsync("101", default))!.Status);
        var attempts = fake.Exchanges;
        await Assert.ThrowsAsync<GitHubAccessExpiredException>(() => service.GetAsync(default));
        Assert.Equal(attempts, fake.Exchanges);
        await control.SaveGitHubUserAsync(101, "alice", null, null, protector.Protect("access-alice"),
            protector.Protect("refresh-alice"), DateTimeOffset.UtcNow.AddHours(8), default);
        var tokens = new GitHubTokens(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), () => false,
            service.GetAsync, service.InvalidateAsync);
        Assert.Equal("access-alice", await tokens.GetAsync("example", default));
        await Assert.ThrowsAsync<GitHubAccessExpiredException>(() => tokens.CheckResponseAsync(HttpStatusCode.Unauthorized, "example", default));
        Assert.Equal("invalid", (await control.TokenAsync("101", default))!.Status);
    }
}
