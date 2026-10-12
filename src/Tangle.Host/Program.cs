using Tangle.Core;
using Tangle.Data;
using Tangle.Web;
using Tangle.GitHub;
using Tangle.Ai;
using Anthropic;
using Tangle.Mcp;
using Tangle.Sessions;
using Tangle.Host;

var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var key = configuration["ANTHROPIC_API_KEY"];
    if (configuration.GetValue<bool>("Ai:Enabled") && string.IsNullOrWhiteSpace(key))
        throw new InvalidOperationException("Ai:Enabled requires ANTHROPIC_API_KEY in environment or Host user-secrets.");
    return new HaikuDecider(new AnthropicClient { ApiKey = key ?? "", MaxRetries = 0 });
});
builder.Services.AddScoped(services => new AiWorker(services.GetRequiredService<IAiStore>(),
    services.GetRequiredService<HaikuDecider>(), services.GetRequiredService<ILiveEvents>(),
    services.GetRequiredService<ILogger<AiWorker>>(), builder.Configuration.GetValue<bool>("Ai:Enabled")
        && services.GetRequiredService<IUserContext>().UserId == "local",
    services.GetRequiredService<DeciderRegistry>()));
builder.Services.AddSingleton<UserAiQueue>();
builder.Services.AddHostedService(s => s.GetRequiredService<UserAiQueue>());
builder.Services.AddScoped<IAiQueue, ScopedAiQueue>();
builder.Services.AddScoped<ISyncStore>(services => services.GetRequiredService<SyncStore>());
builder.Services.AddScoped(s => new GitHubTokens(s.GetRequiredService<IConfiguration>(),
    () => s.GetRequiredService<IUserContext>().UserId == "local"));
builder.Services.AddHttpClient<IGitHubReader, GitHubReader>();
builder.Services.AddHttpClient<IGitHubActions, GitHubActions>();
builder.Services.AddScoped(s => new SyncService(s.GetRequiredService<IGitHubReader>(), s.GetRequiredService<ISyncStore>(),
    s.GetRequiredService<TimeProvider>(), s.GetRequiredService<ILiveEvents>(), s.GetRequiredService<IAiQueue>(),
    s.GetRequiredService<UserResources>().Sync(s.GetRequiredService<IUserContext>().UserId)));
var app = builder.Build();
await app.Services.GetRequiredService<ControlDatabase>().MigrateAsync();
await app.Services.GetRequiredService<ControlDatabase>().EnsureLocalAsync();
await app.Services.GetRequiredService<UserDatabases>().OpenAsync("local");
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<SessionService>().MatchAsync(CancellationToken.None);
app.UseLocalAuthentication(app.Services.GetRequiredService<RuntimeSettings>());
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
