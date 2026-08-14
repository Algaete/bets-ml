using System.Data;
using System.Text.Json;
using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerAutomatedBotPickSettlementRepository : IAutomatedBotPickSettlementRepository
{
    private const string PendingCandidatesSql = """
        WITH PendingSelections AS
        (
            SELECT TOP (@MaxRows)
                s.AutomatedCornerBetSelectionId,
                ReconcileExistingSettlement = CONVERT(BIT, CASE WHEN s.Status = N'Pending' THEN 0 ELSE 1 END),
                ExpectedSettledAtUtc = s.SettledAtUtc,
                s.MarketType,
                s.SelectedSide,
                s.LineValue,
                s.Odds,
                s.Stake,
                s.MatchHistoryId AS StoredMatchHistoryId,
                s.ApiFootballFixtureId AS StoredApiFootballFixtureId,
                s.MatchDate,
                s.HomeTeam,
                s.StandardizedHomeTeam,
                s.AwayTeam,
                s.StandardizedAwayTeam,
                normalized.CanonicalHomeTeam,
                normalized.CanonicalAwayTeam,
                normalized.ExpectedUtcDate,
                normalized.AutomationVersionKey,
                normalized.DecisionBotKey
            FROM dbo.AutomatedCornerBetSelections s WITH (READPAST)
            CROSS APPLY
            (
                VALUES
                (
                    dbo.fn_CanonicalTeamName(
                        COALESCE(NULLIF(s.StandardizedHomeTeam, N''), s.HomeTeam)),
                    dbo.fn_CanonicalTeamName(
                        COALESCE(NULLIF(s.StandardizedAwayTeam, N''), s.AwayTeam)),
                    CAST(
                        s.MatchDate AT TIME ZONE 'Pacific SA Standard Time' AT TIME ZONE 'UTC'
                        AS DATE),
                    UPPER(LTRIM(RTRIM(COALESCE(s.AutomationVersion, N'')))),
                    UPPER(LTRIM(RTRIM(COALESCE(
                        JSON_VALUE(
                            CASE WHEN ISJSON(s.DecisionReason) = 1 THEN s.DecisionReason ELSE N'{}' END,
                            '$.botProfile'),
                        N''))))
                )
            ) normalized(
                CanonicalHomeTeam,
                CanonicalAwayTeam,
                ExpectedUtcDate,
                AutomationVersionKey,
                DecisionBotKey)
            WHERE
            (
                s.Status = N'Pending'
                OR
                (
                    s.Status IN (N'Won', N'Lost', N'Push')
                    AND s.SettlementSource IN (N'LocalMatchHistory', N'LocalMatchHistoryHistorical')
                    AND s.SettledAtUtc IS NOT NULL
                    AND
                    (
                        EXISTS
                        (
                            SELECT 1
                            FROM dbo.MatchHistory refreshed
                            WHERE refreshed.Id = s.MatchHistoryId
                              AND refreshed.ApiFootballUpdatedAtUtc > s.SettledAtUtc
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.MatchHistory refreshed
                            WHERE s.ApiFootballFixtureId IS NOT NULL
                              AND refreshed.ApiFootballFixtureId = s.ApiFootballFixtureId
                              AND refreshed.ApiFootballUpdatedAtUtc > s.SettledAtUtc
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.MatchHistory refreshed
                            WHERE refreshed.ApiFootballFixtureId IS NOT NULL
                              AND
                              (
                                  refreshed.Id = s.MatchHistoryId
                                  OR
                                  (
                                      s.ApiFootballFixtureId IS NOT NULL
                                      AND refreshed.ApiFootballFixtureId = s.ApiFootballFixtureId
                                  )
                              )
                              AND
                              (
                                  (s.MarketType IN (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals')
                                      AND ISNULL(refreshed.ApiFootballGoalsAvailable, 0) = 0)
                                  OR (s.MarketType IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')
                                      AND ISNULL(refreshed.ApiFootballCornersAvailable, 0) = 0)
                                  OR (s.MarketType IN (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots')
                                      AND ISNULL(refreshed.ApiFootballShotsAvailable, 0) = 0)
                                  OR (s.MarketType IN (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal')
                                      AND ISNULL(refreshed.ApiFootballShotsOnGoalAvailable, 0) = 0)
                              )
                        )
                    )
                )
            )
              AND (@MatchDateTo IS NULL OR s.MatchDate < DATEADD(DAY, 1, @MatchDateTo))
              AND
              (
                  @MarketFamily IS NULL
                  OR (@MarketFamily = N'CORNERS' AND s.MarketType IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners'))
                  OR (@MarketFamily = N'GOALS' AND s.MarketType IN (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals'))
                  OR (@MarketFamily = N'SHOTS' AND s.MarketType IN (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots'))
                  OR (@MarketFamily = N'SOG' AND s.MarketType IN (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal'))
              )
              AND
              (
                  @BotKey IS NULL
                  OR
                  (
                      @BotKey = N'A'
                      AND (RIGHT(normalized.AutomationVersionKey, 2) = N'-A' OR normalized.DecisionBotKey = N'A')
                  )
                  OR
                  (
                      @BotKey = N'B'
                      AND (RIGHT(normalized.AutomationVersionKey, 2) = N'-B' OR normalized.DecisionBotKey = N'B')
                  )
                  OR
                  (
                      @BotKey = N'C2026'
                      AND
                      (
                          RIGHT(normalized.AutomationVersionKey, 6) = N'-C2026'
                          OR normalized.DecisionBotKey IN (N'C', N'C2026')
                      )
                  )
                  OR
                  (
                      @BotKey = N'D2026'
                      AND
                      (
                          RIGHT(normalized.AutomationVersionKey, 6) = N'-D2026'
                          OR normalized.DecisionBotKey IN (N'D', N'D2026')
                      )
                  )
                  OR
                  (
                      @BotKey = N'E2026'
                      AND
                      (
                          RIGHT(normalized.AutomationVersionKey, 6) = N'-E2026'
                          OR normalized.DecisionBotKey IN (N'E', N'E2026')
                      )
                  )
                  OR
                  (
                      @BotKey = N'F2026'
                      AND
                      (
                          RIGHT(normalized.AutomationVersionKey, 6) = N'-F2026'
                          OR normalized.DecisionBotKey IN (N'F', N'F2026')
                      )
                  )
                  OR
                  (
                      @BotKey = N'LEGACY'
                      AND RIGHT(normalized.AutomationVersionKey, 2) <> N'-A'
                      AND RIGHT(normalized.AutomationVersionKey, 2) <> N'-B'
                      AND RIGHT(normalized.AutomationVersionKey, 6) <> N'-C2026'
                      AND RIGHT(normalized.AutomationVersionKey, 6) <> N'-D2026'
                      AND RIGHT(normalized.AutomationVersionKey, 6) <> N'-E2026'
                      AND RIGHT(normalized.AutomationVersionKey, 6) <> N'-F2026'
                      AND normalized.DecisionBotKey NOT IN
                          (N'A', N'B', N'C', N'C2026', N'D', N'D2026', N'E', N'E2026', N'F', N'F2026')
                  )
                  OR
                  (
                      @BotKey NOT IN (N'A', N'B', N'C2026', N'D2026', N'E2026', N'F2026', N'LEGACY')
                      AND
                      (
                          normalized.DecisionBotKey = @BotKey
                          OR RIGHT(normalized.AutomationVersionKey, LEN(@BotKey) + 1) = N'-' + @BotKey
                      )
                  )
              )
            ORDER BY s.MatchDate, s.AutomatedCornerBetSelectionId
        ),
        CandidateMatchesRaw AS
        (
            SELECT
                s.AutomatedCornerBetSelectionId,
                MatchHistoryId = CONVERT(BIGINT, mh.Id),
                mh.ApiFootballFixtureId, mh.FixtureStatus, mh.ApiFootballUpdatedAtUtc AS SourceUpdatedAtUtc,
                HomeGoals = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 0 THEN NULL ELSE mh.HomeGoals END,
                AwayGoals = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 0 THEN NULL ELSE mh.AwayGoals END,
                HomeCorners = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 0 THEN NULL ELSE mh.HomeCorners END,
                AwayCorners = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 0 THEN NULL ELSE mh.AwayCorners END,
                HomeShots = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 0 THEN NULL ELSE mh.HomeShots END,
                AwayShots = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 0 THEN NULL ELSE mh.AwayShots END,
                HomeShotsOnGoal = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 0 THEN NULL ELSE mh.HomeShotsOnGoal END,
                AwayShotsOnGoal = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 0 THEN NULL ELSE mh.AwayShotsOnGoal END,
                LinkMethod = CONVERT(NVARCHAR(40), N'MatchHistoryId'),
                LinkPriority = 0,
                DateDistanceDays = ABS(DATEDIFF(DAY, s.ExpectedUtcDate, mh.MatchDate)),
                IsFinal = CASE
                    WHEN UPPER(LTRIM(RTRIM(COALESCE(mh.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
                        THEN 1
                    ELSE 0
                END
            FROM PendingSelections s
            INNER JOIN dbo.MatchHistory mh
                ON mh.Id = s.StoredMatchHistoryId
            WHERE s.StoredMatchHistoryId IS NOT NULL

            UNION ALL

            SELECT
                s.AutomatedCornerBetSelectionId,
                MatchHistoryId = CONVERT(BIGINT, mh.Id),
                mh.ApiFootballFixtureId, mh.FixtureStatus, mh.ApiFootballUpdatedAtUtc AS SourceUpdatedAtUtc,
                HomeGoals = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 0 THEN NULL ELSE mh.HomeGoals END,
                AwayGoals = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 0 THEN NULL ELSE mh.AwayGoals END,
                HomeCorners = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 0 THEN NULL ELSE mh.HomeCorners END,
                AwayCorners = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 0 THEN NULL ELSE mh.AwayCorners END,
                HomeShots = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 0 THEN NULL ELSE mh.HomeShots END,
                AwayShots = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 0 THEN NULL ELSE mh.AwayShots END,
                HomeShotsOnGoal = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 0 THEN NULL ELSE mh.HomeShotsOnGoal END,
                AwayShotsOnGoal = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 0 THEN NULL ELSE mh.AwayShotsOnGoal END,
                LinkMethod = CONVERT(NVARCHAR(40), N'ApiFootballFixtureId'),
                LinkPriority = 1,
                DateDistanceDays = ABS(DATEDIFF(DAY, s.ExpectedUtcDate, mh.MatchDate)),
                IsFinal = CASE
                    WHEN UPPER(LTRIM(RTRIM(COALESCE(mh.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
                        THEN 1
                    ELSE 0
                END
            FROM PendingSelections s
            INNER JOIN dbo.MatchHistory mh
                ON mh.ApiFootballFixtureId = s.StoredApiFootballFixtureId
            WHERE s.StoredApiFootballFixtureId IS NOT NULL

            UNION ALL

            SELECT
                s.AutomatedCornerBetSelectionId,
                MatchHistoryId = CONVERT(BIGINT, mh.Id),
                mh.ApiFootballFixtureId, mh.FixtureStatus, mh.ApiFootballUpdatedAtUtc AS SourceUpdatedAtUtc,
                HomeGoals = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 0 THEN NULL ELSE mh.HomeGoals END,
                AwayGoals = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 0 THEN NULL ELSE mh.AwayGoals END,
                HomeCorners = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 0 THEN NULL ELSE mh.HomeCorners END,
                AwayCorners = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 0 THEN NULL ELSE mh.AwayCorners END,
                HomeShots = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 0 THEN NULL ELSE mh.HomeShots END,
                AwayShots = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 0 THEN NULL ELSE mh.AwayShots END,
                HomeShotsOnGoal = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 0 THEN NULL ELSE mh.HomeShotsOnGoal END,
                AwayShotsOnGoal = CASE WHEN mh.ApiFootballFixtureId IS NOT NULL AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 0 THEN NULL ELSE mh.AwayShotsOnGoal END,
                LinkMethod = CONVERT(NVARCHAR(40), N'CanonicalUtcFallback'),
                LinkPriority = 2,
                DateDistanceDays = ABS(DATEDIFF(DAY, s.ExpectedUtcDate, mh.MatchDate)),
                IsFinal = CASE
                    WHEN UPPER(LTRIM(RTRIM(COALESCE(mh.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
                        THEN 1
                    ELSE 0
                END
            FROM PendingSelections s
            INNER JOIN dbo.MatchHistory mh
                ON mh.MatchDate BETWEEN DATEADD(DAY, -1, s.ExpectedUtcDate)
                                    AND DATEADD(DAY, 1, s.ExpectedUtcDate)
            LEFT JOIN dbo.TeamNameAlias homeAlias
                ON homeAlias.AliasKey = dbo.fn_NormalizeNameKey(
                    COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam))
            LEFT JOIN dbo.TeamNameAlias awayAlias
                ON awayAlias.AliasKey = dbo.fn_NormalizeNameKey(
                    COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam))
            WHERE COALESCE(
                    homeAlias.CanonicalName,
                    COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam))
                    COLLATE Latin1_General_100_CI_AI =
                    s.CanonicalHomeTeam COLLATE Latin1_General_100_CI_AI
              AND COALESCE(
                    awayAlias.CanonicalName,
                    COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam))
                    COLLATE Latin1_General_100_CI_AI =
                    s.CanonicalAwayTeam COLLATE Latin1_General_100_CI_AI
        ),
        CandidateMatches AS
        (
            SELECT
                AutomatedCornerBetSelectionId,
                MatchHistoryId,
                ApiFootballFixtureId = MAX(ApiFootballFixtureId),
                LinkPriority = MIN(LinkPriority),
                LinkMethod = CASE MIN(LinkPriority)
                    WHEN 0 THEN CONVERT(NVARCHAR(40), N'MatchHistoryId')
                    WHEN 1 THEN CONVERT(NVARCHAR(40), N'ApiFootballFixtureId')
                    ELSE CONVERT(NVARCHAR(40), N'CanonicalUtcFallback')
                END,
                DateDistanceDays = MIN(DateDistanceDays),
                IsFinal = MAX(IsFinal),
                FixtureStatus = MAX(FixtureStatus),
                SourceUpdatedAtUtc = MAX(SourceUpdatedAtUtc),
                HomeGoals = MAX(HomeGoals),
                AwayGoals = MAX(AwayGoals),
                HomeCorners = MAX(HomeCorners),
                AwayCorners = MAX(AwayCorners),
                HomeShots = MAX(HomeShots),
                AwayShots = MAX(AwayShots),
                HomeShotsOnGoal = MAX(HomeShotsOnGoal),
                AwayShotsOnGoal = MAX(AwayShotsOnGoal)
            FROM CandidateMatchesRaw
            GROUP BY AutomatedCornerBetSelectionId, MatchHistoryId
        ),
        RankedCandidateMatches AS
        (
            SELECT
                candidate.*,
                CandidateRank = DENSE_RANK() OVER
                (
                    PARTITION BY candidate.AutomatedCornerBetSelectionId
                    ORDER BY
                        candidate.IsFinal DESC,
                        candidate.LinkPriority,
                        candidate.DateDistanceDays,
                        CASE WHEN candidate.ApiFootballFixtureId IS NULL THEN 1 ELSE 0 END
                )
            FROM CandidateMatches candidate
        ),
        Matched AS
        (
            SELECT
                AutomatedCornerBetSelectionId,
                MatchCandidateCount = SUM(CASE WHEN CandidateRank = 1 THEN 1 ELSE 0 END),
                MatchHistoryId = MAX(CASE WHEN CandidateRank = 1 THEN MatchHistoryId END),
                ApiFootballFixtureId = MAX(CASE WHEN CandidateRank = 1 THEN ApiFootballFixtureId END),
                LinkMethod = MAX(CASE WHEN CandidateRank = 1 THEN LinkMethod END),
                FixtureStatus = MAX(CASE WHEN CandidateRank = 1 THEN FixtureStatus END),
                SourceUpdatedAtUtc = MAX(CASE WHEN CandidateRank = 1 THEN SourceUpdatedAtUtc END),
                HomeGoals = MAX(CASE WHEN CandidateRank = 1 THEN HomeGoals END),
                AwayGoals = MAX(CASE WHEN CandidateRank = 1 THEN AwayGoals END),
                HomeCorners = MAX(CASE WHEN CandidateRank = 1 THEN HomeCorners END),
                AwayCorners = MAX(CASE WHEN CandidateRank = 1 THEN AwayCorners END),
                HomeShots = MAX(CASE WHEN CandidateRank = 1 THEN HomeShots END),
                AwayShots = MAX(CASE WHEN CandidateRank = 1 THEN AwayShots END),
                HomeShotsOnGoal = MAX(CASE WHEN CandidateRank = 1 THEN HomeShotsOnGoal END),
                AwayShotsOnGoal = MAX(CASE WHEN CandidateRank = 1 THEN AwayShotsOnGoal END)
            FROM RankedCandidateMatches
            GROUP BY AutomatedCornerBetSelectionId
        )
        SELECT
            SelectionId = s.AutomatedCornerBetSelectionId,
            s.MatchDate,
            s.ReconcileExistingSettlement,
            s.ExpectedSettledAtUtc,
            s.MarketType,
            s.SelectedSide,
            s.LineValue,
            s.Odds,
            s.Stake,
            MatchHistoryId = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.MatchHistoryId END,
            ApiFootballFixtureId = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.ApiFootballFixtureId END,
            MatchCandidateCount = COALESCE(matched.MatchCandidateCount, 0),
            LinkMethod = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.LinkMethod END,
            FixtureStatus = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.FixtureStatus END,
            SourceUpdatedAtUtc = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.SourceUpdatedAtUtc END,
            HomeGoals = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.HomeGoals END,
            AwayGoals = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.AwayGoals END,
            HomeCorners = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.HomeCorners END,
            AwayCorners = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.AwayCorners END,
            HomeShots = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.HomeShots END,
            AwayShots = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.AwayShots END,
            HomeShotsOnGoal = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.HomeShotsOnGoal END,
            AwayShotsOnGoal = CASE WHEN matched.MatchCandidateCount = 1 THEN matched.AwayShotsOnGoal END
        FROM PendingSelections s
        LEFT JOIN Matched matched
            ON matched.AutomatedCornerBetSelectionId = s.AutomatedCornerBetSelectionId
        ORDER BY s.MatchDate, s.AutomatedCornerBetSelectionId;
        """;

    private readonly string _connectionString;

    public SqlServerAutomatedBotPickSettlementRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<AutomatedBotPickSettlementCandidate>> GetPendingCandidatesAsync(
        AutomatedBotPickSettlementFilter filter,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<AutomatedBotPickSettlementCandidate>(new CommandDefinition(
            PendingCandidatesSql,
            new
            {
                MatchDateTo = filter.MatchDateTo?.ToDateTime(TimeOnly.MinValue),
                filter.MaxRows,
                filter.BotKey,
                filter.MarketFamily
            },
            commandTimeout: 300,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<AutomatedBotPickSettlementApplyResult> ApplyAsync(
        IReadOnlyCollection<AutomatedBotPickSettlementUpdate> updates,
        CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
        {
            return new AutomatedBotPickSettlementApplyResult(0, 0);
        }

        var parameters = new DynamicParameters();
        parameters.Add("RowsJson", JsonSerializer.Serialize(updates), DbType.String);
        parameters.Add("AppliedRows", dbType: DbType.Int32, direction: ParameterDirection.Output);
        parameters.Add("SettledRows", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "dbo.sp_ApplyAutomatedBotPickSettlements",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 300,
            cancellationToken: cancellationToken));

        return new AutomatedBotPickSettlementApplyResult(
            parameters.Get<int>("AppliedRows"),
            parameters.Get<int>("SettledRows"));
    }
}
