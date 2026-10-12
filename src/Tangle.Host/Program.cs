using Tangle.Core;
using Tangle.Data;
using Tangle.Web;
using Tangle.GitHub;
using Tangle.Ai;
using Anthropic;
using Tangle.Mcp;
using Tangle.Sessions;

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
builder.Services.AddSingleton(services => new Database(services.GetRequiredService<RuntimeSettings>().DatabasePath));
builder.Services.AddLocalAuthentication();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<SyncStore>();
builder.Services.AddSingleton<AuthoringStore>();
builder.Services.AddSingleton<ActionStore>();
builder.Services.AddSingleton<ActionQueue>();
builder.Services.AddSingleton<PersonalKeys>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddTangleMcp();
builder.Services.AddSingleton<IAiStore, AiStore>();
builder.Services.AddSingleton<IEvaluationStore, EvaluationStore>();
builder.Services.AddSingleton<IExplanationStore, ExplanationStore>();
builder.Services.AddSingleton<IExplanationService, ExplanationService>();
builder.Services.AddSingleton<DeciderRegistry>();
builder.Services.AddSingleton<EvaluationRunner>();
builder.Services.AddSingleton<IEvaluationReports>(s => s.GetRequiredService<EvaluationRunner>());
builder.Services.AddSingleton(s => new JevDecider(new HttpClient(), s.GetRequiredService<IConfiguration>()["TYPESAFE_API_KEY"] ?? ""));
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var key = configuration["ANTHROPIC_API_KEY"];
    if (configuration.GetValue<bool>("Ai:Enabled") && string.IsNullOrWhiteSpace(key))
        throw new InvalidOperationException("Ai:Enabled requires ANTHROPIC_API_KEY in environment or Host user-secrets.");
    return new HaikuDecider(new AnthropicClient { ApiKey = key ?? "", MaxRetries = 0 });
});
builder.Services.AddSingleton(services => new AiWorker(services.GetRequiredService<IAiStore>(),
    services.GetRequiredService<HaikuDecider>(), services.GetRequiredService<ILiveEvents>(),
    services.GetRequiredService<ILogger<AiWorker>>(), builder.Configuration.GetValue<bool>("Ai:Enabled"),
    services.GetRequiredService<DeciderRegistry>()));
builder.Services.AddSingleton<IAiQueue>(s => s.GetRequiredService<AiWorker>());
builder.Services.AddHostedService(s => s.GetRequiredService<AiWorker>());
builder.Services.AddSingleton<ISyncStore>(services => services.GetRequiredService<SyncStore>());
builder.Services.AddSingleton<GitHubTokens>();
builder.Services.AddHttpClient<IGitHubReader, GitHubReader>();
builder.Services.AddHttpClient<IGitHubActions, GitHubActions>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<EventHub>();
builder.Services.AddSingleton<ILiveEvents>(services => services.GetRequiredService<EventHub>());
var app = builder.Build();
var applied = await app.Services.GetRequiredService<Database>().MigrateAsync();
app.Logger.LogInformation("Applied {Count} database migrations", applied);
await app.Services.GetRequiredService<SyncStore>().RecomputeAsync();
await app.Services.GetRequiredService<SessionService>().MatchAsync(CancellationToken.None);
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
