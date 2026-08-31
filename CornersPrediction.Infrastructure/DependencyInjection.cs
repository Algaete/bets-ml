using CornersPrediction.Application.Abstractions;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Application.Admin;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Application.Automation;
using CornersPrediction.Application.Automation.BotG;
using CornersPrediction.Application.Automation.BotH;
using CornersPrediction.Infrastructure.Automation;
using CornersPrediction.Application.Betting;
using CornersPrediction.Application.UpcomingMatches;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Application.RobustPickEvaluation;
using CornersPrediction.Infrastructure.Options;
using CornersPrediction.Infrastructure.Persistence;
using CornersPrediction.Infrastructure.Python;
using CornersPrediction.Infrastructure.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace CornersPrediction.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PythonPredictionOptions>(
            configuration.GetSection(PythonPredictionOptions.SectionName));
        services.Configure<PredictionAdjustmentOptions>(
            configuration.GetSection(PredictionAdjustmentOptions.SectionName));
        services.Configure<CornersAutomationOptions>(
            configuration.GetSection(CornersAutomationOptions.SectionName));
        services.Configure<RobustPickEvaluationOptions>(
            configuration.GetSection(RobustPickEvaluationOptions.SectionName));

        var connectionString = configuration.GetConnectionString("CornersDatabase") ??
            "Data Source=../data/corners.db";

        services.AddDbContext<CornersPredictionDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddSingleton<IPythonPredictionRunner, PythonPredictionRunner>();
        services.AddHttpClient(CornersPipelineService.CornersDataClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<CornersAutomationOptions>>()
                .Value;
            client.BaseAddress = new Uri(options.CornersDataApiBaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
            AddInternalApiKey(client, configuration["ApiSecurity:InternalApiKey"]);
        });
        services.AddHttpClient(CornersPipelineService.CornersBotClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<CornersAutomationOptions>>()
                .Value;
            client.BaseAddress = new Uri(options.CornersBotApiBaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
            AddInternalApiKey(client, configuration["ApiSecurity:InternalApiKey"]);
        });
        services.AddScoped<ICornersPipelineService, CornersPipelineService>();
        services.AddScoped<IMatchHistoryRepository, SqlServerMatchHistoryRepository>();
        services.AddScoped<IBettingRepository, SqlServerBettingRepository>();
        services.AddScoped<IAutomatedCornerSelectionsRepository, SqlServerAutomatedCornerSelectionsRepository>();
        services.AddScoped<IAutomatedBotPickSettlementRepository, SqlServerAutomatedBotPickSettlementRepository>();
        services.AddScoped<IRecommendationJobRepository, SqlServerRecommendationJobRepository>();
        services.AddScoped<IRecommendationBotDefinitionRepository, SqlServerRecommendationBotDefinitionRepository>();
        services.AddScoped<SqlServerBotGRepository>();
        services.AddScoped<IBotGCandidateRepository>(provider =>
            provider.GetRequiredService<SqlServerBotGRepository>());
        services.AddScoped<IBotGCandidateReadRepository>(provider =>
            provider.GetRequiredService<SqlServerBotGRepository>());
        services.AddScoped<IBotHShadowLabReadRepository, SqlServerBotHShadowLabRepository>();
        services.AddSingleton<IRobustPickEvaluationRepository, SqlServerRobustPickEvaluationRepository>();
        services.AddScoped<IUserAdminRepository, SqlServerUserAdminRepository>();
        services.AddScoped<ITeamInfoRepository, TeamInfoRepository>();
        services.AddScoped<IUpcomingMatchesRepository, SqlServerUpcomingMatchesRepository>();
        services.AddScoped<SqlServerFootballIntelligenceRepository>();
        services.AddScoped<INewsDocumentRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());
        services.AddScoped<INewsFactRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());
        services.AddScoped<IIntelligenceSnapshotRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());
        services.AddScoped<IFootballSourceRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());
        services.AddScoped<ITeamAliasRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());
        services.AddScoped<IPlayerAliasRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());
        services.AddScoped<IMatchIntelligenceRunRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());
        services.AddScoped<IUpcomingIntelligenceFixtureRepository>(provider =>
            provider.GetRequiredService<SqlServerFootballIntelligenceRepository>());

        return services;
    }

    private static void AddInternalApiKey(HttpClient client, string? internalApiKey)
    {
        if (!string.IsNullOrWhiteSpace(internalApiKey))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Api-Key", internalApiKey);
        }
    }
}
