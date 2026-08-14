using CornersPrediction.Application.Abstractions;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Application.Admin;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Application.Automation;
using CornersPrediction.Infrastructure.Automation;
using CornersPrediction.Application.Betting;
using CornersPrediction.Application.UpcomingMatches;
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
        services.AddScoped<IUserAdminRepository, SqlServerUserAdminRepository>();
        services.AddScoped<ITeamInfoRepository, TeamInfoRepository>();
        services.AddScoped<IUpcomingMatchesRepository, SqlServerUpcomingMatchesRepository>();

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
