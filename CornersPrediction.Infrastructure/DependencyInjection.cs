using CornersPrediction.Application.Abstractions;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Application.Admin;
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

        var connectionString = configuration.GetConnectionString("CornersDatabase") ??
            "Data Source=../data/corners.db";

        services.AddDbContext<CornersPredictionDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddSingleton<IPythonPredictionRunner, PythonPredictionRunner>();
        services.AddScoped<IMatchHistoryRepository, SqlServerMatchHistoryRepository>();
        services.AddScoped<IBettingRepository, SqlServerBettingRepository>();
        services.AddScoped<IUserAdminRepository, SqlServerUserAdminRepository>();
        services.AddScoped<ITeamInfoRepository, TeamInfoRepository>();
        services.AddScoped<IUpcomingMatchesRepository, SqlServerUpcomingMatchesRepository>();

        return services;
    }
}
