using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

/// <summary>
/// Read-only access to the complete C/D/E/F/H candidate audit. This adapter has
/// no method that can publish, update or settle a candidate.
/// </summary>
public sealed partial class SqlServerAutomatedBotResearchRepository
    : IAutomatedBotResearchRepository
{
    private readonly string _connectionString;

    public SqlServerAutomatedBotResearchRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<AutomatedBotResearchPage> GetEvaluationsAsync(
        AutomatedBotResearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new
        {
            GeneralPicks = false,
            query.DateFromUtc,
            query.DateToExclusiveUtc,
            query.MarketFamily,
            query.MarketType,
            query.BotKey,
            query.ModelDecision,
            query.PublicationStatus,
            Offset = checked((long)(query.Page - 1) * query.PageSize),
            query.PageSize
        };

        await using var connection = new SqlConnection(_connectionString);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            ResearchSql,
            parameters,
            commandTimeout: 120,
            cancellationToken: cancellationToken));

        var totalCount = await results.ReadSingleAsync<long>();
        var rows = (await results.ReadAsync<ResearchRow>()).AsList();
        var items = rows.Select(Map).ToArray();
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Min(
                int.MaxValue,
                (totalCount + query.PageSize - 1L) / query.PageSize);

        return new AutomatedBotResearchPage(
            items,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages);
    }

    private static AutomatedBotResearchEvaluationDto Map(ResearchRow row) => new()
    {
        EvaluationId = row.EvaluationId,
        RunId = row.RunId,
        BotKey = row.BotKey,
        AutomationVersion = row.AutomationVersion,
        MatchDate = AsUtc(row.MatchDate),
        League = row.League,
        HomeTeam = row.HomeTeam,
        AwayTeam = row.AwayTeam,
        Source = row.Source,
        MarketFamily = row.MarketFamily,
        MarketType = row.MarketType,
        LineValue = row.LineValue,
        SelectedSide = row.SelectedSide,
        SelectedOdds = row.SelectedOdds,
        ModelDecision = row.ModelDecision,
        ModelDecisionReasons = GetAutomatedBotResearchEvaluationsUseCase
            .ParseDecisionReasons(row.ModelDecisionReasonsJson),
        ModelExplanation = row.ModelExplanation,
        PublicationStatus = row.PublicationStatus,
        ProductionDecision = row.ProductionDecision,
        ProductionReason = row.ProductionReason,
        IsResearchWinner = row.IsResearchWinner,
        PublishedSelectionId = row.PublishedSelectionId,
        FinalProbability = row.FinalProbability,
        MarketProbability = row.MarketProbability,
        FinalEdge = row.FinalEdge,
        FinalExpectedValue = row.FinalExpectedValue,
        SelectionScore = row.SelectionScore,
        DataQualityScore = row.DataQualityScore,
        ContextAgreementScore = row.ContextAgreementScore,
        ConfigurationVersion = row.ConfigurationVersion,
        FeatureSchemaVersion = row.FeatureSchemaVersion,
        FeatureSnapshotJson = string.IsNullOrWhiteSpace(row.FeatureSnapshotJson)
            ? "{}"
            : row.FeatureSnapshotJson,
        OutcomeStatus = row.OutcomeStatus,
        OutcomeSource = row.OutcomeSource,
        ManualSettlementReason = row.ManualSettlementReason,
        ManualSettledBy = row.ManualSettledBy,
        ActualValue = row.ActualValue,
        ProfitLoss = row.ProfitLoss,
        OutcomeAvailableUtc = row.OutcomeAvailableUtc.HasValue
            ? AsUtc(row.OutcomeAvailableUtc.Value)
            : null,
        EvaluatedAtUtc = AsUtc(row.EvaluatedAtUtc)
    };

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class ResearchRow
    {
        public long EvaluationId { get; init; }
        public Guid RunId { get; init; }
        public string BotKey { get; init; } = string.Empty;
        public string AutomationVersion { get; init; } = string.Empty;
        public DateTime MatchDate { get; init; }
        public string League { get; init; } = string.Empty;
        public string HomeTeam { get; init; } = string.Empty;
        public string AwayTeam { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string MarketFamily { get; init; } = string.Empty;
        public string MarketType { get; init; } = string.Empty;
        public decimal LineValue { get; init; }
        public string? SelectedSide { get; init; }
        public decimal? SelectedOdds { get; init; }
        public string ModelDecision { get; init; } = string.Empty;
        public string? ModelDecisionReasonsJson { get; init; }
        public string? ModelExplanation { get; init; }
        public string PublicationStatus { get; init; } = string.Empty;
        public string ProductionDecision { get; init; } = string.Empty;
        public string? ProductionReason { get; init; }
        public bool IsResearchWinner { get; init; }
        public long? PublishedSelectionId { get; init; }
        public decimal? FinalProbability { get; init; }
        public decimal? MarketProbability { get; init; }
        public decimal? FinalEdge { get; init; }
        public decimal? FinalExpectedValue { get; init; }
        public decimal? SelectionScore { get; init; }
        public decimal? DataQualityScore { get; init; }
        public decimal? ContextAgreementScore { get; init; }
        public string ConfigurationVersion { get; init; } = string.Empty;
        public string FeatureSchemaVersion { get; init; } = string.Empty;
        public string? FeatureSnapshotJson { get; init; }
        public string OutcomeStatus { get; init; } = string.Empty;
        public string? OutcomeSource { get; init; }
        public string? ManualSettlementReason { get; init; }
        public string? ManualSettledBy { get; init; }
        public decimal? ActualValue { get; init; }
        public decimal? ProfitLoss { get; init; }
        public DateTime? OutcomeAvailableUtc { get; init; }
        public DateTime EvaluatedAtUtc { get; init; }
    }

    private const string ResearchSql = ResearchCountSql + ResearchPageKeysSql + ResearchPageDetailsSql;

    private const string ResearchCountSql = "SET NOCOUNT ON; SELECT COUNT_BIG(*) "
        + ResearchAuditFilterSql + " OPTION (RECOMPILE);\n";

    // Shared count/page predicates keep general visibility separate from the
    // model decision and production publication, with optional filters only.
    private const string ResearchAuditFilterSql = """
        FROM dbo.AutomatedBotPickEvaluations AS evaluation
        CROSS APPLY
        (
            SELECT
                IsLegacyProductionGate = CONVERT(BIT, CASE
                    WHEN evaluation.Decision = N'Rejected'
                     AND evaluation.DecisionReasonsJson LIKE N'%REJECTED_PRODUCTION_GATE%'
                        THEN 1 ELSE 0 END),
                IsLegacyLowerRanked = CONVERT(BIT, CASE
                    WHEN evaluation.Decision = N'Rejected'
                     AND evaluation.DecisionReasonsJson LIKE N'%REJECTED_LOWER_RANKED_CANDIDATE%'
                        THEN 1 ELSE 0 END)
        ) AS legacy
        CROSS APPLY
        (
            SELECT ModelDecision = CASE
                WHEN legacy.IsLegacyProductionGate = 1
                  OR legacy.IsLegacyLowerRanked = 1 THEN N'Approved'
                ELSE evaluation.Decision
            END
        ) AS effective
        CROSS APPLY
        (
            SELECT PublicationStatus = CASE
                WHEN evaluation.PublishedSelectionId IS NOT NULL
                  OR ISNULL(evaluation.Published, 0) = 1 THEN N'Published'
                WHEN legacy.IsLegacyProductionGate = 1 THEN N'ProductionBlocked'
                WHEN legacy.IsLegacyLowerRanked = 1 THEN N'NotSelected'
                WHEN effective.ModelDecision = N'PendingData' THEN N'PendingData'
                WHEN effective.ModelDecision <> N'Approved' THEN N'ModelRejected'
                ELSE COALESCE(NULLIF(evaluation.PublicationStatus, N''), N'NotPublished')
            END
        ) AS publication
        WHERE ((@GeneralPicks = 0 AND evaluation.BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026', N'H2026'))
          OR (@GeneralPicks = 1 AND evaluation.BotKey NOT IN (N'G2026', N'I2026')))
          AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
          AND (@DateToExclusiveUtc IS NULL OR evaluation.MatchDate < @DateToExclusiveUtc)
          AND (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
          AND (@ModelDecision IS NULL OR effective.ModelDecision = @ModelDecision)
          AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
          AND
          (
              @MarketFamily IS NULL
              OR (@MarketFamily = N'CORNERS' AND evaluation.MarketType IN
                  (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners'))
              OR (@MarketFamily = N'GOALS' AND evaluation.MarketType IN
                  (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals'))
              OR (@MarketFamily = N'SHOTS' AND evaluation.MarketType IN
                  (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots'))
              OR (@MarketFamily = N'SOG' AND evaluation.MarketType IN
                  (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal'))
          )
          AND
          (
              @PublicationStatus IS NULL
              OR (@PublicationStatus = N'NotPublished'
                  AND publication.PublicationStatus <> N'Published')
              OR (@PublicationStatus <> N'NotPublished'
                  AND publication.PublicationStatus = @PublicationStatus)
          )
        """;

    private const string ResearchPageKeysSql = """
        -- Materialize just the page ids before reading JSON and official outcomes.
        -- With no decision/publication filter RECOMPILE removes the legacy applies
        -- from this narrow index read, even when the audit contains years of data.
        SELECT
                evaluation.AutomatedBotPickEvaluationId AS EvaluationId,
                evaluation.MatchDate
            INTO #ResearchPageKeys
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
            CROSS APPLY
            (
                SELECT
                    IsLegacyProductionGate = CONVERT(BIT, CASE
                        WHEN evaluation.Decision = N'Rejected'
                         AND evaluation.DecisionReasonsJson LIKE N'%REJECTED_PRODUCTION_GATE%'
                            THEN 1 ELSE 0 END),
                    IsLegacyLowerRanked = CONVERT(BIT, CASE
                        WHEN evaluation.Decision = N'Rejected'
                         AND evaluation.DecisionReasonsJson LIKE N'%REJECTED_LOWER_RANKED_CANDIDATE%'
                            THEN 1 ELSE 0 END)
            ) AS legacy
            CROSS APPLY
            (
                SELECT ModelDecision = CASE
                    WHEN legacy.IsLegacyProductionGate = 1
                      OR legacy.IsLegacyLowerRanked = 1 THEN N'Approved'
                    ELSE evaluation.Decision
                END
            ) AS effective
            CROSS APPLY
            (
                SELECT PublicationStatus = CASE
                    WHEN evaluation.PublishedSelectionId IS NOT NULL
                      OR ISNULL(evaluation.Published, 0) = 1 THEN N'Published'
                    WHEN legacy.IsLegacyProductionGate = 1 THEN N'ProductionBlocked'
                    WHEN legacy.IsLegacyLowerRanked = 1 THEN N'NotSelected'
                    WHEN effective.ModelDecision = N'PendingData' THEN N'PendingData'
                    WHEN effective.ModelDecision <> N'Approved' THEN N'ModelRejected'
                    ELSE COALESCE(NULLIF(evaluation.PublicationStatus, N''), N'NotPublished')
                END
            ) AS publication
            WHERE evaluation.BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026', N'H2026')
              AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
              AND (@DateToExclusiveUtc IS NULL OR evaluation.MatchDate < @DateToExclusiveUtc)
              AND (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
              AND (@ModelDecision IS NULL OR effective.ModelDecision = @ModelDecision)
              AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
              AND
              (
                  @MarketFamily IS NULL
                  OR (@MarketFamily = N'CORNERS' AND evaluation.MarketType IN
                      (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners'))
                  OR (@MarketFamily = N'GOALS' AND evaluation.MarketType IN
                      (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals'))
                  OR (@MarketFamily = N'SHOTS' AND evaluation.MarketType IN
                      (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots'))
                  OR (@MarketFamily = N'SOG' AND evaluation.MarketType IN
                      (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal'))
              )
              AND
              (
                  @PublicationStatus IS NULL
                  OR (@PublicationStatus = N'NotPublished'
                      AND publication.PublicationStatus <> N'Published')
                  OR (@PublicationStatus <> N'NotPublished'
                      AND publication.PublicationStatus = @PublicationStatus)
              )
            ORDER BY evaluation.MatchDate DESC,
                     evaluation.AutomatedBotPickEvaluationId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            OPTION (RECOMPILE);

        """;

    private const string ResearchPageDetailsSql = """
        -- Freeze the page before outcome resolution so the optimizer cannot
        -- expand any name/date fallback into the full evaluation ledger.
        SELECT
            evaluation.ApiFootballFixtureId,
            evaluation.AutomatedBotPickEvaluationId,
            evaluation.AutomationVersion,
            evaluation.AwayTeam,
            evaluation.BotKey,
            evaluation.ConfigurationVersion,
            evaluation.ContextAgreementScore,
            evaluation.DataQualityScore,
            evaluation.Decision,
            evaluation.DecisionReasonsJson,
            evaluation.EvaluatedAtUtc,
            evaluation.Explanation,
            evaluation.FeatureSchemaVersion,
            evaluation.FeatureSnapshotJson,
            evaluation.FinalEdge,
            evaluation.FinalExpectedValue,
            evaluation.FinalProbability,
            evaluation.GSelectionScore,
            evaluation.HomeTeam,
            evaluation.IsResearchWinner,
            evaluation.League,
            evaluation.LineValue,
            evaluation.MarketFamily,
            evaluation.MarketNoVigProbability,
            evaluation.MarketType,
            evaluation.MatchDate,
            evaluation.PredictionTimestampUtc,
            evaluation.ProductionDecision,
            evaluation.ProductionReason,
            evaluation.PublicationStatus,
            evaluation.Published,
            evaluation.PublishedSelectionId,
            evaluation.RawImpliedProbability,
            evaluation.RuleBasedConfidenceScore,
            evaluation.RunId,
            evaluation.SelectedOdds,
            evaluation.SelectedSide,
            evaluation.SelectionScore,
            evaluation.Source
        INTO #ResearchPageRows
        FROM #ResearchPageKeys AS page
        INNER JOIN dbo.AutomatedBotPickEvaluations AS evaluation
          ON evaluation.AutomatedBotPickEvaluationId = page.EvaluationId
        OPTION (RECOMPILE);

        -- Resolve repeated missing-id candidates once per fixture identity.
        SELECT DISTINCT
            HomeTeam,
            AwayTeam,
            MatchDateDay = CONVERT(DATE, MatchDate)
        INTO #ResearchFallbackScopes
        FROM #ResearchPageRows
        WHERE ApiFootballFixtureId IS NULL;

        -- Put the page's date boundary before name/collation matching. This
        -- prevents a correlated distinct count from scanning all fixture ids
        -- and looking up every historical match for each candidate.
        SELECT
            history.ApiFootballFixtureId,
            history.MatchDate,
            HomeTeam = COALESCE(NULLIF(history.StandardizedHomeTeam, N''), history.HomeTeam),
            AwayTeam = COALESCE(NULLIF(history.StandardizedAwayTeam, N''), history.AwayTeam)
        INTO #ResearchFallbackHistory
        FROM dbo.MatchHistory AS history
        WHERE history.ApiFootballFixtureId IS NOT NULL
          AND history.MatchDate >=
              (SELECT DATEADD(DAY, -1, MIN(MatchDateDay)) FROM #ResearchFallbackScopes)
          AND history.MatchDate <=
              (SELECT DATEADD(DAY, 1, MAX(MatchDateDay)) FROM #ResearchFallbackScopes)
        OPTION (RECOMPILE);

        -- Count distinct official ids, not history rows: duplicate records of
        -- one fixture are valid; two different fixtures remain ambiguous.
        SELECT
            scope.HomeTeam,
            scope.AwayTeam,
            scope.MatchDateDay,
            MatchCandidateCount = COUNT_BIG(DISTINCT history.ApiFootballFixtureId),
            ApiFootballFixtureId = MIN(history.ApiFootballFixtureId)
        INTO #ResearchFallbackIdentities
        FROM #ResearchFallbackScopes AS scope
        LEFT JOIN #ResearchFallbackHistory AS history
          ON history.MatchDate BETWEEN
              DATEADD(DAY, -1, scope.MatchDateDay)
              AND DATEADD(DAY, 1, scope.MatchDateDay)
         AND history.HomeTeam COLLATE Latin1_General_100_CI_AI =
              scope.HomeTeam COLLATE Latin1_General_100_CI_AI
         AND history.AwayTeam COLLATE Latin1_General_100_CI_AI =
              scope.AwayTeam COLLATE Latin1_General_100_CI_AI
        GROUP BY scope.HomeTeam, scope.AwayTeam, scope.MatchDateDay;

        SELECT
            EvaluationId = evaluation.AutomatedBotPickEvaluationId,
            evaluation.RunId,
            evaluation.BotKey,
            evaluation.AutomationVersion,
            evaluation.MatchDate,
            evaluation.League,
            evaluation.HomeTeam,
            evaluation.AwayTeam,
            evaluation.Source,
            MarketFamily = CASE
                WHEN evaluation.MarketType IN
                    (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners') THEN N'CORNERS'
                WHEN evaluation.MarketType IN
                    (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals') THEN N'GOALS'
                WHEN evaluation.MarketType IN
                    (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots') THEN N'SHOTS'
                WHEN evaluation.MarketType IN
                    (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal') THEN N'SOG'
                ELSE COALESCE(NULLIF(evaluation.MarketFamily, N''), N'UNKNOWN')
            END,
            evaluation.MarketType,
            evaluation.LineValue,
            evaluation.SelectedSide,
            evaluation.SelectedOdds,
            ModelDecision = effective.ModelDecision,
            ModelDecisionReasonsJson = evaluation.DecisionReasonsJson,
            ModelExplanation = NULLIF(evaluation.Explanation, N''),
            PublicationStatus = publication.PublicationStatus,
            ProductionDecision = CASE
                WHEN publication.PublicationStatus = N'Published' THEN N'Published'
                WHEN legacy.IsLegacyProductionGate = 1 THEN N'Blocked'
                WHEN legacy.IsLegacyLowerRanked = 1 THEN N'LowerRanked'
                WHEN effective.ModelDecision = N'PendingData' THEN N'NotEvaluated'
                WHEN effective.ModelDecision <> N'Approved' THEN N'ModelRejected'
                ELSE COALESCE(
                    NULLIF(evaluation.ProductionDecision, N''),
                    CASE
                        WHEN publication.PublicationStatus = N'Shadow' THEN N'Shadow'
                        WHEN publication.PublicationStatus = N'NotSelected' THEN N'LowerRanked'
                        WHEN publication.PublicationStatus = N'ProductionBlocked' THEN N'Blocked'
                        WHEN publication.PublicationStatus = N'Eligible' THEN N'Eligible'
                        ELSE N'NotPublished'
                    END)
            END,
            ProductionReason = COALESCE(
                NULLIF(evaluation.ProductionReason, N''),
                NULLIF(evaluation.Explanation, N''),
                CASE
                    WHEN publication.PublicationStatus = N'Published'
                        THEN N'Published to production.'
                    WHEN effective.ModelDecision = N'Approved'
                        THEN N'Legacy evaluation: the production reason was not recorded.'
                END),
            IsResearchWinner = ISNULL(evaluation.IsResearchWinner, 0),
            evaluation.PublishedSelectionId,
            evaluation.FinalProbability,
            MarketProbability = COALESCE(
                evaluation.MarketNoVigProbability,
                evaluation.RawImpliedProbability),
            evaluation.FinalEdge,
            evaluation.FinalExpectedValue,
            SelectionScore = COALESCE(
                evaluation.SelectionScore,
                evaluation.GSelectionScore,
                evaluation.RuleBasedConfidenceScore),
            evaluation.DataQualityScore,
            evaluation.ContextAgreementScore,
            evaluation.ConfigurationVersion,
            evaluation.FeatureSchemaVersion,
            evaluation.FeatureSnapshotJson,
            OutcomeStatus = CASE
                WHEN manual.OutcomeStatus = N'Void' THEN N'Void'
                WHEN COALESCE(manual.ActualValue, official.ActualValue) IS NULL THEN
                    CASE WHEN evaluation.MatchDate > SYSUTCDATETIME()
                        THEN N'Pending' ELSE N'Unavailable' END
                WHEN settlement.SettlementFactor IS NULL THEN N'Official'
                WHEN settlement.SettlementFactor = 1.0000 THEN N'Win'
                WHEN settlement.SettlementFactor = 0.5000 THEN N'HalfWin'
                WHEN settlement.SettlementFactor = 0.0000 THEN N'Push'
                WHEN settlement.SettlementFactor = -0.5000 THEN N'HalfLoss'
                WHEN settlement.SettlementFactor = -1.0000 THEN N'Loss'
                ELSE N'Official'
            END,
            OutcomeSource = CASE WHEN manual.OutcomeStatus IS NOT NULL THEN N'Manual'
                WHEN official.ActualValue IS NOT NULL THEN N'ApiFootball' ELSE NULL END,
            ManualSettlementReason = manual.Reason,
            ManualSettledBy = manual.SettledBy,
            ActualValue = CONVERT(DECIMAL(12,4), COALESCE(manual.ActualValue, official.ActualValue)),
            ProfitLoss = CONVERT(DECIMAL(12,4), CASE
                WHEN manual.OutcomeStatus = N'Void' THEN 0
                WHEN settlement.SettlementFactor > 0 AND evaluation.SelectedOdds > 1
                    THEN settlement.SettlementFactor * (evaluation.SelectedOdds - 1.0)
                WHEN settlement.SettlementFactor <= 0
                    THEN settlement.SettlementFactor
            END),
            OutcomeAvailableUtc = COALESCE(manual.SettledAtUtc, official.OutcomeAvailableUtc),
            evaluation.EvaluatedAtUtc
        FROM #ResearchPageRows AS evaluation
        """ + GeneralManualOutcomeApplySql + """
            CROSS APPLY
            (
                SELECT
                    IsLegacyProductionGate = CONVERT(BIT, CASE
                        WHEN evaluation.Decision = N'Rejected'
                         AND evaluation.DecisionReasonsJson LIKE N'%REJECTED_PRODUCTION_GATE%'
                            THEN 1 ELSE 0 END),
                    IsLegacyLowerRanked = CONVERT(BIT, CASE
                        WHEN evaluation.Decision = N'Rejected'
                         AND evaluation.DecisionReasonsJson LIKE N'%REJECTED_LOWER_RANKED_CANDIDATE%'
                            THEN 1 ELSE 0 END)
            ) AS legacy
            CROSS APPLY
            (
                SELECT ModelDecision = CASE
                    WHEN legacy.IsLegacyProductionGate = 1
                      OR legacy.IsLegacyLowerRanked = 1 THEN N'Approved'
                    ELSE evaluation.Decision
                END
            ) AS effective
            CROSS APPLY
            (
                SELECT PublicationStatus = CASE
                    WHEN evaluation.PublishedSelectionId IS NOT NULL
                      OR ISNULL(evaluation.Published, 0) = 1 THEN N'Published'
                    WHEN legacy.IsLegacyProductionGate = 1 THEN N'ProductionBlocked'
                    WHEN legacy.IsLegacyLowerRanked = 1 THEN N'NotSelected'
                    WHEN effective.ModelDecision = N'PendingData' THEN N'PendingData'
                    WHEN effective.ModelDecision <> N'Approved' THEN N'ModelRejected'
                    ELSE COALESCE(NULLIF(evaluation.PublicationStatus, N''), N'NotPublished')
                END
            ) AS publication
        LEFT JOIN #ResearchFallbackIdentities AS fallbackIdentity
          ON evaluation.ApiFootballFixtureId IS NULL
         AND fallbackIdentity.HomeTeam = evaluation.HomeTeam
         AND fallbackIdentity.AwayTeam = evaluation.AwayTeam
         AND fallbackIdentity.MatchDateDay = CONVERT(DATE, evaluation.MatchDate)
        OUTER APPLY
        (
            SELECT ResolvedFixtureId = CASE
                -- An exact API-Football id is authoritative; mutable display
                -- names or corrected kickoff times cannot invalidate it.
                WHEN evaluation.ApiFootballFixtureId IS NOT NULL
                    THEN evaluation.ApiFootballFixtureId
                WHEN fallbackIdentity.MatchCandidateCount = 1
                    THEN fallbackIdentity.ApiFootballFixtureId
            END
        ) AS resolvedIdentity
        OUTER APPLY
        (
            SELECT TOP (1)
                ActualValue = CONVERT(DECIMAL(12,4), CASE evaluation.MarketType
                    WHEN N'TotalGoals' THEN history.HomeGoals + history.AwayGoals
                    WHEN N'HomeTeamGoals' THEN history.HomeGoals
                    WHEN N'AwayTeamGoals' THEN history.AwayGoals
                    WHEN N'TotalCorners' THEN history.HomeCorners + history.AwayCorners
                    WHEN N'HomeTeamCorners' THEN history.HomeCorners
                    WHEN N'AwayTeamCorners' THEN history.AwayCorners
                    WHEN N'TotalShots' THEN history.HomeShots + history.AwayShots
                    WHEN N'HomeTeamShots' THEN history.HomeShots
                    WHEN N'AwayTeamShots' THEN history.AwayShots
                    WHEN N'TotalShotsOnGoal' THEN history.HomeShotsOnGoal + history.AwayShotsOnGoal
                    WHEN N'HomeTeamShotsOnGoal' THEN history.HomeShotsOnGoal
                    WHEN N'AwayTeamShotsOnGoal' THEN history.AwayShotsOnGoal
                END),
                OutcomeAvailableUtc = history.ApiFootballUpdatedAtUtc
            FROM dbo.MatchHistory AS history
            WHERE resolvedIdentity.ResolvedFixtureId IS NOT NULL
              AND history.ApiFootballFixtureId = resolvedIdentity.ResolvedFixtureId
              AND COALESCE(
                    evaluation.PredictionTimestampUtc,
                    evaluation.EvaluatedAtUtc) < evaluation.MatchDate
              AND history.ApiFootballUpdatedAtUtc > COALESCE(
                    evaluation.PredictionTimestampUtc,
                    evaluation.EvaluatedAtUtc)
              AND history.ApiFootballUpdatedAtUtc <= SYSUTCDATETIME()
              -- H outcome economics are exposed only for rows whose immutable
              -- decision-time snapshot was accepted by the shadow laboratory.
              AND
              (
                  evaluation.BotKey <> N'H2026'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.BotH2026ShadowEvaluations AS shadow
                      WHERE shadow.SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId
                  )
              )
              AND UPPER(LTRIM(RTRIM(COALESCE(history.FixtureStatus, N''))))
                  IN (N'FT', N'AET', N'PEN')
              AND
              (
                  (evaluation.MarketType IN
                      (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals')
                      AND ISNULL(history.ApiFootballGoalsAvailable, 0) = 1)
                  OR
                  (evaluation.MarketType IN
                      (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')
                      AND ISNULL(history.ApiFootballCornersAvailable, 0) = 1)
                  OR
                  (evaluation.MarketType IN
                      (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots')
                      AND ISNULL(history.ApiFootballShotsAvailable, 0) = 1)
                  OR
                  (evaluation.MarketType IN
                      (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal')
                      AND ISNULL(history.ApiFootballShotsOnGoalAvailable, 0) = 1)
              )
              AND CASE evaluation.MarketType
                    WHEN N'TotalGoals' THEN history.HomeGoals + history.AwayGoals
                    WHEN N'HomeTeamGoals' THEN history.HomeGoals
                    WHEN N'AwayTeamGoals' THEN history.AwayGoals
                    WHEN N'TotalCorners' THEN history.HomeCorners + history.AwayCorners
                    WHEN N'HomeTeamCorners' THEN history.HomeCorners
                    WHEN N'AwayTeamCorners' THEN history.AwayCorners
                    WHEN N'TotalShots' THEN history.HomeShots + history.AwayShots
                    WHEN N'HomeTeamShots' THEN history.HomeShots
                    WHEN N'AwayTeamShots' THEN history.AwayShots
                    WHEN N'TotalShotsOnGoal' THEN history.HomeShotsOnGoal + history.AwayShotsOnGoal
                    WHEN N'HomeTeamShotsOnGoal' THEN history.HomeShotsOnGoal
                    WHEN N'AwayTeamShotsOnGoal' THEN history.AwayShotsOnGoal
                  END IS NOT NULL
            ORDER BY
                history.ApiFootballUpdatedAtUtc DESC,
                history.Id DESC
        ) AS official
        OUTER APPLY
        (
            SELECT
                FirstLine = CASE
                    WHEN evaluation.LineValue - FLOOR(evaluation.LineValue) IN (0.25, 0.75)
                        THEN evaluation.LineValue - 0.25
                    ELSE evaluation.LineValue END,
                SecondLine = CASE
                    WHEN evaluation.LineValue - FLOOR(evaluation.LineValue) IN (0.25, 0.75)
                        THEN evaluation.LineValue + 0.25
                    ELSE evaluation.LineValue END
        ) AS split
        OUTER APPLY
        (
            SELECT SettlementFactor = CONVERT(DECIMAL(9,4), CASE
                WHEN COALESCE(manual.ActualValue, official.ActualValue) IS NULL
                  OR evaluation.SelectedSide NOT IN (N'Over', N'Under') THEN NULL
                ELSE
                (
                    CONVERT(DECIMAL(9,4), CASE evaluation.SelectedSide
                        WHEN N'Over' THEN CASE
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) > split.FirstLine THEN 1.0
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) = split.FirstLine THEN 0.0
                            ELSE -1.0 END
                        WHEN N'Under' THEN CASE
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) < split.FirstLine THEN 1.0
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) = split.FirstLine THEN 0.0
                            ELSE -1.0 END
                    END)
                    +
                    CONVERT(DECIMAL(9,4), CASE evaluation.SelectedSide
                        WHEN N'Over' THEN CASE
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) > split.SecondLine THEN 1.0
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) = split.SecondLine THEN 0.0
                            ELSE -1.0 END
                        WHEN N'Under' THEN CASE
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) < split.SecondLine THEN 1.0
                            WHEN COALESCE(manual.ActualValue, official.ActualValue) = split.SecondLine THEN 0.0
                            ELSE -1.0 END
                    END)
                ) / 2.0
            END)
        ) AS settlement
        ORDER BY evaluation.MatchDate DESC, evaluation.AutomatedBotPickEvaluationId DESC
        OPTION (RECOMPILE);
        """;
}
