using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tangle.Data;
using Tangle.Web;

namespace Tangle.Web.Tests;

public sealed class FakeOAuthHandler : HttpMessageHandler
{
    public int Exchanges { get; private set; }
    public string? Verifier { get; private set; }
    public bool FailToken { get; set; }
    public int Refreshes { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Host == "github.com" && request.RequestUri.AbsolutePath == "/login/oauth/access_token")
        {
            Exchanges++;
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
            Verifier = form.TryGetValue("code_verifier", out var verifier) ? verifier.ToString() : null;
            if (FailToken) return Json(new { error = "bad_verification_code" });
            if (form.TryGetValue("grant_type", out var grant) && grant == "refresh_token")
            {
                Refreshes++;
                var owner = form["refresh_token"].ToString().Replace("refresh-", "");
                return Json(new { access_token = "access-" + owner, refresh_token = "refresh-" + owner, expires_in = 28800 });
            }
            return Json(new { access_token = "access-" + form["code"], refresh_token = "refresh-" + form["code"], expires_in = 28800 });
        }
        var token = request.Headers.Authorization?.Parameter ?? "";
        var code = token.Replace("access-", "");
        if (request.RequestUri.Host != "api.github.com") throw new InvalidOperationException("Unexpected network destination.");
        if (request.RequestUri.AbsolutePath == "/user")
            return Json(new { id = code == "bob" ? 202 : code == "denied" ? 303 : 101,
                login = code == "renamed" ? "alice-renamed" : code, name = "Fake user", avatar_url = "https://avatars.githubusercontent.com/fake" });
        if (request.RequestUri.AbsolutePath.StartsWith("/user/memberships/orgs/", StringComparison.Ordinal))
            return Json(new { state = code == "bob" ? "active" : "pending" });
        if (request.RequestUri.AbsolutePath == "/user/installations")
            return Json(new { installations = new[] { new { account = new { login = "alice" } } } });
        throw new InvalidOperationException("Unexpected fake OAuth endpoint.");
    }
    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
}

public sealed class GitHubSignInTests
{
    internal static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Configure(TangleFactory original, FakeOAuthHandler handler) =>
        original.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Mode"] = "GitHub", ["Auth:AllowedLogins:0"] = "alice", ["Auth:AllowedLogins:1"] = "bob",
                ["Auth:AllowedLogins:2"] = "alice-renamed",
                ["GitHub:App:ClientId"] = "fake-client-id", ["GitHub:App:ClientSecret"] = "fake-client-secret",
                ["GitHub:App:CallbackUrl"] = "http://127.0.0.1:5202/auth/github/callback"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<GitHubOAuthClient>();
                services.AddTransient(s => new GitHubOAuthClient(new HttpClient(handler, false), s.GetRequiredService<GitHubAppSettings>(),
                    s.GetRequiredService<Tangle.Core.RuntimeSettings>(), s.GetRequiredService<TimeProvider>()));
            });
        });
    internal static async Task<string> Start(HttpClient client)
    {
        var response = await client.GetAsync("/auth/github/login");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("github.com", response.Headers.Location!.Host);
        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.Equal("S256", query["code_challenge_method"]);
        return query["state"].ToString();
    }
    [Fact]
    public async Task PkceEncryptedTokensNumericIdentityAccountLogoutAndTwoBoards()
    {
        using var original = new TangleFactory();
        using var fake = new FakeOAuthHandler();
        using var factory = Configure(original, fake);
        using var alice = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5202"), AllowAutoRedirect = false });
        var response = await alice.GetAsync("/auth/github/login");
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Contains("httponly", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
        response = await alice.GetAsync("/auth/github/callback?code=alice&state=" + query["state"]);
        Assert.Equal("/settings#account", response.Headers.Location!.OriginalString);
        Assert.Equal(query["code_challenge"], WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(fake.Verifier!))));
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("tangle.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        var account = await alice.GetStringAsync("/api/account");
        Assert.Contains("alice", account);
        Assert.DoesNotContain("access-alice", account);
        Assert.DoesNotContain("refresh-alice", account);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsJsonAsync("/api/auth/login", new { key = TangleFactory.Key })).StatusCode);
        var databases = factory.Services.GetRequiredService<UserDatabases>();
        var db = await databases.OpenAsync("101");
        await new AuthoringStore(db, TimeProvider.System).SaveStickyAsync(null, "Alice private", "blue", false, default);
        using var bob = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5202"), AllowAutoRedirect = false });
        await bob.GetAsync("/auth/github/callback?code=bob&state=" + await Start(bob));
        Assert.DoesNotContain("Alice private", await bob.GetStringAsync("/api/board"));
        Assert.Contains("Alice private", await alice.GetStringAsync("/api/board"));
        Assert.Contains("alice", await alice.GetStringAsync("/api/account/installations"));
        await using (var control = await factory.Services.GetRequiredService<ControlDatabase>().OpenAsync())
        {
            var token = await control.ExecuteScalarAsync<string>("SELECT access_token FROM user_token WHERE user_id='101'");
            Assert.NotEqual("access-alice", token);
            Assert.DoesNotContain("access-alice", token);
            Assert.Equal("off", await control.ExecuteScalarAsync<string>("SELECT mode FROM user_ai WHERE user_id='101'"));
        }
        await alice.GetAsync("/auth/github/callback?code=renamed&state=" + await Start(alice));
        Assert.Contains("Alice private", await alice.GetStringAsync("/api/board"));
        Assert.Contains("alice-renamed", await alice.GetStringAsync("/api/account"));
        await alice.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/board")).StatusCode);
    }
    [Fact]
    public async Task WrongStateReplayDeniedAndDisabledUsersDoNotGetSessions()
    {
        using var original = new TangleFactory();
        using var fake = new FakeOAuthHandler();
        using var factory = Configure(original, fake);
        using var client = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5202"), AllowAutoRedirect = false });
        var state = await Start(client);
        Assert.Contains("signInError=state", (await client.GetAsync("/auth/github/callback?code=alice&state=wrong")).Headers.Location!.OriginalString);
        Assert.Equal(0, fake.Exchanges);
        state = await Start(client);
        await client.GetAsync("/auth/github/callback?code=denied&state=" + state);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account")).StatusCode);
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        await using (var db = await control.OpenAsync()) Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user WHERE id='303'"));
        state = await Start(client);
        await client.GetAsync("/auth/github/callback?code=alice&state=" + state);
        var exchanges = fake.Exchanges;
        var replay = await client.GetAsync("/auth/github/callback?code=alice&state=" + state);
        Assert.Contains("signInError=state", replay.Headers.Location!.OriginalString);
        Assert.Equal(exchanges, fake.Exchanges);
        await using (var db = await control.OpenAsync()) await db.ExecuteAsync("UPDATE user SET disabled=1 WHERE id='101'");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/board")).StatusCode);
        await client.GetAsync("/auth/github/callback?code=alice&state=" + await Start(client));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/board")).StatusCode);
    }
    [Fact]
    public async Task OrganizationAllowlistAndTokenFailureAreExplicitAndRateLimited()
    {
        using var original = new TangleFactory();
        using var fake = new FakeOAuthHandler();
        using var configured = Configure(original, fake);
        using var factory = configured.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:AllowedLogins:0"] = "nobody", ["Auth:AllowedLogins:1"] = "nobody-else", ["Auth:AllowedLogins:2"] = "third",
                ["Auth:AllowedOrganizations:0"] = "example"
            })));
        using var client = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5202"), AllowAutoRedirect = false });
        await client.GetAsync("/auth/github/callback?code=bob&state=" + await Start(client));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account")).StatusCode);
        await client.PostAsync("/api/auth/logout", null);
        fake.FailToken = true;
        var failure = await client.GetAsync("/auth/github/callback?code=alice&state=" + await Start(client));
        Assert.Contains("signInError=github", failure.Headers.Location!.OriginalString);
        await using var db = await factory.Services.GetRequiredService<ControlDatabase>().OpenAsync();
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user WHERE id='101'"));
    }
    [Fact]
    public async Task LoginRateLimitAndUpdatedAllowlistRecheckAreEnforced()
    {
        using var original = new TangleFactory();
        using var fake = new FakeOAuthHandler();
        using var configured = Configure(original, fake);
        using var factory = configured.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:RequestsPerMinute"] = "4" })));
        using var client = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5202"), AllowAutoRedirect = false });
        await client.GetAsync("/auth/github/callback?code=alice&state=" + await Start(client));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account")).StatusCode);
        factory.Services.GetRequiredService<Tangle.Core.AuthenticationSettings>().AllowedLogins = ["bob"];
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account")).StatusCode);
        await Start(client);
        await Start(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/auth/github/login")).StatusCode);
    }
}
