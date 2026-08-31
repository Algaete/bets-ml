using AutomatedCornersBot.Api;
using CornersMLData.Data;
using CornersMLData.Services;
using CornersPrediction.Application;
using CornersPrediction.Application.Automation.BotC;
using CornersPrediction.Application.Automation.BotG;
using CornersPrediction.Application.Automation.BotI;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Application.RobustPickEvaluation;
using CornersPrediction.Infrastructure;
using CornersPrediction.Infrastructure.Persistence;
using CornersPredictionApi.ApiFootball;
using CornersPredictionApi.CompetitionFiltering;
using CornersPredictionApi.NewGenerationMl;
using CornersPredictionApi.RecommendationJobs;
using CornersPredictionApi.FootballIntelligence;
using DotNetEnv;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

LoadDotEnv(builder.Environment.ContentRootPath);
builder.Configuration.AddInMemoryCollection(BuildDeploymentConfigurationFromEnvironment());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.DictionaryKeyPolicy = null;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<AutomatedBotOptions>(builder.Configuration.GetSection("AutomatedBot"));
builder.Services.Configure<BotCMetaModelOptions>(builder.Configuration.GetSection(BotCMetaModelOptions.SectionName));
builder.Services.AddSingleton<IBotCMetaModelPredictor, FileBotCMetaModelPredictor>();
builder.Services.Configure<BotGArtifactOptions>(builder.Configuration.GetSection(BotGArtifactOptions.SectionName));
builder.Services.AddOptions<BotGShadowSettlementOptions>()
    .Bind(builder.Configuration.GetSection(BotGShadowSettlementOptions.SectionName))
    .Validate(options => options.StartupDelaySeconds is >= 0 and <= 3600
        && options.PollMinutes is >= 1 and <= 1440
        && options.MaximumCandidates is >= 1 and <= 50_000,
        "Bot G shadow settlement limits are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<RobustPickEvaluationOptions>()
    .Bind(builder.Configuration.GetSection(RobustPickEvaluationOptions.SectionName))
    .Validate(options => options.Mode is "Shadow" or "Enforce" or "Disabled"
        && !string.IsNullOrWhiteSpace(options.Version)
        && options.SimulationCount is >= 100 and <= 100_000
        && options.OuterScenarioCount is >= 10 and <= 20_000
        && options.ProbabilityLowerQuantile is > 0 and < 0.5m
        && options.ProbabilityUpperQuantile is > 0.5m and < 1m
        && options.OutcomeAvailabilityLagHours is >= 0 and <= 168
        && options.EvaluationTimeoutSeconds is >= 1 and <= 600
        && options.MinimumReevaluationIntervalSeconds is >= 0 and <= 86_400
        && options.SignificantOddsMovement is > 0 and <= 10m
        && options.SignificantLineMovement is > 0 and <= 100m
        && options.DefaultMaxOddsAgeSeconds is >= 1 and <= 86_400
        && options.MaxLineupOddsAgeSeconds is >= 1 and <= 86_400
        && options.MaxOddsAgeSecondsBySource.Count > 0
        && options.MaxOddsAgeSecondsBySource.All(pair =>
            !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is >= 1 and <= 86_400)
        && options.Residuals.MinimumEffectiveN >= 1m
        && options.Residuals.TargetEffectiveN >= options.Residuals.MinimumEffectiveN
        && options.Residuals.RecencyHalfLifeDays > 0m
        && options.Residuals.ErrorScaleEpsilon > 0m
        && options.Policy.MinPositiveEvStability is >= 0m and <= 1m
        && options.Policy.MinScenarioSideStability is >= 0m and <= 1m
        && options.Policy.MinCalibrationReliability is >= 0m and <= 1m
        && !options.Stake.AllowIncrease
        && options.Stake.HighRobustnessThreshold >= options.Stake.MediumRobustnessThreshold
        && options.Stake.MediumRobustnessThreshold >= options.Stake.MinimumRobustnessThreshold
        && options.Stake.MinimumRobustnessThreshold is >= 0m and <= 1m
        && options.Stake.HighRobustnessThreshold is >= 0m and <= 1m
        && options.Exposure.MaximumStakePerFixture > 0m
        && options.Exposure.MaximumStakePerTeam > 0m
        && options.Exposure.MaximumStakePerCorrelationCluster > 0m
        && options.Exposure.MaximumRelatedPicksPerFixture > 0,
        "Robust Pick Evaluation configuration is invalid or would permit a v1 stake increase.")
    .ValidateOnStart();
builder.Services.AddSingleton<BotGArtifactRuntime>();
builder.Services.AddSingleton<IBotGMetaModelService>(provider =>
    provider.GetRequiredService<BotGArtifactRuntime>());
builder.Services.AddSingleton<IBotGArtifactEvidenceProvider>(provider =>
    provider.GetRequiredService<BotGArtifactRuntime>());
builder.Services.Configure<CompetitionFilterOptions>(
    builder.Configuration.GetSection(CompetitionFilterOptions.SectionName));
builder.Services.AddSingleton<CompetitionEligibilityPolicy>();
builder.Services.AddScoped<CornersMLData.Data.MatchHistoryRepository>();
builder.Services.AddScoped<PartidosProximosRepository>();
builder.Services.AddScoped<BetanoUpcomingOddsRepository>();
builder.Services.AddScoped<PinnacleUpcomingOddsRepository>();
builder.Services.AddScoped<TeamPositionResolver>();
builder.Services.AddSingleton<CanonicalNameNormalizationRepository>();
builder.Services.AddScoped<BetanoUpcomingOddsScraper>();
builder.Services.AddHttpClient<PinnacleUpcomingOddsScraper>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CornersPrediction-Pinnacle/2.0");
});
builder.Services.AddSingleton<SqlAutomationRepository>();
builder.Services.AddSingleton<FeatureBuilder>();
builder.Services.AddHttpClient<PredictionApiClient>();
builder.Services.AddScoped<BotGAutomationService>();
builder.Services.AddScoped<AutomatedCornersSelectionService>();
builder.Services.AddHostedService<BotGShadowSettlementWorker>();
builder.Services.AddOptions<BotIShadowCollectorOptions>()
    .Bind(builder.Configuration.GetSection(BotIShadowCollectorOptions.SectionName))
    .Validate(options => options.StartupDelaySeconds is >= 0 and <= 3600
        && options.PollMinutes is >= 1 and <= 1440
        && options.FixtureLookAheadDays is >= 1 and <= 14
        && options.MaximumFixtures is >= 1 and <= 1000,
        "Bot I shadow collector limits are invalid.")
    .ValidateOnStart();
builder.Services.AddHostedService<BotIShadowCollectorWorker>();
builder.Services.Configure<ApiFootballOptions>(builder.Configuration.GetSection(ApiFootballOptions.SectionName));
builder.Services.AddHttpClient<ApiFootballClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiFootballOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CornersPrediction-ApiFootball/1.0");
});
builder.Services.AddScoped<ApiFootballRepository>();
builder.Services.AddScoped<ApiFootballSyncService>();
builder.Services.AddScoped<ApiFootballBotPickReconciliationService>();
builder.Services.AddScoped<ApiFootballUpcomingMatchesSyncService>();
builder.Services.AddOptions<FootballIntelligenceOptions>()
    .Bind(builder.Configuration.GetSection(FootballIntelligenceOptions.SectionName))
    .Validate(options => options.FixtureLookAheadHours is >= 1 and <= 168
        && options.MaxConcurrentFixtures is >= 1 and <= 8
        && options.WorkerPollMinutes is >= 1 and <= 60
        && options.ArticleMaxCharacters is >= 500 and <= 100_000
        && options.MaximumQueriesPerTeam is >= 1 and <= 30
        && options.MaximumArticlesPerTeam is >= 1 and <= 30
        && options.CutoffsMinutesBeforeKickoff.Length > 0
        && options.CutoffsMinutesBeforeKickoff.All(value => value >= 0),
        "FootballIntelligence limits and cutoff schedule are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<NewsSearchOptions>()
    .Bind(builder.Configuration.GetSection(NewsSearchOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _)
        && options.TimeoutSeconds is >= 5 and <= 120
        && options.MaximumResultsPerQuery is >= 1 and <= 20
        && options.MinimumRequestDelayMilliseconds >= 0,
        "FootballIntelligence news-search options are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<NewsLlmOptions>()
    .Bind(builder.Configuration.GetSection(NewsLlmOptions.SectionName))
    .Validate(options => !options.Enabled
        || (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(options.ApiKey)
            && !string.IsNullOrWhiteSpace(options.Model)
            && !string.IsNullOrWhiteSpace(options.PromptVersion)
            && options.TimeoutSeconds is >= 5 and <= 180),
        "FootballIntelligence OpenAI options are incomplete or invalid.")
    .ValidateOnStart();
builder.Services.AddScoped<IStructuredFootballDataProvider, ApiFootballStructuredDataProvider>();
builder.Services.AddScoped<IEntityResolver, FootballEntityResolver>();
builder.Services.AddHttpClient<BraveNewsSearchProvider>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsSearchOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CornersPrediction-IntelligenceSearch/1.0");
});
builder.Services.AddScoped<INewsSearchProvider>(provider =>
    provider.GetRequiredService<BraveNewsSearchProvider>());
builder.Services.AddHttpClient<HttpArticleContentExtractor>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CornersPrediction-ArticleExtractor/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    AutomaticDecompression = System.Net.DecompressionMethods.GZip
        | System.Net.DecompressionMethods.Deflate
});
builder.Services.AddScoped<IArticleContentExtractor>(provider =>
    provider.GetRequiredService<HttpArticleContentExtractor>());
builder.Services.AddHttpClient<OpenAiNewsFactExtractor>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsLlmOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 180));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CornersPrediction-FootballNews/1.0");
});
builder.Services.AddScoped<ILlmFactExtractionClient>(provider =>
    provider.GetRequiredService<OpenAiNewsFactExtractor>());
builder.Services.AddScoped<INewsFactExtractor>(provider =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsLlmOptions>>().Value;
    return options.Enabled
        ? provider.GetRequiredService<OpenAiNewsFactExtractor>()
        : provider.GetRequiredService<RuleBasedNewsFactExtractor>();
});
builder.Services.AddScoped<IMatchIntelligenceService, MatchIntelligenceService>();
builder.Services.AddHostedService<UpcomingFixtureIntelligenceWorker>();
builder.Services.AddSingleton<ApiFootballHistoricalBatchCoordinator>();
builder.Services.Configure<NewGenerationMlOptions>(
    builder.Configuration.GetSection(NewGenerationMlOptions.SectionName));
builder.Services.AddSingleton<NewGenerationModelPackage>();
builder.Services.AddSingleton<NewGenerationPythonRunner>();
builder.Services.AddScoped<NewGenerationFeatureBuilder>();
builder.Services.AddScoped<NewGenerationPredictionService>();
builder.Services.Configure<RecommendationJobOptions>(
    builder.Configuration.GetSection(RecommendationJobOptions.SectionName));
builder.Services.AddHostedService<RecommendationJobWorker>();
builder.Services.AddSingleton<FootballIntelligenceSchemaInitializer>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

var internalApiKey = app.Configuration["ApiSecurity:InternalApiKey"];
if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(internalApiKey))
{
    throw new InvalidOperationException("INTERNAL_API_KEY must be configured for non-development API deployments.");
}

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    if (string.IsNullOrWhiteSpace(internalApiKey) ||
        context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Internal-Api-Key", out var providedKey) ||
        !ApiKeysMatch(internalApiKey, providedKey.FirstOrDefault()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid internal API key." });
        return;
    }

    await next();
});

var swaggerEnabled = app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("Swagger:Enabled");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Corners Prediction API v1");
    });
}

app.MapControllers();

app.Logger.LogInformation("Initializing the local API database");
await DatabaseInitializer.EnsureDatabaseCreatedAsync(app.Services);
app.Logger.LogInformation("Local API database is ready");

// Finish schema verification before accepting requests. Running the same work
// from ApplicationStarted allowed the first dashboard requests to queue behind
// the migration semaphore and then hit their UI timeout even though SQL was
// healthy. Startup now has one honest readiness boundary instead of exposing a
// half-ready API.
if (!string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("DefaultConnection")))
{
    app.Logger.LogInformation("Initializing robot database objects");
    await InitializeRobotDatabaseAsync(app.Services);
}

app.Run();

static void LoadDotEnv(string contentRootPath)
{
    var candidatePaths = new[]
    {
        Path.Combine(contentRootPath, ".env"),
        Path.GetFullPath(Path.Combine(contentRootPath, "..", ".env"))
    };

    var envPath = candidatePaths.FirstOrDefault(File.Exists);
    if (envPath is not null)
    {
        Env.Load(envPath);
    }
}

static IDictionary<string, string?> BuildDeploymentConfigurationFromEnvironment()
{
    return new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] =
            Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING"),
        ["ApiSecurity:InternalApiKey"] = Environment.GetEnvironmentVariable("INTERNAL_API_KEY"),
        ["Swagger:Enabled"] = Environment.GetEnvironmentVariable("SWAGGER_ENABLED"),
        ["PythonPrediction:PythonExecutable"] = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE"),
        ["PythonPrediction:ScriptPath"] = Environment.GetEnvironmentVariable("PYTHON_PREDICT_SCRIPT_PATH"),
        ["PythonPrediction:ShotsOnGoalScriptPath"] = Environment.GetEnvironmentVariable("PYTHON_SHOTS_SOG_SCRIPT_PATH"),
        ["PythonPrediction:ShotsOnGoalModelPath"] = Environment.GetEnvironmentVariable("PYTHON_SHOTS_SOG_MODEL_PATH"),
        ["PythonPrediction:ModelDebugScriptPath"] = Environment.GetEnvironmentVariable("PYTHON_MODEL_DEBUG_SCRIPT_PATH"),
        ["PythonPrediction:ModelDebugModelPath"] = Environment.GetEnvironmentVariable("PYTHON_MODEL_DEBUG_MODEL_PATH"),
        ["CornersAutomation:CornersDataApiBaseUrl"] = Environment.GetEnvironmentVariable("CORNERS_DATA_API_BASE_URL"),
        ["CornersAutomation:CornersBotApiBaseUrl"] = Environment.GetEnvironmentVariable("CORNERS_BOT_API_BASE_URL"),
        ["AutomatedBot:SqlConnectionString"] =
            Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING"),
        ["AutomatedBot:PredictionApi:BaseUrl"] =
            Environment.GetEnvironmentVariable("AUTOMATED_BOT_PREDICTION_API_BASE_URL"),
        ["AutomatedBot:PredictionApi:InternalApiKey"] = Environment.GetEnvironmentVariable("INTERNAL_API_KEY"),
        ["ApiFootball:ApiKey"] = Environment.GetEnvironmentVariable("API_FOOTBALL_KEY"),
        ["FootballIntelligence:Enabled"] = Environment.GetEnvironmentVariable("FOOTBALL_INTELLIGENCE_ENABLED"),
        ["FootballIntelligence:WorkerEnabled"] = Environment.GetEnvironmentVariable("FOOTBALL_INTELLIGENCE_WORKER_ENABLED"),
        ["FootballIntelligence:WorkerPollMinutes"] = Environment.GetEnvironmentVariable("FOOTBALL_INTELLIGENCE_WORKER_POLL_MINUTES"),
        ["FootballIntelligence:NewsSearch:Provider"] = Environment.GetEnvironmentVariable("FOOTBALL_NEWS_SEARCH_PROVIDER"),
        ["FootballIntelligence:NewsSearch:ApiKey"] = Environment.GetEnvironmentVariable("BRAVE_SEARCH_API_KEY"),
        ["FootballIntelligence:OpenAI:Enabled"] = Environment.GetEnvironmentVariable("FOOTBALL_NEWS_OPENAI_ENABLED"),
        ["FootballIntelligence:OpenAI:ApiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        ["FootballIntelligence:OpenAI:Model"] = Environment.GetEnvironmentVariable("FOOTBALL_NEWS_OPENAI_MODEL"),
        ["NewGenerationMl:ModelsRoot"] = Environment.GetEnvironmentVariable("NEW_GENERATION_ML_MODELS_ROOT"),
        ["NewGenerationMl:ActiveVersion"] = Environment.GetEnvironmentVariable("NEW_GENERATION_ML_ACTIVE_VERSION"),
        ["NewGenerationMl:PythonExecutable"] = Environment.GetEnvironmentVariable("NEW_GENERATION_ML_PYTHON_EXECUTABLE"),
        ["NewGenerationMl:ScriptPath"] = Environment.GetEnvironmentVariable("NEW_GENERATION_ML_SCRIPT_PATH"),
        ["NewGenerationMl:TimeoutSeconds"] = Environment.GetEnvironmentVariable("NEW_GENERATION_ML_TIMEOUT_SECONDS"),
        ["BotCMetaModel:Enabled"] = Environment.GetEnvironmentVariable("BOT_C_META_MODEL_ENABLED"),
        ["BotCMetaModel:ArtifactPath"] = Environment.GetEnvironmentVariable("BOT_C_META_MODEL_ARTIFACT_PATH"),
        ["BotCMetaModel:ArtifactPaths:bot-d-features-1.0.0"] = Environment.GetEnvironmentVariable("BOT_D_META_MODEL_ARTIFACT_PATH"),
        ["BotG:Enabled"] = Environment.GetEnvironmentVariable("BOT_G_ENABLED"),
        ["BotG:ArtifactPath"] = Environment.GetEnvironmentVariable("BOT_G_ARTIFACT_PATH"),
        ["BotIShadowCollector:Enabled"] = Environment.GetEnvironmentVariable("BOT_I_SHADOW_COLLECTOR_ENABLED"),
        ["BotIShadowCollector:PollMinutes"] = Environment.GetEnvironmentVariable("BOT_I_SHADOW_COLLECTOR_POLL_MINUTES"),
        ["BotIShadowCollector:FixtureLookAheadDays"] = Environment.GetEnvironmentVariable("BOT_I_SHADOW_COLLECTOR_LOOKAHEAD_DAYS"),
        ["BotIShadowCollector:MaximumFixtures"] = Environment.GetEnvironmentVariable("BOT_I_SHADOW_COLLECTOR_MAX_FIXTURES"),
        ["RobustPickEvaluation:Enabled"] = Environment.GetEnvironmentVariable("ROBUST_PICK_EVALUATION_ENABLED"),
        ["RobustPickEvaluation:Mode"] = Environment.GetEnvironmentVariable("ROBUST_PICK_EVALUATION_MODE"),
        ["RobustPickEvaluation:Version"] = Environment.GetEnvironmentVariable("ROBUST_PICK_EVALUATION_VERSION"),
        ["RobustPickEvaluation:OutcomeAvailabilityLagHours"] = Environment.GetEnvironmentVariable("ROBUST_PICK_OUTCOME_AVAILABILITY_LAG_HOURS"),
        ["RobustPickEvaluation:DefaultMaxOddsAgeSeconds"] = Environment.GetEnvironmentVariable("ROBUST_PICK_DEFAULT_MAX_ODDS_AGE_SECONDS"),
        ["PinnacleGuestApi:BaseUrl"] = Environment.GetEnvironmentVariable("PINNACLE_GUEST_API_BASE_URL"),
        ["PinnacleGuestApi:ApiKey"] = Environment.GetEnvironmentVariable("PINNACLE_GUEST_API_KEY"),
        ["BetanoScraping:BrowserChannel"] = Environment.GetEnvironmentVariable("BETANO_BROWSER_CHANNEL"),
        ["BetanoScraping:Headless"] = Environment.GetEnvironmentVariable("BETANO_HEADLESS"),
        ["BetanoScraping:ProfilePath"] = Environment.GetEnvironmentVariable("BETANO_PROFILE_PATH"),
        ["RecommendationJobs:Enabled"] = Environment.GetEnvironmentVariable("RECOMMENDATION_JOBS_ENABLED"),
        ["RecommendationJobs:Recurring:Enabled"] = Environment.GetEnvironmentVariable("RECOMMENDATION_RECURRING_ENABLED"),
        ["RecommendationJobs:Recurring:IntervalMinutes"] = Environment.GetEnvironmentVariable("RECOMMENDATION_RECURRING_INTERVAL_MINUTES"),
        ["RecommendationJobs:Recurring:LookAheadDays"] = Environment.GetEnvironmentVariable("RECOMMENDATION_RECURRING_LOOKAHEAD_DAYS")
    }
    .Where(setting => !string.IsNullOrWhiteSpace(setting.Value))
    .ToDictionary(setting => setting.Key, setting => setting.Value);
}

static bool ApiKeysMatch(string expected, string? provided)
{
    if (string.IsNullOrWhiteSpace(provided))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var providedBytes = Encoding.UTF8.GetBytes(provided);

    return expectedBytes.Length == providedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
}

static async Task InitializeRobotDatabaseAsync(IServiceProvider services)
{
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RobotDatabaseInitializer");

    try
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<CanonicalNameNormalizationRepository>()
            .EnsureReadyAsync();
        await scope.ServiceProvider
            .GetRequiredService<ApiFootballRepository>()
            .EnsureSchemaAsync(CancellationToken.None);
        await scope.ServiceProvider
            .GetRequiredService<SqlAutomationRepository>()
            .EnsureSchemaAsync(CancellationToken.None);
        await scope.ServiceProvider
            .GetRequiredService<IRobustPickEvaluationRepository>()
            .EnsureSchemaAsync(CancellationToken.None);
        await scope.ServiceProvider
            .GetRequiredService<FootballIntelligenceSchemaInitializer>()
            .EnsureReadyAsync(CancellationToken.None);
        await EnsureMatchHistoryPerformanceIndexesAsync(scope.ServiceProvider, CancellationToken.None);
        logger.LogInformation("Robot database objects are ready.");
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Robot database initialization failed. Robot endpoints remain available for retry.");
    }
}

static async Task EnsureMatchHistoryPerformanceIndexesAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    var environment = services.GetRequiredService<IWebHostEnvironment>();
    var options = services
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<AutomatedBotOptions>>()
        .Value;
    var scriptPath = Path.Combine(environment.ContentRootPath, "SqlScripts", "MatchHistoryPerformanceIndexes.sql");
    if (!File.Exists(scriptPath))
    {
        throw new FileNotFoundException("The MatchHistory performance index script was not found.", scriptPath);
    }

    var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
    await using var connection = new SqlConnection(options.ResolveSqlConnectionString());
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.CommandType = CommandType.Text;
    command.CommandTimeout = 600;
    await command.ExecuteNonQueryAsync(cancellationToken);
}
