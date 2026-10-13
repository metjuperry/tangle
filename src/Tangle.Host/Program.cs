using Tangle.Core;
using Tangle.Data;
using Tangle.Web;
using Tangle.GitHub;
using Tangle.Ai;
using Anthropic;
using Tangle.Mcp;
using Tangle.Sessions;
using Tangle.Host;
using Microsoft.AspNetCore.DataProtection;

var importArguments = args.Contains("--import-local", StringComparer.Ordinal);
var hostArguments = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--import-local") continue;
    if (args[i] is "--target-user" or "--confirm-login") { i++; continue; }
    hostArguments.Add(args[i]);
}
var builder = WebApplication.CreateBuilder(hostArguments.ToArray());
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.WebHost.UseUrls(builder.Configuration["Tangle:Address"] ?? new RuntimeSettings().Address);
builder.Services.AddSingleton(services =>
{
    var auth = services.GetRequiredService<IConfiguration>().GetSection("Auth").Get<AuthenticationSettings>() ?? new();
    auth.Validate();
    return auth;
});
builder.Services.AddSingleton(services =>
{
    var settings = services.GetRequiredService<IConfiguration>().GetSection("Tangle").Get<RuntimeSettings>() ?? new();
    if (!Uri.TryCreate(settings.Address, UriKind.Absolute, out var address)
        || address.Scheme is not ("http" or "https") || address.AbsolutePath != "/"
        || address.Query.Length != 0 || address.UserInfo.Length != 0)
        throw new InvalidOperationException("Tangle:Address must be an absolute HTTP(S) bind address without a path, query or credentials.");
    if (address.Host != "127.0.0.1" && (settings.AllowedHosts.Length == 0 || settings.AllowedOrigins.Length == 0))
        throw new InvalidOperationException("Non-default bind hosts require explicit Tangle:AllowedHosts and Tangle:AllowedOrigins.");
    if (settings.AllowedHosts.Any(host => host.Contains('*') || host.Contains('/'))
        || settings.AllowedOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || origin != uri.GetLeftPart(UriPartial.Authority)))
        throw new InvalidOperationException("Allowed hosts must be exact authorities and allowed origins exact HTTP(S) origins.");
    if (services.GetRequiredService<AuthenticationSettings>().Mode == "Local" && settings.DevelopmentKey.Length < 32)
        throw new InvalidOperationException("Set Tangle:DevelopmentKey in user-secrets or Tangle__DevelopmentKey to at least 32 characters.");
    return settings;
});
builder.Services.AddUserData();
builder.Services.AddSingleton(s =>
{
    var settings = s.GetRequiredService<IConfiguration>().GetSection("GitHub:App").Get<GitHubAppSettings>() ?? new();
    if (s.GetRequiredService<AuthenticationSettings>().Mode == "GitHub") settings.Validate(s.GetRequiredService<RuntimeSettings>());
    return settings;
});
builder.Services.AddDataProtection().SetApplicationName("Tangle");
builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
    .Configure<RuntimeSettings, IConfiguration>((options, runtime, config) =>
    {
        var path = config["Auth:KeyDirectory"] ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(runtime.DatabasePath))!, "keys");
        options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
            new DirectoryInfo(path), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    });
builder.Services.AddSingleton<OAuthAttempts>();
builder.Services.AddHttpClient<GitHubOAuthClient>();
builder.Services.AddScoped<UserGitHubTokens>();
builder.Services.AddScoped<UserApplyPolicy>();
builder.Services.AddScoped<UserAiAccess>();
builder.Services.AddLocalAuthentication();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<SyncStore>();
builder.Services.AddScoped<AuthoringStore>();
builder.Services.AddScoped<ActionStore>();
builder.Services.AddScoped<ActionQueue>();
builder.Services.AddScoped<PersonalKeys>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddTangleMcp();
builder.Services.AddScoped<IAiStore, AiStore>();
builder.Services.AddScoped<IEvaluationStore, EvaluationStore>();
builder.Services.AddScoped<IExplanationStore, ExplanationStore>();
builder.Services.AddScoped<IExplanationService, ExplanationService>();
builder.Services.AddScoped<DeciderRegistry>();
builder.Services.AddScoped(s => new EvaluationRunner(s.GetRequiredService<IEvaluationStore>(), s.GetRequiredService<IAiStore>(),
    s.GetRequiredService<DeciderRegistry>(), s.GetRequiredService<UserResources>().Replay(s.GetRequiredService<IUserContext>().UserId)));
builder.Services.AddScoped<IEvaluationReports>(s => s.GetRequiredService<EvaluationRunner>());
builder.Services.AddSingleton(s => new JevDecider(new HttpClient(), s.GetRequiredService<IConfiguration>()["TYPESAFE_API_KEY"] ?? ""));
builder.Services.AddScoped(services =>
{
    return new HaikuDecider(new AnthropicClient { ApiKey = services.GetRequiredService<UserAiAccess>().Key, MaxRetries = 0 });
});
builder.Services.AddScoped(services => new AiWorker(services.GetRequiredService<IAiStore>(),
    services.GetRequiredService<HaikuDecider>(), services.GetRequiredService<ILiveEvents>(),
    services.GetRequiredService<ILogger<AiWorker>>(), services.GetRequiredService<UserAiAccess>().Enabled,
    services.GetRequiredService<DeciderRegistry>()));
builder.Services.AddSingleton<UserAiQueue>();
builder.Services.AddHostedService(s => s.GetRequiredService<UserAiQueue>());
builder.Services.AddScoped<IAiQueue, ScopedAiQueue>();
builder.Services.AddScoped<ISyncStore>(services => services.GetRequiredService<SyncStore>());
builder.Services.AddScoped(s => new GitHubTokens(s.GetRequiredService<IConfiguration>(),
    () => s.GetRequiredService<IUserContext>().UserId == "local",
    ct => s.GetRequiredService<UserGitHubTokens>().GetAsync(ct),
    ct => s.GetRequiredService<UserGitHubTokens>().InvalidateAsync(ct)));
builder.Services.AddHttpClient<IGitHubReader, GitHubReader>();
builder.Services.AddHttpClient<IGitHubActions, GitHubActions>();
builder.Services.AddScoped(s => new SyncService(s.GetRequiredService<IGitHubReader>(), s.GetRequiredService<ISyncStore>(),
    s.GetRequiredService<TimeProvider>(), s.GetRequiredService<ILiveEvents>(), s.GetRequiredService<IAiQueue>(),
    s.GetRequiredService<UserResources>().Sync(s.GetRequiredService<IUserContext>().UserId)));
var app = builder.Build();
var runtime = app.Services.GetRequiredService<RuntimeSettings>();
var dataDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(runtime.DatabasePath))!, "data");
Directory.CreateDirectory(dataDirectory);
using var hostLock = new FileStream(Path.Combine(dataDirectory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
if (importArguments)
{
    string Argument(string name) => Array.IndexOf(args, name) is var index && index >= 0 && index + 1 < args.Length
        ? args[index + 1] : throw new ArgumentException($"Supply {name}.");
    var backup = await LocalBoardImport.RunAsync(runtime.DatabasePath, Path.Combine(dataDirectory, "users"),
        Argument("--target-user"), Argument("--confirm-login"), app.Services.GetRequiredService<ControlDatabase>());
    app.Logger.LogInformation("Local board imported; source backup at {Path}. Original database preserved.", backup);
    return;
}
await app.Services.GetRequiredService<ControlDatabase>().MigrateAsync();
await app.Services.GetRequiredService<ControlDatabase>().EnsureLocalAsync();
var authSettings = app.Services.GetRequiredService<AuthenticationSettings>();
var controlDb = app.Services.GetRequiredService<ControlDatabase>();
var savedLogins = await controlDb.AllowlistAsync("login", default);
var savedOrgs = await controlDb.AllowlistAsync("org", default);
if (savedLogins.Length + savedOrgs.Length > 0)
{
    authSettings.AllowedLogins = savedLogins;
    authSettings.AllowedOrganizations = savedOrgs;
}
else await controlDb.SaveAllowlistAsync(authSettings.AllowedLogins, authSettings.AllowedOrganizations, default);
if (app.Services.GetRequiredService<AuthenticationSettings>().Mode == "Local")
{
    await app.Services.GetRequiredService<UserDatabases>().OpenAsync("local");
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SessionService>().MatchAsync(CancellationToken.None);
}
_ = app.Services.GetRequiredService<GitHubAppSettings>();
app.UseLocalAuthentication(app.Services.GetRequiredService<RuntimeSettings>());
app.MapGitHubSignIn();
app.MapAccountEndpoints();
app.MapAccountManagement();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapSyncEndpoints();
app.MapAuthoringEndpoints();
app.MapActionEndpoints();
app.MapSessionEndpoints();
app.MapTangleMcp();
app.MapEvaluationEndpoints();
app.MapWebShell();
app.Run();

public partial class Program;
