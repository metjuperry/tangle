using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Tangle.Data;
using Tangle.Web;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Tangle.Web.Tests;

public sealed class AccountManagementTests
{
    [Fact]
    public async Task AccountRoutesEnforceAdminOwnershipAndConfirmedDeletion()
    {
        using var original = new TangleFactory();
        using var fake = new FakeOAuthHandler();
        using var configured = GitHubSignInTests.Configure(original, fake);
        using var factory = configured.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:AdminLogin"] = "alice" })));
        using var alice = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5202"), AllowAutoRedirect = false });
        using var bob = factory.CreateClient(new() { BaseAddress = new("http://127.0.0.1:5202"), AllowAutoRedirect = false });
        await alice.GetAsync("/auth/github/callback?code=alice&state=" + await GitHubSignInTests.Start(alice));
        await bob.GetAsync("/auth/github/callback?code=bob&state=" + await GitHubSignInTests.Start(bob));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/account/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/account/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.PutAsJsonAsync("/api/account/ai",
            new { mode = "company", budget = 1, key = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.PutAsJsonAsync("/api/account/ai",
            new { mode = "own_key", budget = 1, key = "fake-secret" })).StatusCode);
        Assert.DoesNotContain("fake-secret", await bob.GetStringAsync("/api/account/ai"));
        var wrong = new HttpRequestMessage(HttpMethod.Delete, "/api/account") { Content = JsonContent.Create(new { login = "alice" }) };
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.SendAsync(wrong)).StatusCode);
        var right = new HttpRequestMessage(HttpMethod.Delete, "/api/account") { Content = JsonContent.Create(new { login = "bob" }) };
        Assert.Equal(HttpStatusCode.OK, (await bob.SendAsync(right)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.GetAsync("/api/account")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/account")).StatusCode);
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        await using var db = await control.OpenAsync();
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_ai WHERE user_id='202'"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_token WHERE user_id='202'"));
    }
    [Fact]
    public async Task AiKeysAreEncryptedAndModeGrantAndDeletionAreIsolated()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tangle.Anthropic.Keys.v1");
        foreach (var id in new[] { 101L, 202L })
            await control.SaveGitHubUserAsync(id, id.ToString(), null, null, "encrypted", "encrypted", DateTimeOffset.UtcNow.AddHours(8), default);
        await control.SaveAiAsync("101", "own_key", protector.Protect("fake-anthropic-key"), 2, default);
        Assert.NotEqual("fake-anthropic-key", (await control.AiAsync("101")).EncryptedKey);
        Assert.Equal("off", (await control.AiAsync("202")).Mode);
        await Assert.ThrowsAsync<ArgumentException>(() => control.SaveAiAsync("202", "company", null, 2, default));
        await control.GrantCompanyAsync("202", true, 1, default);
        await Assert.ThrowsAsync<ArgumentException>(() => control.SaveAiAsync("202", "company", null, 2, default));
        await control.SaveAiAsync("202", "company", null, 1, default);
        await control.GrantCompanyAsync("202", false, 1, default);
        Assert.Equal("off", (await control.AiAsync("202")).Mode);
        await control.SaveAiAsync("101", "claude_code", null, 1, default);
        Assert.Null((await control.AiAsync("101")).EncryptedKey);
        var databases = factory.Services.GetRequiredService<UserDatabases>();
        await databases.OpenAsync("101");
        await databases.OpenAsync("202");
        using (var first = databases.Lease("101"))
        using (var second = databases.Lease("101"))
            await Assert.ThrowsAsync<InvalidOperationException>(() => databases.DeleteUserAsync("101", default));
        await databases.DeleteUserAsync("101", default);
        Assert.False(await control.ActiveAsync("101"));
        Assert.True(await control.ActiveAsync("202"));
        await Assert.ThrowsAsync<ArgumentException>(() => databases.DeleteUserAsync("local", default));
        using var localScope = factory.Services.CreateScope();
        var local = localScope.ServiceProvider.GetRequiredService<Database>();
        await using (var source = await local.OpenAsync())
            await source.ExecuteAsync("INSERT INTO personal_key VALUES('old-key','Old','legacy-secret-hash','now',NULL)");
        await control.ImportKeysAsync(local, "local");
        await using (var destination = await control.OpenAsync())
            await destination.ExecuteAsync("DELETE FROM personal_key WHERE id='old-key'");
        await control.ImportKeysAsync(local, "local");
        await using (var destination = await control.OpenAsync())
            Assert.Equal(0, await destination.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM personal_key WHERE id='old-key'"));
    }
    [Fact]
    public async Task ImportBacksUpSourceAndPreservesBoardWithoutCopyingUnsafeCredentials()
    {
        using var factory = new TangleFactory();
        using var client = factory.LocalClient();
        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<Database>();
        var control = factory.Services.GetRequiredService<ControlDatabase>();
        await control.SaveGitHubUserAsync(101, "viewer", null, null, "encrypted", "encrypted", DateTimeOffset.UtcNow.AddHours(8), default);
        await using var db = await source.OpenAsync();
        await db.ExecuteAsync("INSERT INTO sticky VALUES('goal','Keep this','blue',NULL); INSERT INTO note VALUES('note','sticky:goal','preserved','now')");
        var sourcePath = db.DataSource;
        var users = Path.Combine(Path.GetDirectoryName(sourcePath)!, "data", "users");
        var backup = await LocalBoardImport.RunAsync(sourcePath, users, "101", "viewer", control);
        Assert.True(File.Exists(backup));
        await using var imported = await new Database(Path.Combine(users, "101.db")).OpenAsync();
        Assert.Equal("preserved", await imported.ExecuteScalarAsync<string>("SELECT text FROM note WHERE id='note'"));
        Assert.Equal("preserved", await db.ExecuteScalarAsync<string>("SELECT text FROM note WHERE id='note'"));
        Assert.Equal(0, await imported.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM personal_key"));
        await Assert.ThrowsAsync<ArgumentException>(() => LocalBoardImport.RunAsync(sourcePath, users, "101", "wrong", control));
    }
}
