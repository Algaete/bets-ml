using CornersPrediction.Application.AutomatedCorners;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed partial class SqlServerAutomatedBotResearchRepository
    : IAutomatedBotGeneralPicksRepository, IGeneralPickEvidenceRepository, IGeneralPickLabRepository
{
    public async Task<GeneralPickEvidence?> GetEvidenceAsync(long evaluationId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<GeneralPickEvidence>(new CommandDefinition("""
            SELECT FeatureSnapshotJson,
                ModelDecisionReasonsJson = DecisionReasonsJson,
                ModelExplanation = NULLIF(Explanation, N'')
            FROM dbo.AutomatedBotPickEvaluations
            WHERE AutomatedBotPickEvaluationId = @EvaluationId
              AND BotKey NOT IN (N'G2026', N'I2026');
            """, new { EvaluationId = evaluationId }, commandTimeout: 60, cancellationToken: cancellationToken));
    }

    public async Task<AutomatedBotResearchPage> GetGeneralPicksAsync(
        AutomatedBotResearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parameters = new
        {
            GeneralPicks = true,
            query.DateFromUtc,
            query.DateToExclusiveUtc,
            query.MarketFamily,
            query.MarketType,
            query.BotKey,
            query.ModelDecision,
            query.PublicationStatus,
            query.SortBy,
            Offset = checked((long)(query.Page - 1) * query.PageSize),
            query.PageSize
        };
        await using var connection = new SqlConnection(_connectionString);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            BuildGeneralPicksQuery(query),
            parameters, commandTimeout: 120,
            cancellationToken: cancellationToken));
        var totalCount = await results.ReadSingleAsync<long>();
        var audit = (await results.ReadAsync<ResearchRow>()).Select(Map).ToArray();
        var published = (await results.ReadAsync<ResearchRow>())
            .Select(row => Map(row) with { RecordKind = "PublishedSelection" }).ToArray();
        var definitions = (await results.ReadAsync<AutomatedBotGeneralPickBotDto>()).AsList();
        var positions = (await results.ReadAsync<long>()).Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);
        var items = audit.Concat(published)
            .OrderBy(row => positions[row.EvaluationId])
            .ToArray();
        // Historical rows can outlive their editable definition. Keep their
        // keys selectable without DISTINCT over the full evaluation ledger.
        var bots = definitions.Concat(items.Select(row =>
                new AutomatedBotGeneralPickBotDto(row.BotKey, $"Bot {row.BotKey}")))
            .Concat(query.BotKey is null ? [] :
                [new AutomatedBotGeneralPickBotDto(query.BotKey, $"Bot {query.BotKey}")])
            .DistinctBy(bot => bot.BotKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(bot => bot.BotKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Min(int.MaxValue,
            (totalCount + query.PageSize - 1L) / query.PageSize);
        return new AutomatedBotResearchPage(items, totalCount,
            query.Page, query.PageSize, totalPages) { AvailableBots = bots };
    }

    // Restrict counts and sorting to the covering metadata index. In particular,
    // do not expand the legacy reason JSON into a scan of the model snapshots.
    private const string GeneralAuditScopeSql = """
        FROM dbo.AutomatedBotPickEvaluations AS evaluation
            WITH (INDEX(IX_AutomatedBotPickEvaluations_ResearchPage))
        WHERE evaluation.BotKey NOT IN (N'G2026', N'I2026')
          AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
          AND (@DateToExclusiveUtc IS NULL OR evaluation.MatchDate < @DateToExclusiveUtc)
          AND (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
          AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
          AND
          (
              @ModelDecision IS NULL
              OR (@ModelDecision = N'Approved' AND
                  (evaluation.Decision = N'Approved'
                   OR (evaluation.Decision = N'Rejected'
                       AND evaluation.PerformanceLegacyPublicationRejection = 1)))
              OR (@ModelDecision = N'Rejected'
                  AND evaluation.Decision = N'Rejected'
                  AND evaluation.PerformanceLegacyPublicationRejection = 0)
              OR (@ModelDecision NOT IN (N'Approved', N'Rejected')
                  AND evaluation.Decision = @ModelDecision)
          )
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

        """;

    // Freeze the narrow metadata universe before touching the reason JSON.
    // Only legacy publication rejections need that JSON to distinguish a
    // production gate from a lower-ranked candidate; modern rows do not.
    private const string GeneralFilteredAuditKeysSql = """
        SELECT EvaluationId = evaluation.AutomatedBotPickEvaluationId,
            evaluation.MatchDate,
            ModelDecision = evaluation.Decision,
            PublicationStatus = CASE
                WHEN evaluation.PublishedSelectionId IS NOT NULL
                  OR ISNULL(evaluation.Published, 0) = 1 THEN N'Published'
                WHEN evaluation.Decision = N'PendingData' THEN N'PendingData'
                WHEN evaluation.Decision <> N'Approved' THEN N'ModelRejected'
                ELSE COALESCE(NULLIF(evaluation.PublicationStatus, N''), N'NotPublished')
            END,
            IsLegacyRejection = CONVERT(BIT, CASE
                WHEN evaluation.Decision = N'Rejected'
                  AND evaluation.PerformanceLegacyPublicationRejection = 1 THEN 1
                ELSE 0 END)
        INTO #GeneralAuditCandidates
        """ + "\n" + GeneralAuditScopeSql + """

        OPTION (RECOMPILE);

        SELECT candidate.EvaluationId,
            IsProductionGate = CONVERT(BIT, CASE
                WHEN evaluation.DecisionReasonsJson LIKE N'%REJECTED_PRODUCTION_GATE%'
                    THEN 1 ELSE 0 END)
        INTO #GeneralLegacyStates
        FROM #GeneralAuditCandidates AS candidate
        INNER JOIN dbo.AutomatedBotPickEvaluations AS evaluation
          ON evaluation.AutomatedBotPickEvaluationId = candidate.EvaluationId
        WHERE candidate.IsLegacyRejection = 1
          AND candidate.PublicationStatus <> N'Published'
          AND (@SortBy = N'PublicationStatus' OR @PublicationStatus IN (N'ProductionBlocked', N'NotSelected'))
          AND (@ModelDecision IS NULL OR @ModelDecision = N'Approved')
        OPTION (RECOMPILE);

        SELECT candidate.EvaluationId, candidate.MatchDate,
            effective.ModelDecision, effective.PublicationStatus
        INTO #GeneralAuditKeys
        FROM #GeneralAuditCandidates AS candidate
        LEFT JOIN #GeneralLegacyStates AS legacy
          ON legacy.EvaluationId = candidate.EvaluationId
        CROSS APPLY
        (
            SELECT ModelDecision = CASE WHEN candidate.IsLegacyRejection = 1
                THEN N'Approved' ELSE candidate.ModelDecision END,
                PublicationStatus = CASE
                    WHEN candidate.PublicationStatus = N'Published' THEN N'Published'
                    WHEN legacy.IsProductionGate = 1 THEN N'ProductionBlocked'
                    WHEN candidate.IsLegacyRejection = 1 THEN N'NotSelected'
                    ELSE candidate.PublicationStatus END
        ) AS effective
        WHERE (@ModelDecision IS NULL OR effective.ModelDecision = @ModelDecision)
          AND
          (
              @PublicationStatus IS NULL
              OR (@PublicationStatus = N'NotPublished'
                  AND effective.PublicationStatus <> N'Published')
              OR (@PublicationStatus <> N'NotPublished'
                  AND effective.PublicationStatus = @PublicationStatus)
          )
        OPTION (RECOMPILE);

        """;

    // The General view opens on model-approved picks. Build its final key set
    // in one pass so SQL Server does not create statistics for two intermediate
    // temp tables before it can return the first page.
    private const string GeneralApprovedAuditKeysSql = """
        SELECT approved.EvaluationId, approved.MatchDate,
            ModelDecision = CONVERT(NVARCHAR(30), N'Approved'),
            approved.PublicationStatus
        INTO #GeneralAuditKeys
        FROM
        (
            SELECT EvaluationId = evaluation.AutomatedBotPickEvaluationId,
                evaluation.MatchDate,
                PublicationStatus = CONVERT(NVARCHAR(30), CASE
                    WHEN evaluation.PublishedSelectionId IS NOT NULL
                      OR ISNULL(evaluation.Published, 0) = 1 THEN N'Published'
                    ELSE COALESCE(NULLIF(evaluation.PublicationStatus, N''), N'NotPublished')
                END)
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
                WITH (INDEX(IX_AutomatedBotPickEvaluations_GeneralDecisionPage))
            WHERE evaluation.Decision = N'Approved'
              AND evaluation.BotKey NOT IN (N'G2026', N'I2026')
              AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
              AND (@DateToExclusiveUtc IS NULL OR evaluation.MatchDate < @DateToExclusiveUtc)
              AND (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
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

            UNION ALL

            SELECT EvaluationId = evaluation.AutomatedBotPickEvaluationId,
                evaluation.MatchDate,
                PublicationStatus = CONVERT(NVARCHAR(30), CASE
                    WHEN evaluation.PublishedSelectionId IS NOT NULL
                      OR ISNULL(evaluation.Published, 0) = 1 THEN N'Published'
                    WHEN evaluation.DecisionReasonsJson LIKE N'%REJECTED_PRODUCTION_GATE%'
                        THEN N'ProductionBlocked'
                    ELSE N'NotSelected'
                END)
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
                WITH (INDEX(IX_AutomatedBotPickEvaluations_GeneralDecisionPage))
            WHERE evaluation.Decision = N'Rejected'
              AND evaluation.PerformanceLegacyPublicationRejection = 1
              AND evaluation.BotKey NOT IN (N'G2026', N'I2026')
              AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
              AND (@DateToExclusiveUtc IS NULL OR evaluation.MatchDate < @DateToExclusiveUtc)
              AND (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
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
        ) AS approved
        OPTION (RECOMPILE);

        CREATE UNIQUE CLUSTERED INDEX IX_GeneralApprovedAuditKeys
            ON #GeneralAuditKeys(EvaluationId);

        """;

    private const string GeneralBotCatalogSql = """

        SELECT BotKey, DisplayName
        FROM dbo.AutomatedBotDefinitions
        WHERE BotKey NOT IN (N'G2026', N'I2026')
        UNION
        SELECT selection.BotKey, CONCAT(N'Bot ', selection.BotKey)
        FROM #GeneralPublishedKeys AS page
        INNER JOIN dbo.AutomatedCornerBetSelections AS selection
          ON selection.AutomatedCornerBetSelectionId = -page.EvaluationId
        WHERE NOT EXISTS (SELECT 1 FROM dbo.AutomatedBotDefinitions AS definition
            WHERE definition.BotKey = selection.BotKey)
        ORDER BY BotKey;
        """;

    private const string GeneralPublishedKeysSql = """
        SELECT EvaluationId = -selection.AutomatedCornerBetSelectionId,
            selection.MatchDate
        INTO #GeneralPublishedKeys
        FROM dbo.AutomatedCornerBetSelections AS selection
        WHERE selection.BotKey NOT IN (N'G2026', N'I2026')
          AND (@DateFromUtc IS NULL OR selection.MatchDate >= @DateFromUtc)
          AND (@DateToExclusiveUtc IS NULL OR selection.MatchDate < @DateToExclusiveUtc)
          AND (@BotKey IS NULL OR selection.BotKey = @BotKey)
          AND (@ModelDecision IS NULL OR @ModelDecision = N'NotRecorded')
          AND (@PublicationStatus IS NULL OR @PublicationStatus = N'Published')
          AND (@MarketType IS NULL OR selection.MarketType = @MarketType)
          AND
          (
              @MarketFamily IS NULL
              OR (@MarketFamily = N'CORNERS' AND selection.MarketType IN
                  (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners'))
              OR (@MarketFamily = N'GOALS' AND selection.MarketType IN
                  (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals'))
              OR (@MarketFamily = N'SHOTS' AND selection.MarketType IN
                  (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots'))
              OR (@MarketFamily = N'SOG' AND selection.MarketType IN
                  (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal'))
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.AutomatedBotPickEvaluations AS linked
              WHERE linked.PublishedSelectionId = selection.AutomatedCornerBetSelectionId
                AND linked.BotKey NOT IN (N'G2026', N'I2026')
          )
        OPTION (RECOMPILE);

        """;

    // An archived selection is evidence of publication, not evidence that a
    // missing model audit was Approved. Its stored result is normalized to the
    // one-unit economics used by research, without touching settlement records.
    private const string GeneralPublishedDetailsSql = """

        SELECT EvaluationId = -selection.AutomatedCornerBetSelectionId,
            selection.RunId,
            selection.BotKey,
            selection.AutomationVersion,
            selection.MatchDate,
            League = COALESCE(NULLIF(selection.StandardizedLeague, N''), selection.League),
            HomeTeam = COALESCE(NULLIF(selection.StandardizedHomeTeam, N''), selection.HomeTeam),
            AwayTeam = COALESCE(NULLIF(selection.StandardizedAwayTeam, N''), selection.AwayTeam),
            selection.Source,
            MarketFamily = CASE
                WHEN selection.MarketType IN
                    (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners') THEN N'CORNERS'
                WHEN selection.MarketType IN
                    (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals') THEN N'GOALS'
                WHEN selection.MarketType IN
                    (N'TotalShots', N'HomeTeamShots', N'AwayTeamShots') THEN N'SHOTS'
                WHEN selection.MarketType IN
                    (N'TotalShotsOnGoal', N'HomeTeamShotsOnGoal', N'AwayTeamShotsOnGoal') THEN N'SOG'
                ELSE N'UNKNOWN'
            END,
            selection.MarketType,
            selection.LineValue,
            selection.SelectedSide,
            SelectedOdds = selection.Odds,
            ModelDecision = N'NotRecorded',
            ModelDecisionReasonsJson = N'[]',
            ModelExplanation = N'La selección publicada no tiene una evaluación de modelo enlazada.',
            PublicationStatus = N'Published',
            ProductionDecision = N'Published',
            ProductionReason = selection.DecisionReason,
            IsResearchWinner = CONVERT(BIT, 0),
            PublishedSelectionId = selection.AutomatedCornerBetSelectionId,
            FinalProbability = selection.ModelProbability,
            MarketProbability = selection.ImpliedProbability,
            FinalEdge = selection.ProbabilityEdge,
            FinalExpectedValue = selection.ExpectedValue,
            selection.SelectionScore,
            ConfigurationVersion = N'',
            FeatureSchemaVersion = N'',
            FeatureSnapshotJson = N'{}',
            OutcomeStatus = CASE
                WHEN selection.Status = N'Won' AND selection.SettlementFactor = 0.5000
                    THEN N'HalfWin'
                WHEN selection.Status = N'Lost' AND selection.SettlementFactor = -0.5000
                    THEN N'HalfLoss'
                ELSE CASE selection.Status
                WHEN N'Won' THEN N'Win'
                WHEN N'Lost' THEN N'Loss'
                WHEN N'HalfWon' THEN N'HalfWin'
                WHEN N'HalfLost' THEN N'HalfLoss'
                ELSE selection.Status
                END
            END,
            OutcomeSource = selection.SettlementSource,
            ManualSettlementReason = CASE WHEN selection.SettlementSource = N'Manual'
                THEN COALESCE(entered.Reason, selection.SettlementReason) END,
            ManualSettledBy = entered.SettledBy,
            ActualValue = CONVERT(DECIMAL(12,4), selection.SettlementActualValue),
            ProfitLoss = CONVERT(DECIMAL(12,4), CASE
                WHEN selection.Status = N'Pending' THEN NULL
                WHEN selection.Stake > 0 THEN selection.ProfitLoss / selection.Stake
            END),
            OutcomeAvailableUtc = selection.SettledAtUtc,
            EvaluatedAtUtc = selection.CreatedAtUtc
        FROM #ResearchPageKeys AS page
        INNER JOIN dbo.AutomatedCornerBetSelections AS selection
          ON selection.AutomatedCornerBetSelectionId = -page.EvaluationId
        OUTER APPLY
        (
            SELECT TOP (1) Reason, SettledBy FROM dbo.GeneralBotPickManualSettlements
            WHERE RecordId = -selection.AutomatedCornerBetSelectionId ORDER BY Id DESC
        ) AS entered
        WHERE page.EvaluationId < 0
        ORDER BY selection.MatchDate DESC, EvaluationId DESC
        OPTION (RECOMPILE);
        """;
}
