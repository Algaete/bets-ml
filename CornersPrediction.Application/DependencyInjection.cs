using CornersPrediction.Application.Predictions;
using CornersPrediction.Application.MatchHistory;
using CornersPrediction.Application.Teams;
using CornersPrediction.Application.Betting;
using CornersPrediction.Application.Admin;
using CornersPrediction.Application.UpcomingMatches;
using Microsoft.Extensions.DependencyInjection;

namespace CornersPrediction.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPredictTotalCornersUseCase, PredictTotalCornersUseCase>();
        services.AddScoped<IOverUnderPredictionUseCase, OverUnderPredictionUseCase>();
        services.AddScoped<IShotsOnGoalPredictionUseCase, ShotsOnGoalPredictionUseCase>();
        services.AddScoped<IModelDebugPredictionUseCase, ModelDebugPredictionUseCase>();
        services.AddScoped<ICreateMatchHistoryItemUseCase, CreateMatchHistoryItemUseCase>();
        services.AddScoped<IUpdateMatchHistoryItemUseCase, UpdateMatchHistoryItemUseCase>();
        services.AddScoped<IDeleteMatchHistoryItemUseCase, DeleteMatchHistoryItemUseCase>();
        services.AddScoped<IGetRecentMatchHistoryUseCase, GetRecentMatchHistoryUseCase>();
        services.AddScoped<IGetManualMatchHistoryEntriesUseCase, GetManualMatchHistoryEntriesUseCase>();
        services.AddScoped<IGetPredictionContextUseCase, GetPredictionContextUseCase>();
        services.AddScoped<IGetTeamBi3InfoUseCase, GetTeamBi3InfoUseCase>();
        services.AddScoped<IGetTeamBig3LeaguesUseCase, GetTeamBig3LeaguesUseCase>();
        services.AddScoped<IGetFormationListUseCase, GetFormationListUseCase>();
        services.AddScoped<IGetUpcomingMatchesUseCase, GetUpcomingMatchesUseCase>();
        services.AddScoped<ICreateBettingRecordUseCase, CreateBettingRecordUseCase>();
        services.AddScoped<IUpdateBettingRecordUseCase, UpdateBettingRecordUseCase>();
        services.AddScoped<IDeleteBettingRecordUseCase, DeleteBettingRecordUseCase>();
        services.AddScoped<IGetBettingRecordByIdUseCase, GetBettingRecordByIdUseCase>();
        services.AddScoped<IGetBettingRecordsUseCase, GetBettingRecordsUseCase>();
        services.AddScoped<IGetBettingSummaryUseCase, GetBettingSummaryUseCase>();
        services.AddScoped<ICreateBankrollTransactionUseCase, CreateBankrollTransactionUseCase>();
        services.AddScoped<IGetBankrollTransactionsUseCase, GetBankrollTransactionsUseCase>();
        services.AddScoped<IGetCurrentBankrollUseCase, GetCurrentBankrollUseCase>();
        services.AddScoped<ICreatePlatformUserUseCase, CreatePlatformUserUseCase>();
        services.AddScoped<IUpdatePlatformUserUseCase, UpdatePlatformUserUseCase>();
        services.AddScoped<IDeletePlatformUserUseCase, DeletePlatformUserUseCase>();
        services.AddScoped<IGetPlatformUserByIdUseCase, GetPlatformUserByIdUseCase>();
        services.AddScoped<IGetPlatformUsersUseCase, GetPlatformUsersUseCase>();
        services.AddScoped<IGetPlatformRolesUseCase, GetPlatformRolesUseCase>();

        return services;
    }
}
