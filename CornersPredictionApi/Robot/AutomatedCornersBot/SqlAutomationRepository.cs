using System.Data;
using System.Text.RegularExpressions;
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
        string? league,
        bool excludeExistingSelections,
        int expectedAutomationVersionCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
        WITH RankedOdds AS
        (
            SELECT
                q.PartidoProximoCuotaId,
                q.Source,
                q.SourceMatchId,
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
            WHERE q.MarketType IN (N'CornersTotal', N'CornersHomeTeam', N'CornersAwayTeam')
              AND q.MatchDate >= @DateFrom
              AND q.MatchDate < @DateToExclusive
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
                  ) < @ExpectedAutomationVersionCount
              )
        )
        SELECT
            PartidoProximoCuotaId,
            Source,
            SourceMatchId,
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
        ORDER BY MatchDate, COALESCE(StandardizedLeague, League), COALESCE(StandardizedHomeTeam, HomeTeam), COALESCE(StandardizedAwayTeam, AwayTeam), LineValue;
        """;

        var rows = new List<UpcomingOddsRecord>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.DateTime2) { Value = dateFrom.ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@DateToExclusive", SqlDbType.DateTime2) { Value = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue) });
        command.Parameters.Add(new SqlParameter("@League", SqlDbType.NVarChar, 200) { Value = (object?)league ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ExcludeExistingSelections", SqlDbType.Bit) { Value = excludeExistingSelections });
        command.Parameters.Add(new SqlParameter("@ExpectedAutomationVersionCount", SqlDbType.Int) { Value = Math.Max(1, expectedAutomationVersionCount) });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new UpcomingOddsRecord
            {
                PartidoProximoCuotaId = reader.GetInt64(reader.GetOrdinal("PartidoProximoCuotaId")),
                Source = reader.GetString(reader.GetOrdinal("Source")),
                SourceMatchId = reader.IsDBNull(reader.GetOrdinal("SourceMatchId")) ? null : reader.GetString(reader.GetOrdinal("SourceMatchId")),
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
        command.Parameters.Add(new SqlParameter("@ContextTotalCorners", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = commandModel.PredictionContext.Comparison.EnrichedPrediction.ToSqlDecimal() });
        command.Parameters.Add(new SqlParameter("@ContextDifference", SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = Math.Abs(commandModel.PredictionContext.Comparison.EnrichedPrediction - commandModel.CornersPrediction.PredictedTotalCorners).ToSqlDecimal() });
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

    public async Task<IReadOnlyList<PersistedAutomatedSelection>> PreviewSettleAsync(
        DateOnly? matchDateTo,
        CancellationToken cancellationToken)
    {
        const string sql = """
        WITH SettledCandidates AS
        (
            SELECT
                s.AutomatedCornerBetSelectionId
            FROM dbo.AutomatedCornerBetSelections s
            INNER JOIN dbo.MatchHistory mh
                ON CAST(s.MatchDate AS DATE) = mh.MatchDate
               AND COALESCE(s.StandardizedLeague, s.League) = COALESCE(mh.StandardizedLeague, mh.League)
               AND COALESCE(s.StandardizedHomeTeam, s.HomeTeam) = COALESCE(mh.StandardizedHomeTeam, mh.HomeTeam)
               AND COALESCE(s.StandardizedAwayTeam, s.AwayTeam) = COALESCE(mh.StandardizedAwayTeam, mh.AwayTeam)
            WHERE s.Status = N'Pending'
              AND (@MatchDateTo IS NULL OR CAST(s.MatchDate AS DATE) <= @MatchDateTo)
        )
        SELECT s.*
        FROM dbo.AutomatedCornerBetSelections s
        INNER JOIN SettledCandidates c
            ON c.AutomatedCornerBetSelectionId = s.AutomatedCornerBetSelectionId
        ORDER BY s.MatchDate, COALESCE(s.StandardizedLeague, s.League), COALESCE(s.StandardizedHomeTeam, s.HomeTeam);
        """;

        var rows = new List<PersistedAutomatedSelection>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Parameters.Add(new SqlParameter("@MatchDateTo", SqlDbType.Date) { Value = (object?)matchDateTo?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapSelection(reader));
        }

        return rows;
    }

    public async Task<int> SettleAsync(DateOnly? matchDateTo, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.sp_SettleAutomatedCornerBetSelections";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@MatchDateTo", SqlDbType.Date) { Value = (object?)matchDateTo?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value });

        var rowsAffected = new SqlParameter("@RowsAffected", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(rowsAffected);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return Convert.ToInt32(rowsAffected.Value);
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
            _ => "TotalCorners"
        };
    }
}
