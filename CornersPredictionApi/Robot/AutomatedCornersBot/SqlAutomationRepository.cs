using System.Data;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CornersPrediction.Application.Automation.BotE;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AutomatedCornersBot.Api;

public sealed class SqlAutomationRepository
{
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private readonly AutomatedBotOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SqlAutomationRepository> _logger;
    private bool _schemaReady;

    public SqlAutomationRepository(
        IOptions<AutomatedBotOptions> options,
        IWebHostEnvironment environment,
        ILogger<SqlAutomationRepository> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            var scriptPath = Path.Combine(_environment.ContentRootPath, "sql", "automated_corners_bot.sql");
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("The SQL bootstrap script was not found.", scriptPath);
            }

            var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            foreach (var batch in SplitSqlBatches(sql))
            {
                if (string.IsNullOrWhiteSpace(batch))
                {
                    continue;
                }

                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 180;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<IReadOnlyList<UpcomingOddsRecord>> GetUpcomingOddsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        DateTime minimumMatchDate,
        string? league,
        bool excludeExistingSelections,
        int expectedAutomationVersionCount,
        bool resolveApiFootballFixtureId,
        CancellationToken cancellationToken)
    {
        const string sql = """
        WITH RankedOdds AS
        (
            SELECT
                q.PartidoProximoCuotaId,
                q.Source,
                q.SourceMatchId,
                fixture.ApiFootballFixtureId,
                q.SourceUrl,
                q.MatchDate,
                q.League,
                q.HomeTeam,
                q.AwayTeam,
                q.StandardizedLeague,
                q.StandardizedHomeTeam,
                q.StandardizedAwayTeam,
                q.HomeTeamGender,
                q.AwayTeamGender,
                q.MarketType,
                q.LineValue,
                q.OverOdds,
                q.UnderOdds,
                q.UpdatedAtUtc,
                MatchIdentity =
                    CASE
                        WHEN NULLIF(LTRIM(RTRIM(q.SourceMatchId)), N'') IS NOT NULL
                            THEN CONCAT(N'ID|', q.Source, N'|', LTRIM(RTRIM(q.SourceMatchId)))
                        WHEN NULLIF(LTRIM(RTRIM(q.SourceUrl)), N'') IS NOT NULL
                            THEN CONCAT(N'URL|', q.Source, N'|', LTRIM(RTRIM(q.SourceUrl)))
                        ELSE CONCAT(
                            N'FALLBACK|',
                            q.Source,
                            N'|',
                            CONVERT(NVARCHAR(19), q.MatchDate, 126),
                            N'|',
                            COALESCE(q.StandardizedLeague, q.League),
                            N'|',
                            COALESCE(q.StandardizedHomeTeam, q.HomeTeam),
                            N'|',
                            COALESCE(q.StandardizedAwayTeam, q.AwayTeam))
                    END,
                rn = ROW_NUMBER() OVER
                (
                    PARTITION BY
                        CASE
                            WHEN NULLIF(LTRIM(RTRIM(q.SourceMatchId)), N'') IS NOT NULL
                                THEN CONCAT(N'ID|', q.Source, N'|', LTRIM(RTRIM(q.SourceMatchId)))
                            WHEN NULLIF(LTRIM(RTRIM(q.SourceUrl)), N'') IS NOT NULL
                                THEN CONCAT(N'URL|', q.Source, N'|', LTRIM(RTRIM(q.SourceUrl)))
                            ELSE CONCAT(
                                N'FALLBACK|',
                                q.Source,
                                N'|',
                                CONVERT(NVARCHAR(19), q.MatchDate, 126),
                                N'|',
                                COALESCE(q.StandardizedLeague, q.League),
                                N'|',
                                COALESCE(q.StandardizedHomeTeam, q.HomeTeam),
                                N'|',
                                COALESCE(q.StandardizedAwayTeam, q.AwayTeam))
                        END,
                        q.MarketType,
                        q.LineValue
                    ORDER BY
                        CASE
                            WHEN NULLIF(LTRIM(RTRIM(q.StandardizedLeague)), N'') IS NOT NULL
                             AND LTRIM(RTRIM(q.StandardizedLeague)) <> LTRIM(RTRIM(q.League))
                                THEN 0
                            ELSE 1
                        END,
                        q.UpdatedAtUtc DESC,
                        q.PartidoProximoCuotaId DESC
                )
            FROM dbo.PartidosProximosCuotas q
            OUTER APPLY
            (
                SELECT ApiFootballFixtureId = CASE
                    WHEN COUNT_BIG(*) = 1 THEN MAX(pp.ExternalFixtureId)
                    ELSE NULL
                END
                FROM dbo.PartidosProximos pp
                WHERE @ResolveApiFootballFixtureId = 1
                  AND pp.ExternalFixtureId IS NOT NULL
                  AND pp.FechaPartido >= CAST(q.MatchDate AS DATE)
                  AND pp.FechaPartido < DATEADD(DAY, 1, CAST(q.MatchDate AS DATE))
                  AND pp.EquipoLocal COLLATE Latin1_General_100_CI_AI =
                      COALESCE(NULLIF(q.StandardizedHomeTeam, N''), q.HomeTeam) COLLATE Latin1_General_100_CI_AI
                  AND pp.EquipoVisita COLLATE Latin1_General_100_CI_AI =
                      COALESCE(NULLIF(q.StandardizedAwayTeam, N''), q.AwayTeam) COLLATE Latin1_General_100_CI_AI
            ) fixture
            WHERE q.MarketType IN (
                    N'CornersTotal', N'CornersHomeTeam', N'CornersAwayTeam',
                    N'GoalsTotal', N'GoalsHomeTeam', N'GoalsAwayTeam',
                    N'ShotsTotal', N'ShotsHomeTeam', N'ShotsAwayTeam',
                    N'ShotsOnTargetTotal', N'ShotsOnTargetHomeTeam', N'ShotsOnTargetAwayTeam')
              AND q.MatchDate >= @DateFrom
              AND q.MatchDate < @DateToExclusive
              AND q.MatchDate > @MinimumMatchDate
              AND (@League IS NULL OR COALESCE(q.StandardizedLeague, q.League) = @League)
              AND ISNULL(q.HomeTeamGender, 'M') <> 'F'
              AND ISNULL(q.AwayTeamGender, 'M') <> 'F'
              AND (q.OverOdds IS NOT NULL OR q.UnderOdds IS NOT NULL)
              AND
              (
                  @ExcludeExistingSelections = 0
                  OR
                  (
                      SELECT COUNT(DISTINCT s.AutomationVersion)
                      FROM dbo.AutomatedCornerBetSelections s
                      WHERE s.Source = q.Source
                        AND s.MarketType = CASE q.MarketType
                            WHEN N'CornersHomeTeam' THEN N'HomeTeamCorners'
                            WHEN N'CornersAwayTeam' THEN N'AwayTeamCorners'
                            WHEN N'GoalsTotal' THEN N'TotalGoals'
                            WHEN N'GoalsHomeTeam' THEN N'HomeTeamGoals'
                            WHEN N'GoalsAwayTeam' THEN N'AwayTeamGoals'
                            WHEN N'ShotsTotal' THEN N'TotalShots'
                            WHEN N'ShotsHomeTeam' THEN N'HomeTeamShots'
                            WHEN N'ShotsAwayTeam' THEN N'AwayTeamShots'
                            WHEN N'ShotsOnTargetTotal' THEN N'TotalShotsOnGoal'
                            WHEN N'ShotsOnTargetHomeTeam' THEN N'HomeTeamShotsOnGoal'
                            WHEN N'ShotsOnTargetAwayTeam' THEN N'AwayTeamShotsOnGoal'
                            ELSE N'TotalCorners'
                        END
                        AND
                        (
                            (
                                NULLIF(LTRIM(RTRIM(q.SourceMatchId)), N'') IS NOT NULL
                                AND s.SourceMatchId = q.SourceMatchId
                            )
                            OR
                            (
                                NULLIF(LTRIM(RTRIM(q.SourceMatchId)), N'') IS NULL
                                AND NULLIF(LTRIM(RTRIM(q.SourceUrl)), N'') IS NOT NULL
                                AND s.SourceUrl = q.SourceUrl
                            )
                            OR
                            (
                                NULLIF(LTRIM(RTRIM(q.SourceMatchId)), N'') IS NULL
                                AND NULLIF(LTRIM(RTRIM(q.SourceUrl)), N'') IS NULL
                                AND s.MatchDate = q.MatchDate
                                AND COALESCE(s.StandardizedLeague, s.League) = COALESCE(q.StandardizedLeague, q.League)
                                AND COALESCE(s.StandardizedHomeTeam, s.HomeTeam) = COALESCE(q.StandardizedHomeTeam, q.HomeTeam)
                                AND COALESCE(s.StandardizedAwayTeam, s.AwayTeam) = COALESCE(q.StandardizedAwayTeam, q.AwayTeam)
                            )
                        )
                  ) < CASE
                        WHEN q.MarketType IN (
                            N'GoalsHomeTeam', N'GoalsAwayTeam',
                            N'ShotsTotal', N'ShotsHomeTeam', N'ShotsAwayTeam',
                            N'ShotsOnTargetHomeTeam', N'ShotsOnTargetAwayTeam') THEN 1
                        ELSE @ExpectedAutomationVersionCount
                      END
              )
        )
        SELECT
            PartidoProximoCuotaId,
            Source,
            SourceMatchId,
            ApiFootballFixtureId,
            SourceUrl,
            MatchDate,
            League,
            HomeTeam,
            AwayTeam,
            StandardizedLeague,
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            HomeTeamGender,
            AwayTeamGender,
            MarketType,
            LineValue,
            OverOdds,
            UnderOdds,
            UpdatedAtUtc
        FROM RankedOdds
        WHERE rn = 1
        ORDER BY MatchDate, COALESCE(StandardizedLeague, League), COALESCE(StandardizedHomeTeam, HomeTeam), COALESCE(StandardizedAwayTeam, AwayTeam), LineValue
        OPTION (RECOMPILE);
        """;

        var rows = new List<UpcomingOddsRecord>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.DateTime2) { Value = dateFrom.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@DateToExclusive", SqlDbType.DateTime2) { Value = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@MinimumMatchDate", SqlDbType.DateTime2) { Value = minimumMatchDate });
        command.Parameters.Add(new SqlParameter("@League", SqlDbType.NVarChar, 200) { Value = (object?)league ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ExcludeExistingSelections", SqlDbType.Bit) { Value = excludeExistingSelections });
        command.Parameters.Add(new SqlParameter("@ExpectedAutomationVersionCount", SqlDbType.Int) { Value = Math.Max(1, expectedAutomationVersionCount) });
        command.Parameters.Add(new SqlParameter("@ResolveApiFootballFixtureId", SqlDbType.Bit) { Value = resolveApiFootballFixtureId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new UpcomingOddsRecord
            {
                PartidoProximoCuotaId = reader.GetInt64(reader.GetOrdinal("PartidoProximoCuotaId")),
                Source = reader.GetString(reader.GetOrdinal("Source")),
                SourceMatchId = reader.IsDBNull(reader.GetOrdinal("SourceMatchId")) ? null : reader.GetString(reader.GetOrdinal("SourceMatchId")),
                ApiFootballFixtureId = reader.IsDBNull(reader.GetOrdinal("ApiFootballFixtureId")) ? null : reader.GetInt64(reader.GetOrdinal("ApiFootballFixtureId")),
                SourceUrl = reader.IsDBNull(reader.GetOrdinal("SourceUrl")) ? null : reader.GetString(reader.GetOrdinal("SourceUrl")),
                MatchDate = reader.GetDateTime(reader.GetOrdinal("MatchDate")),
                League = reader.GetString(reader.GetOrdinal("League")),
                HomeTeam = reader.GetString(reader.GetOrdinal("HomeTeam")),
                AwayTeam = reader.GetString(reader.GetOrdinal("AwayTeam")),
                StandardizedLeague = reader.IsDBNull(reader.GetOrdinal("StandardizedLeague")) ? null : reader.GetString(reader.GetOrdinal("StandardizedLeague")),
                StandardizedHomeTeam = reader.IsDBNull(reader.GetOrdinal("StandardizedHomeTeam")) ? null : reader.GetString(reader.GetOrdinal("StandardizedHomeTeam")),
                StandardizedAwayTeam = reader.IsDBNull(reader.GetOrdinal("StandardizedAwayTeam")) ? null : reader.GetString(reader.GetOrdinal("StandardizedAwayTeam")),
                HomeTeamGender = reader.IsDBNull(reader.GetOrdinal("HomeTeamGender")) ? "M" : reader.GetString(reader.GetOrdinal("HomeTeamGender")),
                AwayTeamGender = reader.IsDBNull(reader.GetOrdinal("AwayTeamGender")) ? "M" : reader.GetString(reader.GetOrdinal("AwayTeamGender")),
                MarketType = reader.GetString(reader.GetOrdinal("MarketType")),
                LineValue = reader.GetDecimal(reader.GetOrdinal("LineValue")),
                OverOdds = reader.IsDBNull(reader.GetOrdinal("OverOdds")) ? null : reader.GetDecimal(reader.GetOrdinal("OverOdds")),
                UnderOdds = reader.IsDBNull(reader.GetOrdinal("UnderOdds")) ? null : reader.GetDecimal(reader.GetOrdinal("UnderOdds")),
                UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc"))
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<BotECalibrationObservation>> GetBotECalibrationHistoryAsync(
        string sourceBotKey,
        DateTime asOfDateUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceBotKey))
        {
            throw new ArgumentException("A source bot key is required.", nameof(sourceBotKey));
        }

        const string sql = """
        WITH EligibleEvaluations AS
        (
            SELECT
                e.AutomatedBotPickEvaluationId,
                e.ApiFootballFixtureId,
                e.PublishedSelectionId,
                e.MatchDate,
                ExpectedUtcDate = CAST(
                    e.MatchDate AT TIME ZONE 'Pacific SA Standard Time' AT TIME ZONE 'UTC'
                    AS DATE),
                e.HomeTeam,
                e.AwayTeam,
                e.MarketType,
                e.SelectedSide,
                e.LineValue,
                e.SelectedOdds,
                SourceProbability = e.FinalProbability,
                e.MarketNoVigProbability,
                e.DataQualityScore,
                e.BaseModelTrainedThroughUtc,
                BaseModelVersion = COALESCE(NULLIF(e.BaseModelVersion, N''), N'unknown')
            FROM dbo.AutomatedBotPickEvaluations e
            WHERE e.BotKey = @SourceBotKey
              AND e.MatchDate < @AsOfDateUtc
              AND e.Decision IN (N'Approved', N'Rejected')
              AND e.SelectedSide IN (N'Over', N'Under')
              AND e.SelectedOdds > 1
              AND e.FinalProbability > 0 AND e.FinalProbability < 1
              AND e.MarketNoVigProbability > 0 AND e.MarketNoVigProbability < 1
              AND e.DataQualityScore BETWEEN 0 AND 1
              AND e.BaseModelTrainedThroughUtc IS NOT NULL
              AND CAST(
                    e.MatchDate AT TIME ZONE 'Pacific SA Standard Time' AT TIME ZONE 'UTC'
                    AS DATETIME2) > e.BaseModelTrainedThroughUtc
        ),
        CandidateMatchesRaw AS
        (
            SELECT
                e.AutomatedBotPickEvaluationId,
                MatchHistoryId = CONVERT(BIGINT, mh.Id),
                mh.ApiFootballFixtureId,
                LinkPriority = 0,
                DateDistanceDays = ABS(DATEDIFF(DAY, e.ExpectedUtcDate, mh.MatchDate))
            FROM EligibleEvaluations e
            INNER JOIN dbo.AutomatedCornerBetSelections s
                ON s.AutomatedCornerBetSelectionId = e.PublishedSelectionId
            INNER JOIN dbo.MatchHistory mh
                ON mh.Id = s.MatchHistoryId
            WHERE e.PublishedSelectionId IS NOT NULL
              AND s.MatchHistoryId IS NOT NULL

            UNION ALL

            SELECT
                e.AutomatedBotPickEvaluationId,
                MatchHistoryId = CONVERT(BIGINT, mh.Id),
                mh.ApiFootballFixtureId,
                LinkPriority = 1,
                DateDistanceDays = ABS(DATEDIFF(DAY, e.ExpectedUtcDate, mh.MatchDate))
            FROM EligibleEvaluations e
            INNER JOIN dbo.MatchHistory mh
                ON mh.ApiFootballFixtureId = e.ApiFootballFixtureId
            WHERE e.ApiFootballFixtureId IS NOT NULL

            UNION ALL

            SELECT
                e.AutomatedBotPickEvaluationId,
                MatchHistoryId = CONVERT(BIGINT, mh.Id),
                mh.ApiFootballFixtureId,
                LinkPriority = 2,
                DateDistanceDays = ABS(DATEDIFF(DAY, e.ExpectedUtcDate, mh.MatchDate))
            FROM EligibleEvaluations e
            INNER JOIN dbo.MatchHistory mh
                ON mh.MatchDate BETWEEN DATEADD(DAY, -1, e.ExpectedUtcDate)
                                    AND DATEADD(DAY, 1, e.ExpectedUtcDate)
               AND COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam)
                    COLLATE Latin1_General_100_CI_AI = e.HomeTeam COLLATE Latin1_General_100_CI_AI
               AND COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam)
                    COLLATE Latin1_General_100_CI_AI = e.AwayTeam COLLATE Latin1_General_100_CI_AI
            WHERE e.PublishedSelectionId IS NULL
              AND e.ApiFootballFixtureId IS NULL
        ),
        CandidateMatches AS
        (
            SELECT
                AutomatedBotPickEvaluationId,
                MatchHistoryId,
                ApiFootballFixtureId = MAX(ApiFootballFixtureId),
                LinkPriority = MIN(LinkPriority),
                DateDistanceDays = MIN(DateDistanceDays)
            FROM CandidateMatchesRaw
            GROUP BY AutomatedBotPickEvaluationId, MatchHistoryId
        ),
        RankedCandidateMatches AS
        (
            SELECT
                candidate.*,
                CandidateRank = DENSE_RANK() OVER
                (
                    PARTITION BY candidate.AutomatedBotPickEvaluationId
                    ORDER BY candidate.LinkPriority, candidate.DateDistanceDays,
                        CASE WHEN candidate.ApiFootballFixtureId IS NULL THEN 1 ELSE 0 END
                )
            FROM CandidateMatches candidate
        ),
        MatchedEvaluations AS
        (
            SELECT
                AutomatedBotPickEvaluationId,
                MatchCandidateCount = SUM(CASE WHEN CandidateRank = 1 THEN 1 ELSE 0 END),
                MatchHistoryId = MAX(CASE WHEN CandidateRank = 1 THEN MatchHistoryId END)
            FROM RankedCandidateMatches
            GROUP BY AutomatedBotPickEvaluationId
        )
        SELECT
            EvaluationId = e.AutomatedBotPickEvaluationId,
            FixtureId = mh.ApiFootballFixtureId,
            MatchDateUtc = CAST(
                e.MatchDate AT TIME ZONE 'Pacific SA Standard Time' AT TIME ZONE 'UTC'
                AS DATETIME2),
            e.MarketType,
            e.SelectedSide,
            LineValue = e.LineValue,
            Odds = e.SelectedOdds,
            ActualValue = CONVERT(INT, CASE e.MarketType
                WHEN N'TotalGoals' THEN mh.HomeGoals + mh.AwayGoals
                WHEN N'HomeTeamGoals' THEN mh.HomeGoals
                WHEN N'AwayTeamGoals' THEN mh.AwayGoals
                WHEN N'TotalCorners' THEN mh.HomeCorners + mh.AwayCorners
                WHEN N'HomeTeamCorners' THEN mh.HomeCorners
                WHEN N'AwayTeamCorners' THEN mh.AwayCorners
                WHEN N'TotalShots' THEN mh.HomeShots + mh.AwayShots
                WHEN N'HomeTeamShots' THEN mh.HomeShots
                WHEN N'AwayTeamShots' THEN mh.AwayShots
                WHEN N'TotalShotsOnGoal' THEN mh.HomeShotsOnGoal + mh.AwayShotsOnGoal
                WHEN N'HomeTeamShotsOnGoal' THEN mh.HomeShotsOnGoal
                WHEN N'AwayTeamShotsOnGoal' THEN mh.AwayShotsOnGoal
            END),
            e.SourceProbability,
            e.MarketNoVigProbability,
            e.DataQualityScore,
            e.BaseModelVersion
        FROM EligibleEvaluations e
        INNER JOIN MatchedEvaluations matched
            ON matched.AutomatedBotPickEvaluationId = e.AutomatedBotPickEvaluationId
           AND matched.MatchCandidateCount = 1
        INNER JOIN dbo.MatchHistory mh
            ON mh.Id = matched.MatchHistoryId
        WHERE mh.ApiFootballFixtureId IS NOT NULL
          AND UPPER(LTRIM(RTRIM(COALESCE(mh.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
          AND
          (
              (e.MarketType IN (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals')
                  AND ISNULL(mh.ApiFootballGoalsAvailable, 0) = 1)
              OR (e.MarketType IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')
                  AND ISNULL(mh.ApiFootballCornersAvailable, 0) = 1)
              OR (e.MarketType IN (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots')
                  AND ISNULL(mh.ApiFootballShotsAvailable, 0) = 1)
              OR (e.MarketType IN (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal')
                  AND ISNULL(mh.ApiFootballShotsOnGoalAvailable, 0) = 1)
          )
          AND CASE e.MarketType
                WHEN N'TotalGoals' THEN mh.HomeGoals + mh.AwayGoals
                WHEN N'HomeTeamGoals' THEN mh.HomeGoals
                WHEN N'AwayTeamGoals' THEN mh.AwayGoals
                WHEN N'TotalCorners' THEN mh.HomeCorners + mh.AwayCorners
                WHEN N'HomeTeamCorners' THEN mh.HomeCorners
                WHEN N'AwayTeamCorners' THEN mh.AwayCorners
                WHEN N'TotalShots' THEN mh.HomeShots + mh.AwayShots
                WHEN N'HomeTeamShots' THEN mh.HomeShots
                WHEN N'AwayTeamShots' THEN mh.AwayShots
                WHEN N'TotalShotsOnGoal' THEN mh.HomeShotsOnGoal + mh.AwayShotsOnGoal
                WHEN N'HomeTeamShotsOnGoal' THEN mh.HomeShotsOnGoal
                WHEN N'AwayTeamShotsOnGoal' THEN mh.AwayShotsOnGoal
              END IS NOT NULL
        ORDER BY e.MatchDate, e.AutomatedBotPickEvaluationId
        OPTION (RECOMPILE);
        """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 180;
        command.Parameters.Add(new SqlParameter("@SourceBotKey", SqlDbType.NVarChar, 50)
        {
            Value = sourceBotKey.Trim().ToUpperInvariant()
        });
        command.Parameters.Add(new SqlParameter("@AsOfDateUtc", SqlDbType.DateTime2)
        {
            Value = asOfDateUtc
        });

        var observations = new List<BotECalibrationObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            observations.Add(new BotECalibrationObservation(
                reader.GetInt64(reader.GetOrdinal("EvaluationId")),
                reader.GetInt64(reader.GetOrdinal("FixtureId")),
                reader.GetDateTime(reader.GetOrdinal("MatchDateUtc")),
                reader.GetString(reader.GetOrdinal("MarketType")),
                reader.GetString(reader.GetOrdinal("SelectedSide")),
                reader.GetDecimal(reader.GetOrdinal("LineValue")),
                reader.GetDecimal(reader.GetOrdinal("Odds")),
                reader.GetInt32(reader.GetOrdinal("ActualValue")),
                Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("SourceProbability"))),
                Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("MarketNoVigProbability"))),
                Convert.ToDouble(reader.GetDecimal(reader.GetOrdinal("DataQualityScore"))),
                reader.GetString(reader.GetOrdinal("BaseModelVersion"))));
        }

        return observations;
    }

    public async Task<UpsertSelectionResult> UpsertSelectionAsync(
        PersistSelectionCommand commandModel,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.sp_UpsertAutomatedCornerBetSelection";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add(new SqlParameter("@RunId", SqlDbType.UniqueIdentifier) { Value = commandModel.RunId });
        command.Parameters.Add(new SqlParameter("@AutomationVersion", SqlDbType.NVarChar, 50) { Value = commandModel.AutomationVersion });
        command.Parameters.Add(new SqlParameter("@Source", SqlDbType.NVarChar, 50) { Value = commandModel.Odds.Source });
        command.Parameters.Add(new SqlParameter("@SourceMatchId", SqlDbType.NVarChar, 100) { Value = (object?)commandModel.Odds.SourceMatchId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ApiFootballFixtureId", SqlDbType.BigInt) { Value = (object?)commandModel.Odds.ApiFootballFixtureId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@SourceUrl", SqlDbType.NVarChar, 500) { Value = (object?)commandModel.Odds.SourceUrl ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@MatchDate", SqlDbType.DateTime2) { Value = commandModel.Odds.MatchDate });
        command.Parameters.Add(new SqlParameter("@League", SqlDbType.NVarChar, 200) { Value = commandModel.Odds.League });
        command.Parameters.Add(new SqlParameter("@StandardizedLeague", SqlDbType.NVarChar, 200) { Value = (object?)commandModel.Odds.StandardizedLeague ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@HomeTeam", SqlDbType.NVarChar, 150) { Value = commandModel.Odds.HomeTeam });
        command.Parameters.Add(new SqlParameter("@AwayTeam", SqlDbType.NVarChar, 150) { Value = commandModel.Odds.AwayTeam });
        command.Parameters.Add(new SqlParameter("@StandardizedHomeTeam", SqlDbType.NVarChar, 150) { Value = (object?)commandModel.Odds.StandardizedHomeTeam ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@StandardizedAwayTeam", SqlDbType.NVarChar, 150) { Value = (object?)commandModel.Odds.StandardizedAwayTeam ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@HomeTeamGender", SqlDbType.Char, 1) { Value = commandModel.Odds.HomeTeamGender });
        command.Parameters.Add(new SqlParameter("@AwayTeamGender", SqlDbType.Char, 1) { Value = commandModel.Odds.AwayTeamGender });
        command.Parameters.Add(new SqlParameter("@SourceMarketType", SqlDbType.NVarChar, 50) { Value = commandModel.Odds.MarketType });
        command.Parameters.Add(new SqlParameter("@MarketType", SqlDbType.NVarChar, 50) { Value = MapSelectionMarketType(commandModel.Odds.MarketType) });
        command.Parameters.Add(new SqlParameter("@LineValue", SqlDbType.Decimal) { Precision = 6, Scale = 2, Value = commandModel.Odds.LineValue });
        command.Parameters.Add(new SqlParameter("@SelectedSide", SqlDbType.NVarChar, 10) { Value = commandModel.SelectedSide });
        command.Parameters.Add(new SqlParameter("@Odds", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = commandModel.SelectedOdds });
        command.Parameters.Add(new SqlParameter("@Stake", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = commandModel.Stake });
        command.Parameters.Add(new SqlParameter("@FlatStake", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = commandModel.Stake });
        command.Parameters.Add(new SqlParameter("@ImpliedProbability", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = commandModel.ImpliedProbability.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@ModelProbability", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = commandModel.ModelProbability.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@ProbabilityEdge", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = commandModel.ProbabilityEdge.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@ExpectedValue", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = commandModel.ExpectedValue.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@KellyFraction", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = commandModel.KellyFraction.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@SelectionScore", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = commandModel.SelectionScore.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@PredictedTotalCorners", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = commandModel.CornersPrediction.PredictedTotalCorners.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@PredTotalDirect", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = (object?)commandModel.CornersPrediction.PredTotalDirect?.ToSqlDecimal() ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@PredHomeCorners", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = (object?)commandModel.CornersPrediction.PredHomeCorners?.ToSqlDecimal() ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@PredAwayCorners", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = (object?)commandModel.CornersPrediction.PredAwayCorners?.ToSqlDecimal() ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@PredTotalCombined", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = (object?)commandModel.CornersPrediction.PredTotalCombined?.ToSqlDecimal() ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@DistanceToLine", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = (commandModel.CornersPrediction.DistanceToLine ?? 0).ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@ConfidenceLevel", SqlDbType.NVarChar, 20) { Value = (object?)commandModel.CornersPrediction.Confidence ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@OverUnderConfidenceLevel", SqlDbType.NVarChar, 20) { Value = (object?)commandModel.OverUnderPrediction?.Confidence ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ModelConsensus", SqlDbType.NVarChar, 20) { Value = (object?)commandModel.CornersPrediction.ModelConsensus ?? DBNull.Value });
        var contextPrediction = ResolveContextPrediction(commandModel.Odds.MarketType, commandModel.PredictionContext);
        command.Parameters.Add(new SqlParameter("@ContextTotalCorners", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = contextPrediction.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@ContextDifference", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = Math.Abs(contextPrediction - commandModel.CornersPrediction.PredictedTotalCorners).ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@RecommendedSide", SqlDbType.NVarChar, 10) { Value = (object?)commandModel.CornersPrediction.RecommendedSide ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@DecisionReason", SqlDbType.NVarChar) { Value = commandModel.DecisionReason });

        var idParameter = new SqlParameter("@AutomatedCornerBetSelectionId", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(idParameter);

        var mergeActionParameter = new SqlParameter("@MergeAction", SqlDbType.NVarChar, 10)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(mergeActionParameter);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return new UpsertSelectionResult(
            SelectionId: Convert.ToInt64(idParameter.Value),
            MergeAction: Convert.ToString(mergeActionParameter.Value) ?? "UNKNOWN");
    }

    public async Task UpsertBotCEvaluationAsync(
        PersistBotCEvaluationCommand model,
        CancellationToken cancellationToken)
    {
        var decision = model.Decision;
        var evidenceFingerprint = decision.ConfigurationVersion.StartsWith(
            "bot-e-",
            StringComparison.OrdinalIgnoreCase)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(decision.FeatureSnapshotJson)))
            : string.Empty;
        var idempotencyPayload = string.Join(
            "|",
            model.BotKey.Trim().ToUpperInvariant(),
            model.Odds.PartidoProximoCuotaId,
            model.Odds.MarketType.Trim().ToUpperInvariant(),
            model.Odds.LineValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            model.BaseModelVersion,
            decision.ConfigurationVersion,
            evidenceFingerprint);
        var idempotencyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyPayload)));

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.sp_UpsertAutomatedBotPickEvaluation";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = 120;
        Add(command, "@IdempotencyKey", SqlDbType.Char, idempotencyKey, 64);
        Add(command, "@RunId", SqlDbType.UniqueIdentifier, model.RunId);
        Add(command, "@BotKey", SqlDbType.NVarChar, model.BotKey, 50);
        Add(command, "@AutomationVersion", SqlDbType.NVarChar, model.AutomationVersion, 50);
        Add(command, "@PartidoProximoCuotaId", SqlDbType.BigInt, model.Odds.PartidoProximoCuotaId);
        Add(command, "@ApiFootballFixtureId", SqlDbType.BigInt, model.Odds.ApiFootballFixtureId);
        Add(command, "@MatchDate", SqlDbType.DateTime2, model.Odds.MatchDate);
        Add(command, "@League", SqlDbType.NVarChar, model.Odds.EffectiveLeague, 200);
        Add(command, "@HomeTeam", SqlDbType.NVarChar, model.Odds.EffectiveHomeTeam, 150);
        Add(command, "@AwayTeam", SqlDbType.NVarChar, model.Odds.EffectiveAwayTeam, 150);
        Add(command, "@Source", SqlDbType.NVarChar, model.Odds.Source, 50);
        Add(command, "@SourceMarketType", SqlDbType.NVarChar, model.Odds.MarketType, 50);
        Add(command, "@MarketType", SqlDbType.NVarChar, model.MarketType, 50);
        AddDecimal(command, "@LineValue", model.Odds.LineValue, 6, 2);
        Add(command, "@SelectedSide", SqlDbType.NVarChar, EmptyToNull(decision.SelectedSide), 10);
        AddDecimal(command, "@SelectedOdds", decision.SelectedOdds, 10, 2);
        Add(command, "@DecisionEngineType", SqlDbType.NVarChar, decision.DecisionEngineType, 40);
        Add(command, "@Decision", SqlDbType.NVarChar, decision.Decision, 20);
        Add(command, "@BaseModelName", SqlDbType.NVarChar, model.BaseModelName, 120);
        Add(command, "@BaseModelVersion", SqlDbType.NVarChar, model.BaseModelVersion, 120);
        Add(command, "@BaseModelTrainedThroughUtc", SqlDbType.DateTime2, model.BaseModelTrainedThroughUtc);
        Add(command, "@FeatureSchemaVersion", SqlDbType.NVarChar, decision.FeatureSchemaVersion, 80);
        Add(command, "@ConfigurationVersion", SqlDbType.NVarChar, decision.ConfigurationVersion, 80);
        AddDecimal(command, "@BaseRawProbability", decision.BaseRawProbability, 9, 6);
        AddDecimal(command, "@BaseCalibratedProbability", decision.BaseCalibratedProbability, 9, 6);
        AddDecimal(command, "@RawImpliedProbability", decision.RawImpliedProbability, 9, 6);
        AddDecimal(command, "@MarketNoVigProbability", decision.MarketNoVigProbability, 9, 6);
        AddDecimal(command, "@FinalProbability", decision.FinalProbability, 9, 6);
        AddDecimal(command, "@FinalEdge", decision.FinalEdge, 9, 6);
        AddDecimal(command, "@FinalExpectedValue", decision.FinalExpectedValue, 9, 6);
        AddDecimal(command, "@RuleBasedConfidenceScore", decision.RuleBasedConfidenceScore, 9, 6);
        AddDecimal(command, "@ContextExpectedValue", decision.ContextExpectedValue, 12, 4);
        AddDecimal(command, "@ContextAgreementScore", decision.ContextAgreementScore, 9, 6);
        AddDecimal(command, "@DataQualityScore", decision.DataQualityScore, 9, 6);
        Add(command, "@DecisionReasonsJson", SqlDbType.NVarChar, JsonSerializer.Serialize(decision.DecisionReasons));
        Add(command, "@RiskFlagsJson", SqlDbType.NVarChar, JsonSerializer.Serialize(decision.RiskFlags));
        Add(command, "@Explanation", SqlDbType.NVarChar, decision.Summary, 1000);
        Add(command, "@FeatureSnapshotJson", SqlDbType.NVarChar, decision.FeatureSnapshotJson);
        Add(command, "@PublishedSelectionId", SqlDbType.BigInt, model.PublishedSelectionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(SqlCommand command, string name, SqlDbType type, object? value, int size = 0)
    {
        var parameter = size > 0 ? new SqlParameter(name, type, size) : new SqlParameter(name, type);
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static void AddDecimal(SqlCommand command, string name, double? value, byte precision, byte scale) =>
        AddDecimal(command, name, value is null ? null : Convert.ToDecimal(value.Value), precision, scale);

    private static void AddDecimal(SqlCommand command, string name, decimal? value, byte precision, byte scale)
    {
        var parameter = new SqlParameter(name, SqlDbType.Decimal) { Precision = precision, Scale = scale, Value = value ?? (object)DBNull.Value };
        command.Parameters.Add(parameter);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public async Task<IReadOnlyList<PersistedAutomatedSelection>> GetSelectionsAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.sp_GetAutomatedCornerBetSelections";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.Date) { Value = (object?)dateFrom?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@DateTo", SqlDbType.Date) { Value = (object?)dateTo?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 20) { Value = (object?)status ?? DBNull.Value });

        var rows = new List<PersistedAutomatedSelection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapSelection(reader));
        }

        return rows;
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_options.ResolveSqlConnectionString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql)
    {
        var goSplit = Regex.Split(sql, @"(?im)^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline)
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

        if (goSplit.Length > 1)
        {
            return goSplit;
        }

        var matches = Regex.Matches(
            sql,
            @"(?im)^\s*CREATE\s+OR\s+ALTER\s+PROCEDURE\s+");

        if (matches.Count == 0)
        {
            return new[] { sql };
        }

        var batches = new List<string>();
        var firstProcedureIndex = matches[0].Index;
        if (firstProcedureIndex > 0)
        {
            batches.Add(sql[..firstProcedureIndex]);
        }

        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : sql.Length;
            batches.Add(sql[start..end]);
        }

        return batches;
    }

    private static PersistedAutomatedSelection MapSelection(SqlDataReader reader)
    {
        return new PersistedAutomatedSelection
        {
            AutomatedCornerBetSelectionId = reader.GetInt64(reader.GetOrdinal("AutomatedCornerBetSelectionId")),
            RunId = reader.GetGuid(reader.GetOrdinal("RunId")),
            AutomationVersion = reader.GetString(reader.GetOrdinal("AutomationVersion")),
            Source = reader.GetString(reader.GetOrdinal("Source")),
            SourceMatchId = reader.IsDBNull(reader.GetOrdinal("SourceMatchId")) ? null : reader.GetString(reader.GetOrdinal("SourceMatchId")),
            SourceUrl = reader.IsDBNull(reader.GetOrdinal("SourceUrl")) ? null : reader.GetString(reader.GetOrdinal("SourceUrl")),
            MatchDate = reader.GetDateTime(reader.GetOrdinal("MatchDate")),
            League = reader.GetString(reader.GetOrdinal("League")),
            StandardizedLeague = reader.IsDBNull(reader.GetOrdinal("StandardizedLeague")) ? null : reader.GetString(reader.GetOrdinal("StandardizedLeague")),
            HomeTeam = reader.GetString(reader.GetOrdinal("HomeTeam")),
            AwayTeam = reader.GetString(reader.GetOrdinal("AwayTeam")),
            StandardizedHomeTeam = reader.IsDBNull(reader.GetOrdinal("StandardizedHomeTeam")) ? null : reader.GetString(reader.GetOrdinal("StandardizedHomeTeam")),
            StandardizedAwayTeam = reader.IsDBNull(reader.GetOrdinal("StandardizedAwayTeam")) ? null : reader.GetString(reader.GetOrdinal("StandardizedAwayTeam")),
            HomeTeamGender = reader.GetString(reader.GetOrdinal("HomeTeamGender")),
            AwayTeamGender = reader.GetString(reader.GetOrdinal("AwayTeamGender")),
            SourceMarketType = reader.GetString(reader.GetOrdinal("SourceMarketType")),
            MarketType = reader.GetString(reader.GetOrdinal("MarketType")),
            LineValue = reader.GetDecimal(reader.GetOrdinal("LineValue")),
            SelectedSide = reader.GetString(reader.GetOrdinal("SelectedSide")),
            Odds = reader.GetDecimal(reader.GetOrdinal("Odds")),
            Stake = reader.GetDecimal(reader.GetOrdinal("Stake")),
            FlatStake = reader.IsDBNull(reader.GetOrdinal("FlatStake")) ? null : reader.GetDecimal(reader.GetOrdinal("FlatStake")),
            ImpliedProbability = reader.IsDBNull(reader.GetOrdinal("ImpliedProbability")) ? null : reader.GetDecimal(reader.GetOrdinal("ImpliedProbability")),
            ModelProbability = reader.IsDBNull(reader.GetOrdinal("ModelProbability")) ? null : reader.GetDecimal(reader.GetOrdinal("ModelProbability")),
            ProbabilityEdge = reader.IsDBNull(reader.GetOrdinal("ProbabilityEdge")) ? null : reader.GetDecimal(reader.GetOrdinal("ProbabilityEdge")),
            ExpectedValue = reader.IsDBNull(reader.GetOrdinal("ExpectedValue")) ? null : reader.GetDecimal(reader.GetOrdinal("ExpectedValue")),
            KellyFraction = reader.IsDBNull(reader.GetOrdinal("KellyFraction")) ? null : reader.GetDecimal(reader.GetOrdinal("KellyFraction")),
            SelectionScore = reader.IsDBNull(reader.GetOrdinal("SelectionScore")) ? null : reader.GetDecimal(reader.GetOrdinal("SelectionScore")),
            PredictedTotalCorners = reader.IsDBNull(reader.GetOrdinal("PredictedTotalCorners")) ? null : reader.GetDecimal(reader.GetOrdinal("PredictedTotalCorners")),
            PredTotalDirect = reader.IsDBNull(reader.GetOrdinal("PredTotalDirect")) ? null : reader.GetDecimal(reader.GetOrdinal("PredTotalDirect")),
            PredHomeCorners = reader.IsDBNull(reader.GetOrdinal("PredHomeCorners")) ? null : reader.GetDecimal(reader.GetOrdinal("PredHomeCorners")),
            PredAwayCorners = reader.IsDBNull(reader.GetOrdinal("PredAwayCorners")) ? null : reader.GetDecimal(reader.GetOrdinal("PredAwayCorners")),
            PredTotalCombined = reader.IsDBNull(reader.GetOrdinal("PredTotalCombined")) ? null : reader.GetDecimal(reader.GetOrdinal("PredTotalCombined")),
            DistanceToLine = reader.IsDBNull(reader.GetOrdinal("DistanceToLine")) ? null : reader.GetDecimal(reader.GetOrdinal("DistanceToLine")),
            ConfidenceLevel = reader.IsDBNull(reader.GetOrdinal("ConfidenceLevel")) ? null : reader.GetString(reader.GetOrdinal("ConfidenceLevel")),
            OverUnderConfidenceLevel = reader.IsDBNull(reader.GetOrdinal("OverUnderConfidenceLevel")) ? null : reader.GetString(reader.GetOrdinal("OverUnderConfidenceLevel")),
            ModelConsensus = reader.IsDBNull(reader.GetOrdinal("ModelConsensus")) ? null : reader.GetString(reader.GetOrdinal("ModelConsensus")),
            ContextTotalCorners = reader.IsDBNull(reader.GetOrdinal("ContextTotalCorners")) ? null : reader.GetDecimal(reader.GetOrdinal("ContextTotalCorners")),
            ContextDifference = reader.IsDBNull(reader.GetOrdinal("ContextDifference")) ? null : reader.GetDecimal(reader.GetOrdinal("ContextDifference")),
            RecommendedSide = reader.IsDBNull(reader.GetOrdinal("RecommendedSide")) ? null : reader.GetString(reader.GetOrdinal("RecommendedSide")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            ActualHomeCorners = reader.IsDBNull(reader.GetOrdinal("ActualHomeCorners")) ? null : reader.GetInt32(reader.GetOrdinal("ActualHomeCorners")),
            ActualAwayCorners = reader.IsDBNull(reader.GetOrdinal("ActualAwayCorners")) ? null : reader.GetInt32(reader.GetOrdinal("ActualAwayCorners")),
            ActualTotalCorners = reader.IsDBNull(reader.GetOrdinal("ActualTotalCorners")) ? null : reader.GetInt32(reader.GetOrdinal("ActualTotalCorners")),
            ProfitLoss = reader.IsDBNull(reader.GetOrdinal("ProfitLoss")) ? null : reader.GetDecimal(reader.GetOrdinal("ProfitLoss")),
            YieldPct = reader.IsDBNull(reader.GetOrdinal("YieldPct")) ? null : reader.GetDecimal(reader.GetOrdinal("YieldPct")),
            DecisionReason = reader.IsDBNull(reader.GetOrdinal("DecisionReason")) ? null : reader.GetString(reader.GetOrdinal("DecisionReason")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("CreatedAtUtc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc")),
            SettledAtUtc = reader.IsDBNull(reader.GetOrdinal("SettledAtUtc")) ? null : reader.GetDateTime(reader.GetOrdinal("SettledAtUtc"))
        };
    }

    private static string MapSelectionMarketType(string sourceMarketType)
    {
        return sourceMarketType switch
        {
            "CornersHomeTeam" => "HomeTeamCorners",
            "CornersAwayTeam" => "AwayTeamCorners",
            "GoalsTotal" => "TotalGoals",
            "GoalsHomeTeam" => "HomeTeamGoals",
            "GoalsAwayTeam" => "AwayTeamGoals",
            "ShotsTotal" => "TotalShots",
            "ShotsHomeTeam" => "HomeTeamShots",
            "ShotsAwayTeam" => "AwayTeamShots",
            "ShotsOnTargetTotal" => "TotalShotsOnGoal",
            "ShotsOnTargetHomeTeam" => "HomeTeamShotsOnGoal",
            "ShotsOnTargetAwayTeam" => "AwayTeamShotsOnGoal",
            _ => "TotalCorners"
        };
    }

    private static double ResolveContextPrediction(string sourceMarketType, PredictionContextDto context)
    {
        return sourceMarketType switch
        {
            "GoalsTotal" => context.Comparison.EnrichedGoalsPrediction,
            "GoalsHomeTeam" => context.Comparison.HomeExpectedGoals,
            "GoalsAwayTeam" => context.Comparison.AwayExpectedGoals,
            "ShotsTotal" => context.Comparison.EnrichedShotsPrediction,
            "ShotsHomeTeam" => context.Comparison.HomeExpectedShots,
            "ShotsAwayTeam" => context.Comparison.AwayExpectedShots,
            "ShotsOnTargetTotal" => context.Comparison.EnrichedShotsOnGoalPrediction,
            "ShotsOnTargetHomeTeam" => context.Comparison.HomeExpectedShotsOnGoal,
            "ShotsOnTargetAwayTeam" => context.Comparison.AwayExpectedShotsOnGoal,
            _ => context.Comparison.EnrichedPrediction
        };
    }
}
